using System.Text.Json.Nodes;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.FUNCTIONS;
using Prg_Moadian.Bulk;
using Prg_Moadian.Generaly;

namespace MoadianLocalTest;

/// <summary>
/// گروه ۲۱ — مسیر ارسال تکی (CL_MOADIAN) از سر تا ته.
/// گروه ۲۲ — مقایسه مسیر تکی با مسیر گروهی روی یک فاکتور یکسان.
///
/// مسیر تکی تا حالا هیچ پوششی نداشت. خوشبختانه headless قابل اجراست:
/// DoSendInvoice ورودی‌اش یک رشته «شماره_تگ_فراخوان» است و تنها نقطه تعامل
/// با کاربر پشت OnValidationWarning قرار دارد.
///
/// گروه ۲۲ مهم‌ترین بخش است: فرمول‌های مبلغ در دو مسیر تکرار شده‌اند. اگر
/// کسی یکی را عوض کند و دیگری را نه، اینجا لو می‌رود.
/// </summary>
internal static class SinglePath
{
    private const int SendTag = 77;
    private const int HeadTag = 88;
    private const long FirstNumber = 1_000_001;

    public static void Run(string mockBaseUrl, HttpClient http,
                           Action<string, bool, string> check, Action<string> line)
    {
        var db = new CL_CCNNMANAGER();
        string savedMode = CL_Generaly.MrCorrect;
        string savedUrl = CL_MOADIAN.TaxURL;
        var savedWarn = CL_MOADIAN.OnValidationWarning;

        try
        {
            CL_Generaly.MrCorrect = "m";
            CL_MOADIAN.TaxURL = mockBaseUrl;                 // شامل «sandbox» است
            CL_MOADIAN.OnValidationWarning = _ => true;

            var numbers = db.DoGetDataSQL<double>(
                $@"SELECT TOP 2 NUMBER FROM dbo.HEAD_LST
                   WHERE TAG={HeadTag} AND NUMBER>={FirstNumber} ORDER BY NUMBER")
                .Select(x => (long)x).ToList();

            if (numbers.Count == 0)
            {
                check("۲۱-۱ داده تستی موجود است", false, "seed_bulk_test.sql را اجرا کنید");
                return;
            }

            long target = numbers[0];

            // ---------------- گروه ۲۱ : مسیر تکی ----------------
            Reset(http, mockBaseUrl);
            db.DoExecuteSQL($"DELETE FROM dbo.TAXDTL WHERE TAG={SendTag} AND NUMBER BETWEEN 1000001 AND 1000120");

            bool threw = false;
            string err = "";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                CL_MOADIAN.DoSendInvoice(new[] { $"{target}_{SendTag}_m" });
            }
            catch (Exception ex) { threw = true; err = One(ex.Message); }
            sw.Stop();
            line($"      · ارسال تکی فاکتور {target}: {sw.Elapsed.TotalSeconds:F0} ثانیه" +
                 (threw ? $" — استثنا: {err}" : ""));

            check("۲۱-۱ ارسال تکی بدون استثنا تمام شد", !threw, err);

            var singlePayloads = Payloads(http, mockBaseUrl);
            check("۲۱-۲ دقیقا یک صورتحساب به سرور رسید",
                  singlePayloads.Count == 1, singlePayloads.Count.ToString());
            if (singlePayloads.Count == 0) return;

            var single = singlePayloads.Values.First().AsObject();
            var sh = single["header"]!.AsObject();

            check("۲۱-۳ شماره مالیاتی ۲۲ کاراکتری ساخته شد",
                  (sh["taxid"]?.GetValue<string>() ?? "").Length == 22,
                  sh["taxid"]?.GetValue<string>());
            check("۲۱-۴ سریال ۱۰ کاراکتری ساخته شد",
                  (sh["inno"]?.GetValue<string>() ?? "").Length == 10,
                  sh["inno"]?.GetValue<string>());
            check("۲۱-۵ صورتحساب اصلی است و مرجع ندارد",
                  sh["ins"]!.GetValue<int>() == 1 && sh["irtaxid"] == null, "");
            check("۲۱-۶ قاعده ارسال خالی رفت",
                  sh["insr"] == null, "");
            check("۲۱-۷ اقلام خالی نیست",
                  single["body"]!.AsArray().Count > 0,
                  single["body"]!.AsArray().Count.ToString());

            // --- آنچه در دیتابیس ثبت شد ---
            long rows = db.DoGetDataSQL<long>(
                $"SELECT COUNT(*) FROM dbo.TAXDTL WHERE TAG={SendTag} AND NUMBER={target}").First();
            check("۲۱-۸ ردیف‌ها در TAXDTL ثبت شدند",
                  rows == single["body"]!.AsArray().Count, $"{rows} ردیف");

            long noBulkMark = db.DoGetDataSQL<long>(
                $"SELECT COUNT(*) FROM dbo.TAXDTL WHERE TAG={SendTag} AND NUMBER={target} AND ISNULL(REMARKS,'')='Bulk'").First();
            check("۲۱-۹ ردیف‌های مسیر تکی برچسب Bulk نمی‌خورند",
                  noBulkMark == 0, noBulkMark.ToString());

            // مسیر تکی بعد از ارسال، ۱۰ ثانیه صبر می‌کند و خودش استعلام می‌زند،
            // بعد وضعیت را از PENDING به آنچه سامانه گفته به‌روز می‌کند.
            // پس اینجا وضعیتِ *بعد از استعلام* را می‌سنجیم، نه PENDING.
            var statuses = db.DoGetDataSQL<string>(
                $"SELECT DISTINCT ISNULL(TheStatus,'(null)') FROM dbo.TAXDTL WHERE TAG={SendTag} AND NUMBER={target}")
                .ToList();
            check("۲۱-۱۰ استعلام خودکار بعد از ارسال وضعیت را به‌روز کرد",
                  statuses.Count == 1 && statuses[0] != "(null)" && statuses[0] != "FAILED",
                  string.Join("، ", statuses));

            long hasRef = db.DoGetDataSQL<long>(
                $"SELECT COUNT(*) FROM dbo.TAXDTL WHERE TAG={SendTag} AND NUMBER={target} AND ISNULL(RefrenceNumber,'')<>''").First();
            check("۲۱-۱۰-ب همه ردیف‌ها کد رهگیری گرفتند",
                  hasRef == rows, $"{hasRef}/{rows}");

            long emptyTinb = db.DoGetDataSQL<long>(
                $"SELECT COUNT(*) FROM dbo.TAXDTL WHERE TAG={SendTag} AND NUMBER={target} AND Tinb IS NULL").First();
            check("۲۱-۱۱ ستون Tinb در دیتابیس هیچ‌وقت NULL نمی‌ماند (رشته خالی می‌شود)",
                  emptyTinb == 0, emptyTinb.ToString());

            // --- حالت استاتیک بعد از ارسال ---
            check("۲۱-۱۲ ارسال دوم در همان پروسه ردیف‌ها را دو برابر نمی‌کند",
                  SecondSendDoesNotDouble(db, http, mockBaseUrl, target, single, out var d2), d2);

            // --- فاکتوری که ارسال زنده دارد: کاربر «لغو ارسال» می‌زند ---
            {
                long TaxidCount() => db.DoGetDataSQL<long>(
                    $"SELECT COUNT(DISTINCT Taxid) FROM dbo.TAXDTL WHERE TAG={SendTag} AND NUMBER={target}").First();
                long taxidsBefore = TaxidCount();
                bool asked = false, blocked = false;
                CL_MOADIAN.OnValidationWarning = msg =>
                {
                    bool dup = msg.Contains("قبلاً به سامانه ارسال شده");
                    asked |= dup;
                    return !dup;
                };
                Reset(http, mockBaseUrl);
                try { CL_MOADIAN.DoSendInvoice(new[] { $"{target}_{SendTag}_m" }); }
                catch (Exception) { blocked = true; }
                finally { CL_MOADIAN.OnValidationWarning = _ => true; }

                int sentAgain = Payloads(http, mockBaseUrl).Count;
                check("۲۱-۱۳ ارسال تکیِ فاکتورِ ارسال‌شده، اول می‌پرسد و با «لغو» چیزی نمی‌فرستد",
                      taxidsBefore > 0 && asked && blocked && sentAgain == 0 && TaxidCount() == taxidsBefore,
                      $"پرسید {asked}، متوقف {blocked}، ارسال {sentAgain}، شماره‌ها {taxidsBefore}→{TaxidCount()}");
            }

            // --- جواب سامانه نمی‌رسد، ولی سامانه صورتحساب را گرفته است ---
            foreach (var (mode, id) in new[] { ("empty", "۲۱-۱۴"), ("close", "۲۱-۱۵") })
                check($"{id} جواب نرسید ({mode}): ردیف‌ها UNKNOWN و با همان شماره مالیاتی ثبت می‌شوند و ارسال بعدی هشدار می‌دهد",
                      LostResponseIsRecorded(db, http, mockBaseUrl, target, mode, out var dl), dl);

            // ---------------- گروه ۲۲ : مقایسه دو مسیر ----------------
            Reset(http, mockBaseUrl);
            db.DoExecuteSQL($"DELETE FROM dbo.TAXDTL WHERE TAG={SendTag} AND NUMBER BETWEEN 1000001 AND 1000120");

            var bulk = new SendInvoiceBulk(db, mockBaseUrl) { OnValidationWarning = _ => true };
            bulk.SendAsync(new[] { target }, SendTag, inty_value: null, setm_value: null)
                .GetAwaiter().GetResult();

            var bulkPayloads = Payloads(http, mockBaseUrl);
            if (bulkPayloads.Count != 1)
            {
                check("۲۲-۱ ارسال گروهی همان فاکتور انجام شد", false, bulkPayloads.Count.ToString());
                return;
            }
            var bulkInv = bulkPayloads.Values.First().AsObject();
            var bh = bulkInv["header"]!.AsObject();

            check("۲۲-۱ ارسال گروهی همان فاکتور انجام شد", true, "");

            // --- سرصفحه ---
            CompareHeader(sh, bh, check);

            // --- اقلام ---
            var sb = single["body"]!.AsArray();
            var bb = bulkInv["body"]!.AsArray();
            check("۲۲-۸ تعداد اقلام در دو مسیر یکسان است",
                  sb.Count == bb.Count, $"تکی {sb.Count} / گروهی {bb.Count}");

            if (sb.Count == bb.Count)
            {
                string[] moneyFields = { "am", "fee", "prdis", "dis", "adis", "vra", "vam", "tsstam" };
                var mismatch = new List<string>();
                for (int i = 0; i < sb.Count; i++)
                {
                    foreach (var f in moneyFields)
                    {
                        string a = sb[i]![f]?.ToJsonString() ?? "null";
                        string b = bb[i]![f]?.ToJsonString() ?? "null";
                        if (a != b) mismatch.Add($"قلم {i + 1} {f}: تکی {a} / گروهی {b}");
                    }
                }
                check("۲۲-۹ همه مبالغ اقلام در دو مسیر یکسان است",
                      mismatch.Count == 0,
                      string.Join("؛ ", mismatch.Take(4)) +
                      (mismatch.Count > 4 ? $" و {mismatch.Count - 4} مورد دیگر" : ""));

                var idMismatch = new List<string>();
                for (int i = 0; i < sb.Count; i++)
                {
                    foreach (var f in new[] { "sstid", "mu" })
                    {
                        string a = sb[i]![f]?.GetValue<string>() ?? "null";
                        string b = bb[i]![f]?.GetValue<string>() ?? "null";
                        if (a != b) idMismatch.Add($"قلم {i + 1} {f}: تکی {a} / گروهی {b}");
                    }
                }
                check("۲۲-۱۰ شناسه کالا و واحد در دو مسیر یکسان است",
                      idMismatch.Count == 0, string.Join("؛ ", idMismatch.Take(4)));
            }
        }
        finally
        {
            CL_Generaly.MrCorrect = savedMode;
            CL_MOADIAN.TaxURL = savedUrl;
            CL_MOADIAN.OnValidationWarning = savedWarn;
            db.DoExecuteSQL($"DELETE FROM dbo.TAXDTL WHERE TAG={SendTag} AND NUMBER BETWEEN 1000001 AND 1000120");
        }
    }

