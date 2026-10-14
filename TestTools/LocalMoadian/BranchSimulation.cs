using Prg_Moadian.Bulk;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.Generaly;
using Prg_Moadian.SQLMODELS;
using TaxCollectData.Library.Dto.Content;

namespace MoadianLocalTest;

/// <summary>
/// شبیه‌سازی فقط‌ساختنی (بدون ارسال، بدون INSERT در TAXDTL): برای هر فاکتوری که در
/// سامانهٔ اصلی SUCCESS شده، صورتحساب را با هر دو شاخهٔ ارسال گروهی می‌سازد —
/// MrCorrect (سرگروه فاکتور) و دناافراز (حواله) — و با آنچه واقعاً پذیرفته شده
/// مقایسه می‌کند. اصلاح CheckIsDenafaraz مشتری‌های MrCorrect را از شاخهٔ دوم به
/// اولی می‌برد؛ این شبیه‌سازی نشان می‌دهد چه چیزی عوض می‌شود.
///
/// اجرا: MoadianLocalTest.exe --simulate-branches http://127.0.0.1:PORT/
/// (سرور ساختگی باید بالا باشد؛ سازندهٔ SendInvoiceBulk توکن می‌گیرد.)
/// </summary>
internal static class BranchSimulation
{
    private sealed class Accepted
    {
        public double NUMBER { get; set; }
        public string Taxid { get; set; } = "";
        public long Indatim_Sec { get; set; }
        public long Indati2m_Sec { get; set; }
        public string? Tinb { get; set; }
        public int? Tob { get; set; }
        public decimal? Tbill { get; set; }
        public decimal? Tvam { get; set; }
        public decimal? Tadis { get; set; }
        public string? Sstid { get; set; }
        public decimal? Am { get; set; }
        public decimal? Fee { get; set; }
        public decimal? Vam { get; set; }
        public string? REMARKS { get; set; }
    }

    public static int Run(string mockUrl)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var db = new CL_CCNNMANAGER();
        string dbName = db.DoGetDataSQL<string>("SELECT DB_NAME()").First();