    // ---------------------------------------------------------------- مقایسه سرصفحه

    private static void CompareHeader(JsonObject s, JsonObject b, Action<string, bool, string> check)
    {
        void Same(string label, string field) =>
            check(label,
                  (s[field]?.ToJsonString() ?? "null") == (b[field]?.ToJsonString() ?? "null"),
                  $"تکی {Short(s[field])} / گروهی {Short(b[field])}");

        Same("۲۲-۲ سریال صورتحساب یکسان", "inno");
        Same("۲۲-۳ تاریخ صدور یکسان", "indatim");
        Same("۲۲-۴ هویت خریدار یکسان (tinb)", "tinb");
        Same("۲۲-۵-الف نوع صورتحساب یکسان", "inty");
        Same("۲۲-۵-ب الگوی صورتحساب یکسان", "inp");
        Same("۲۲-۵-ج موضوع صورتحساب یکسان", "ins");
        Same("۲۲-۵-د شماره اقتصادی فروشنده یکسان", "tins");
        Same("۲۲-۵-ه نوع شخص خریدار یکسان", "tob");
        Same("۲۲-۵-و شناسه ملی خریدار یکسان", "bid");
        Same("۲۲-۵-ز کد شعبه و کد پستی خریدار یکسان", "bbc");
        Same("۲۲-۵-ح قاعده ارسال یکسان", "insr");
        Same("۲۲-۵-ط شماره مالیاتی مرجع یکسان", "irtaxid");

        string[] money = { "tprdis", "tdis", "tadis", "tvam", "todam", "tbill" };
        var diff = money.Where(f => (s[f]?.ToJsonString() ?? "null") != (b[f]?.ToJsonString() ?? "null"))
                        .Select(f => $"{f}: تکی {Short(s[f])} / گروهی {Short(b[f])}")
                        .ToList();
        check("۲۲-۶ همه جمع‌های سرصفحه در دو مسیر یکسان است",
              diff.Count == 0, string.Join("؛ ", diff));

        string[] settle = { "setm", "cap", "insp" };
        var sdiff = settle.Where(f => (s[f]?.ToJsonString() ?? "null") != (b[f]?.ToJsonString() ?? "null"))
                          .Select(f => $"{f}: تکی {Short(s[f])} / گروهی {Short(b[f])}")
                          .ToList();
        check("۲۲-۷ روش تسویه و مبالغ نقدی/نسیه در دو مسیر یکسان است",
              sdiff.Count == 0, string.Join("؛ ", sdiff));
    }

    // ---------------------------------------------------------------- کمکی

    private static bool SecondSendDoesNotDouble(CL_CCNNMANAGER db, HttpClient http, string url,
                                                long target, JsonObject first, out string detail)
    {
        try
        {
            int firstLines = first["body"]!.AsArray().Count;
            Reset(http, url);
            db.DoExecuteSQL($"DELETE FROM dbo.TAXDTL WHERE TAG={SendTag} AND NUMBER BETWEEN 1000001 AND 1000120");
            CL_MOADIAN.DoSendInvoice(new[] { $"{target}_{SendTag}_m" });
            var again = Payloads(http, url);
            if (again.Count == 0) { detail = "ارسال دوم چیزی نفرستاد"; return false; }
            int secondLines = again.Values.First()["body"]!.AsArray().Count;
            detail = $"{firstLines} → {secondLines}";
            return firstLines == secondLines;
        }
        catch (Exception e) { detail = One(e.Message); return false; }
    }

    /// <summary>
    /// قبلاً اگر جواب نمی‌رسید، مسیر تکی هیچ ردی نمی‌گذاشت و ارسال بعدی بی‌هشدار
    /// شماره مالیاتی تازه می‌ساخت؛ در حالی که سامانه شاید اولی را گرفته بود.
    /// </summary>
    private static bool LostResponseIsRecorded(CL_CCNNMANAGER db, HttpClient http, string url,
                                               long target, string mode, out string detail)
    {
        var saved = CL_MOADIAN.OnValidationWarning;
        try
        {
            Reset(http, url);
            db.DoExecuteSQL($"DELETE FROM dbo.TAXDTL WHERE TAG={SendTag} AND NUMBER={target}");
            http.PostAsync(Root(url) + "/__drop_next_send",
                           new StringContent($"{{\"mode\":\"{mode}\"}}", System.Text.Encoding.UTF8, "application/json"))
                .GetAwaiter().GetResult();

            string err = "";
            try { CL_MOADIAN.DoSendInvoice(new[] { $"{target}_{SendTag}_m" }); }
            catch (Exception ex) { err = ex.Message; }

            var sentTaxids = Payloads(http, url).Keys.ToList();
            var rows = db.DoGetDataSQL<(string Taxid, string TheStatus)>(
                $"SELECT Taxid, TheStatus FROM dbo.TAXDTL WHERE TAG={SendTag} AND NUMBER={target}").ToList();

            bool told = err.Contains("نرسید");
            bool recorded = rows.Count > 0 && rows.All(r => r.TheStatus == "UNKNOWN");
            bool sameTaxid = sentTaxids.Count == 1 && rows.All(r => r.Taxid == sentTaxids[0]);

            // ارسال بعدی باید بپرسد؛ کاربر «لغو» می‌زند
            bool asked = false;
            CL_MOADIAN.OnValidationWarning = msg =>
            {
                bool dup = msg.Contains("قبلاً به سامانه ارسال شده");
                asked |= dup;
                return !dup;
            };
            try { CL_MOADIAN.DoSendInvoice(new[] { $"{target}_{SendTag}_m" }); } catch { }
            bool noNewSend = Payloads(http, url).Count == 1;

            detail = $"پیام «نرسید» {told}، ردیف‌ها {rows.Count} ({string.Join(",", rows.Select(r => r.TheStatus).Distinct())})، " +
                     $"شماره یکسان {sameTaxid}، پرسید {asked}، ارسال تازه نشد {noNewSend}" +
                     (told ? "" : $" — خطا: {One(err)}");
            return told && recorded && sameTaxid && asked && noNewSend;
        }
        catch (Exception e) { detail = One(e.Message); return false; }
        finally { CL_MOADIAN.OnValidationWarning = saved; }
    }

    private static Dictionary<string, JsonNode> Payloads(HttpClient http, string baseUrl)
    {
        string url = Root(baseUrl) + "/__payloads";
        var json = http.GetStringAsync(url).GetAwaiter().GetResult();
        return JsonNode.Parse(json)!.AsObject().ToDictionary(kv => kv.Key, kv => kv.Value!);
    }

    private static void Reset(HttpClient http, string baseUrl) =>
        http.PostAsync(Root(baseUrl) + "/__reset",
                       new StringContent("{}", System.Text.Encoding.UTF8, "application/json"))
            .GetAwaiter().GetResult();

    private static string Root(string baseUrl) =>
        baseUrl.TrimEnd('/').Replace("/sandbox", "");

    private static string Short(JsonNode? n)
    {
        var s = n?.ToJsonString() ?? "null";
        return s.Length > 30 ? s.Substring(0, 30) + "…" : s;
    }

    private static string One(string s)
    {
        s = (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        return s.Length > 100 ? s.Substring(0, 100) + "…" : s;
    }
}