        // آخرین ارسال موفقِ هر فاکتور اصلی در سامانهٔ اصلی
        var rows = db.DoGetDataSQL<Accepted>(@"
            WITH last AS (
              SELECT NUMBER, MAX(IDD) AS maxidd FROM dbo.TAXDTL
              WHERE TheStatus='SUCCESS' AND ISNULL(Ins,1)=1 AND ApiTypeSent=1 AND TAG=2
              GROUP BY NUMBER),
            tx AS (SELECT t.NUMBER, t.Taxid FROM dbo.TAXDTL t JOIN last l ON l.maxidd = t.IDD)
            SELECT t.NUMBER, t.Taxid, t.Indatim_Sec, t.Indati2m_Sec, t.Tinb, t.Tob, t.Tbill, t.Tvam, t.Tadis,
                   t.Sstid, t.Am, t.Fee, t.Vam, t.REMARKS
            FROM dbo.TAXDTL t JOIN tx ON tx.Taxid = t.Taxid
            OPTION (MAXDOP 1)").ToList();
        var byInvoice = rows.GroupBy(r => (long)r.NUMBER).ToList();
        var hasHead13 = db.DoGetDataSQL<double>("SELECT NUMBER FROM dbo.HEAD_LST WHERE TAG=13")
                          .Select(x => (long)x).ToHashSet();

        long taxdtlBefore = db.DoGetDataSQL<long>("SELECT COUNT_BIG(*) FROM dbo.TAXDTL").First();
        long extBefore = db.DoGetDataSQL<long>("SELECT COUNT_BIG(*) FROM dbo.HEAD_LST_EXTENDED").First();

        // DryRun: ساختن فاکتور هیچ‌چیز در دیتابیس نمی‌نویسد (نه HEAD_LST_EXTENDED، نه LOCAL_ERROR).
        var bulk = new SendInvoiceBulk(db, mockUrl) { OnValidationWarning = _ => true, DryRun = true };

        InvoiceDto? Build(long number, string product, out string err)
        {
            err = "";
            string saved = CL_Generaly.MrCorrect;
            try
            {
                CL_Generaly.MrCorrect = product;       // bulk.CALLER_NAME = "m": "m"→MrCorrect، "d"→دناافراز
                return bulk.BuildDtoAndRecords(number, 2, null, null, false, null, true).Dto;
            }
            catch (Exception e) { err = e.Message.Replace("\n", " "); return null; }
            finally { CL_Generaly.MrCorrect = saved; }
        }

        var iran = TimeSpan.FromHours(3.5);
        DateTime Day(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).ToOffset(iran).Date;

        // دسته‌ها
        int total = 0, noHead13 = 0;
        var counters = new Dictionary<string, int>();
        void Inc(string k) => counters[k] = counters.TryGetValue(k, out var v) ? v + 1 : 1;
        var samples = new Dictionary<string, List<string>>();
        void Sample(string k, string s) { if (!samples.TryGetValue(k, out var l)) samples[k] = l = new(); if (l.Count < 5) l.Add(s); }

        List<string> Diff(InvoiceDto dto, List<Accepted> acc, bool compareDate)
        {
            var d = new List<string>();
            var a = acc.First();
            var h = dto.Header;
            if (compareDate && Day(h.Indatim ?? 0) != Day(a.Indatim_Sec)) d.Add("date");
            if ((h.Tinb ?? "").Trim() != (a.Tinb ?? "").Trim()) d.Add("tinb");
            if ((h.Tob ?? 0) != (a.Tob ?? 0)) d.Add("tob");
            if ((h.Tbill ?? 0) != (a.Tbill ?? 0)) d.Add("tbill");
            if ((h.Tvam ?? 0) != (a.Tvam ?? 0)) d.Add("tvam");
            if ((h.Tadis ?? 0) != (a.Tadis ?? 0)) d.Add("tadis");
            var bodies = dto.Body ?? new List<InvoiceBodyDto>();
            if (bodies.Count != acc.Count) d.Add($"items {acc.Count}->{bodies.Count}");
            else
            {
                var x = bodies.Select(b => $"{b.Sstid}|{b.Am:0.####}|{b.Fee:0.####}|{b.Vam:0}").OrderBy(s => s).ToList();
                var y = acc.Select(b => $"{b.Sstid}|{b.Am:0.####}|{b.Fee:0.####}|{b.Vam:0}").OrderBy(s => s).ToList();
                if (!x.SequenceEqual(y)) d.Add("items");
            }
            return d;
        }

        foreach (var g in byInvoice)
        {
            total++;
            long number = g.Key;
            var acc = g.ToList();
            string src = acc.First().REMARKS ?? "single";
            if (!hasHead13.Contains(number)) { noHead13++; Inc($"[{src}] NO tag13 header"); }

            var mr = Build(number, "m", out var mrErr);
            var dn = Build(number, "d", out var dnErr);

            if (mr == null) { Inc($"[{src}] MrCorrect build FAILED"); Sample($"[{src}] MrCorrect build FAILED", $"{number}: {mrErr}"); }
            if (dn == null) Inc($"[{src}] DenaFaraz build FAILED");
            if (mr == null || dn == null) continue;

            // تاریخِ پذیرفته‌شده کدام بود؟
            var accDay = Day(acc.First().Indatim_Sec);
            bool mrDate = Day(mr.Header.Indatim ?? 0) == accDay;
            bool dnDate = Day(dn.Header.Indatim ?? 0) == accDay;
            bool customDate = !mrDate && !dnDate;
            if (customDate) Inc($"[{src}] accepted date = neither branch (custom date)");

            var mrDiff = Diff(mr, acc, !customDate);
            var dnDiff = Diff(dn, acc, !customDate);
            string mrKey = mrDiff.Count == 0 ? "MATCH" : string.Join(",", mrDiff);
            string dnKey = dnDiff.Count == 0 ? "MATCH" : string.Join(",", dnDiff);
            Inc($"[{src}] MrCorrect={mrKey} | DenaFaraz={dnKey}");
            if (mrDiff.Count > 0) Sample($"[{src}] MrCorrect={mrKey} | DenaFaraz={dnKey}", number.ToString());

            // دو شاخه با هم (مستقل از آنچه پذیرفته شده)
            var between = Diff(mr, dn.Body!.Select(b => new Accepted
            {
                Sstid = b.Sstid, Am = b.Am, Fee = b.Fee, Vam = b.Vam,
                Indatim_Sec = dn.Header.Indatim ?? 0, Tinb = dn.Header.Tinb, Tob = dn.Header.Tob,
                Tbill = dn.Header.Tbill, Tvam = dn.Header.Tvam, Tadis = dn.Header.Tadis
            }).ToList(), true);
            Inc("BETWEEN BRANCHES: " + (between.Count == 0 ? "identical" : string.Join(",", between)));
        }

        long taxdtlAfter = db.DoGetDataSQL<long>("SELECT COUNT_BIG(*) FROM dbo.TAXDTL").First();
        long extAfter = db.DoGetDataSQL<long>("SELECT COUNT_BIG(*) FROM dbo.HEAD_LST_EXTENDED").First();

        Console.WriteLine($"===== {dbName}: {total} accepted invoices (no tag13 header: {noHead13})");
        foreach (var kv in counters.OrderBy(k => k.Key)) Console.WriteLine($"  {kv.Value,6}  {kv.Key}");
        foreach (var kv in samples) Console.WriteLine($"  sample {kv.Key}: {string.Join(" ; ", kv.Value)}");
        Console.WriteLine($"  writes: TAXDTL {taxdtlBefore}->{taxdtlAfter}, HEAD_LST_EXTENDED {extBefore}->{extAfter}");
        return taxdtlBefore == taxdtlAfter && extBefore == extAfter ? 0 : 2;
    }
}
