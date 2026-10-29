using System.Text.Json.Nodes;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.FUNCTIONS;
using Prg_Moadian.Headless;

namespace MoadianLocalTest;

/// <summary>
/// گروه ۳۰ — ارسال تکی بدون پنجره (SingleSendRunner)، همان مسیری که سرور سفیر صدا می‌زند.
///
/// سه چیز را قفل می‌کند که در MOADIAN.EXE خودبه‌خود درست بود و در پروسهٔ ماندگار نیست:
/// رشتهٔ اتصال از میزبان، پاک شدن حالت استاتیک بین دو ارسال، و اینکه «لغو» روی یک
/// پرسش هیچ چیزی نفرستد و متن همان پرسش را برگرداند.
/// </summary>
internal static class HeadlessPath
{
    private const int SendTag = 77;
    private const int HeadTag = 88;
    private const long FirstNumber = 1_000_001;

    public static void Run(string mockBaseUrl, HttpClient http,
                           Action<string, bool, string> check, Action<string> line)
    {
        var db = new CL_CCNNMANAGER();
        string cs = CL_CCNNMANAGER.CONNECTION_STR;
        string savedUrl = CL_MOADIAN.TaxURL;
        var savedWarn = CL_MOADIAN.OnValidationWarning;

        try
        {
            long target = db.DoGetDataSQL<double>(
                $@"SELECT TOP 1 NUMBER FROM dbo.HEAD_LST
                   WHERE TAG={HeadTag} AND NUMBER>={FirstNumber} ORDER BY NUMBER")
                .Select(x => (long)x).FirstOrDefault();
            if (target == 0)
            {
                check("۳۰-۰ داده تستی موجود است", false, "seed_bulk_test.sql را اجرا کنید");
                return;
            }

            SingleSendRunner.Request Req(Func<string, bool>? confirm) => new()
            {
                ConnectionString = cs,
                Number = target,
                Tag = SendTag,
                MainSystem = false,
                TaxUrlOverride = mockBaseUrl,
                Confirm = confirm,
            };

            // ---- ۱: ارسال کامل ----
            Reset(http, mockBaseUrl);
            db.DoExecuteSQL($"DELETE FROM dbo.TAXDTL WHERE TAG={SendTag} AND NUMBER BETWEEN 1000001 AND 1000120");
            var r1 = SingleSendRunner.Run(Req(_ => true));
            var p1 = Payloads(http, mockBaseUrl);
            check("۳۰-۱ ارسال بی‌پنجره صورتحساب را فرستاد و شماره مالیاتی و کد رهگیری برگرداند",
                  r1.Sent && (r1.TaxId ?? "").Length == 22 && !string.IsNullOrEmpty(r1.ReferenceNumber) && p1.Count == 1,
                  $"Sent={r1.Sent} TaxId={r1.TaxId} Ref={r1.ReferenceNumber} ارسال={p1.Count} خطا={r1.Error} {string.Join("؛ ", r1.Messages)}");
            check("۳۰-۲ بعد از اجرا رشتهٔ اتصال میزبان پاک شد",
                  CL_CCNNMANAGER.ConnectionOverride == null, CL_CCNNMANAGER.ConnectionOverride ?? "");
            check("۳۰-۳ بعد از اجرا آدرس سامانه و callback هسته به حالت قبل برگشت",
                  CL_MOADIAN.TaxURL == savedUrl && CL_MOADIAN.OnValidationWarning == savedWarn, CL_MOADIAN.TaxURL);
            check("۳۰-۴ وضعیت استعلام همان لحظه گزارش شد",
                  !string.IsNullOrEmpty(r1.Status), r1.Status ?? "(خالی)");
            if (p1.Count == 0) return;
            int firstLines = p1.Values.First()["body"]!.AsArray().Count;

            // ---- ۲: فاکتور ارسال‌شده؛ میزبان هنوز تأیید نداده ----
            long Taxids() => db.DoGetDataSQL<long>(
                $"SELECT COUNT(DISTINCT Taxid) FROM dbo.TAXDTL WHERE TAG={SendTag} AND NUMBER={target}").First();
            long before = Taxids();
            Reset(http, mockBaseUrl);
            var r2 = SingleSendRunner.Run(Req(null));
            check("۳۰-۵ بدون تأیید: پرسش «قبلاً ارسال شده» برمی‌گردد و چیزی فرستاده نمی‌شود",
                  !r2.Sent && r2.Error == null && (r2.Question ?? "").Contains("قبلاً به سامانه ارسال شده")
                  && Payloads(http, mockBaseUrl).Count == 0 && Taxids() == before,
                  $"Sent={r2.Sent} خطا={r2.Error} پرسش={One(r2.Question ?? "")}");

            // ---- ۳: تأیید همان پرسش؛ حالت استاتیک دوبرابر نمی‌شود ----
            string q = r2.Question ?? "";
            Reset(http, mockBaseUrl);
            var r3 = SingleSendRunner.Run(Req(m => m == q));
            var p3 = Payloads(http, mockBaseUrl);
            int thirdLines = p3.Count == 1 ? p3.Values.First()["body"]!.AsArray().Count : -1;
            check("۳۰-۶ با تأیید همان پرسش ارسال انجام شد و اقلام دو برابر نشد",
                  r3.Sent && p3.Count == 1 && thirdLines == firstLines,
                  $"Sent={r3.Sent} ارسال={p3.Count} اقلام {firstLines}→{thirdLines} خطا={r3.Error}");

            // ---- ۴: رشتهٔ اتصال خراب ----
            var r4 = SingleSendRunner.Run(new SingleSendRunner.Request
            {
                ConnectionString = "Data Source=127.0.0.1,1;Initial Catalog=x;User ID=x;Password=x;Connect Timeout=2;TrustServerCertificate=True",
                Number = target, Tag = SendTag, TaxUrlOverride = mockBaseUrl, Confirm = _ => true,
            });
            check("۳۰-۷ دیتابیس در دسترس نیست: پیام روشن، بدون استثنا، بدون ارسال",
                  !r4.Sent && r4.Error == "خطا در ارتباط با دیتابیس" && CL_CCNNMANAGER.ConnectionOverride == null,
                  r4.Error ?? "");

            BulkAndInquiry(db, cs, mockBaseUrl, http, check);
        }
        finally
        {
            new CL_CCNNMANAGER(); // CONNECTION_STR استاتیک را به CNR.udl برمی‌گرداند
            CL_MOADIAN.dbms = db;
            CL_MOADIAN.TaxURL = savedUrl;
            CL_MOADIAN.OnValidationWarning = savedWarn;
            db.DoExecuteSQL($"DELETE FROM dbo.TAXDTL WHERE TAG={SendTag} AND NUMBER BETWEEN 1000001 AND 1000120");
        }
    }

    /// <summary>
    /// گروه ۳۱ — ارسالِ گروهی و استعلامِ بی‌پنجره (BulkSendRunner و InquiryRunner).
    /// </summary>
    private static void BulkAndInquiry(CL_CCNNMANAGER db, string cs, string mockBaseUrl, HttpClient http, Action<string, bool, string> check)
    {
        var numbers = db.DoGetDataSQL<double>(
            $@"SELECT TOP 3 NUMBER FROM dbo.HEAD_LST WHERE TAG={HeadTag} AND NUMBER>={FirstNumber} ORDER BY NUMBER")
            .Select(x => (long)x).ToList();
        if (numbers.Count < 3) { check("۳۱-۰ سه فاکتورِ تستی هست", false, numbers.Count.ToString()); return; }

        BulkSendRunner.Request Req() => new()
        {
            ConnectionString = cs, Numbers = numbers, Tag = SendTag, TaxUrlOverride = mockBaseUrl,
        };

        // ---- ۱: ارسالِ گروهیِ سه فاکتور ----
        Reset(http, mockBaseUrl);
        db.DoExecuteSQL($"DELETE FROM dbo.TAXDTL WHERE TAG={SendTag} AND NUMBER BETWEEN 1000001 AND 1000120");
        int reviewed = -1, lastProgress = -1;
        var req = Req();
        req.ReviewWarnings = w => { reviewed = w.Count; return new HashSet<long>(); };
        req.Progress = new SyncProgress(p => lastProgress = p);
        var r1 = BulkSendRunner.RunAsync(req).GetAwaiter().GetResult();
        var p1 = Payloads(http, mockBaseUrl);
        check("۳۱-۱ ارسالِ گروهیِ بی‌پنجره هر سه فاکتور را فرستاد",
              r1.Error == null && r1.Success == 3 && r1.Failures.Count == 0 && p1.Count == 3,
              $"Success={r1.Success} Failures={r1.Failures.Count} ارسال={p1.Count} خطا={r1.Error} {string.Join("؛ ", r1.Failures.Values.Take(2))}");
        // مقدارهای progress ِ SendInvoiceBulk یکدست نیستند (۲۵، درصدِ ساخت، ۰، تعدادِ هر بسته)؛ فقط رسیدنشان قفل می‌شود.
        // فهرستِ هشدارها فقط وقتی هشداری هست صدا زده می‌شود (فاکتورهای seed بی‌هشدارند).
        check("۳۱-۲ بی‌هشدار، فهرستِ هشدارها پرسیده نشد و پیشرفت گزارش شد",
              reviewed == -1 && lastProgress >= 0, $"هشدارها={reviewed} آخرین پیشرفت={lastProgress}");
        check("۳۱-۳ بعد از ارسالِ گروهی رشته‌ی اتصالِ میزبان پاک شد",
              CL_CCNNMANAGER.ConnectionOverride == null, CL_CCNNMANAGER.ConnectionOverride ?? "");

        // ---- ۲: دوباره همان‌ها؛ میزبان «لغو» می‌زند ----
        Reset(http, mockBaseUrl);
        string? asked = null;
        req = Req();
        req.Confirm = q => { asked = q; return false; };
        var r2 = BulkSendRunner.RunAsync(req).GetAwaiter().GetResult();
        check("۳۱-۴ فاکتورهای ارسال‌شده: یک پرسش و با «لغو» هیچ‌کدام دوباره نمی‌روند",
              asked != null && r2.Success == 0 && r2.Failures.Count == 3 && Payloads(http, mockBaseUrl).Count == 0,
              $"پرسید={asked != null} Success={r2.Success} Failures={r2.Failures.Count}");

        // ---- ۳: «ReviewWarnings» فاکتورِ هشداردار را کنار می‌گذارد (مثلِ ۱۶-۳۳: نرخ دارد، مالیات صفر) ----
        Reset(http, mockBaseUrl);
        db.DoExecuteSQL($"DELETE FROM dbo.TAXDTL WHERE TAG={SendTag} AND NUMBER BETWEEN 1000001 AND 1000120");
        var imbaa = db.DoGetDataSQL<(double Radif, double? Imbaa)>(
            $"SELECT RADIF, IMBAA FROM dbo.INVO_LST WHERE TAG={SendTag} AND NUMBER={numbers[0]}").ToList();
        IReadOnlyList<Prg_Moadian.Bulk.BulkWarning> seen = Array.Empty<Prg_Moadian.Bulk.BulkWarning>();
        Prg_Moadian.Headless.BulkSendRunner.Result r3;
        try
        {
            db.DoExecuteSQL($"UPDATE dbo.INVO_LST SET IMBAA = 0 WHERE TAG={SendTag} AND NUMBER={numbers[0]}");
            req = Req();
            req.Inty = 1; req.Setm = 1;
            req.ReviewWarnings = w => { seen = w; return new HashSet<long> { numbers[0] }; };
            r3 = BulkSendRunner.RunAsync(req).GetAwaiter().GetResult();
        }
        finally
        {
            foreach (var s in imbaa)
                db.DoExecuteSQL($"UPDATE dbo.INVO_LST SET IMBAA = @I WHERE TAG={SendTag} AND NUMBER={numbers[0]} AND RADIF=@R", new { I = s.Imbaa, R = s.Radif });
        }
        check("۳۱-۵ هشدارِ ساخت به میزبان رسید؛ فاکتوری که کنار گذاشت نرفت و بقیه رفتند",
              seen.Any(w => w.Number == numbers[0]) && r3.Success == 2 && r3.Failures.ContainsKey(numbers[0]) && Payloads(http, mockBaseUrl).Count == 2,
              $"هشدارها={seen.Count} Success={r3.Success} Failures={string.Join(",", r3.Failures.Keys)}");

        // ---- ۴: استعلام ----
        var refs = db.DoGetDataSQL<string>(
            $"SELECT DISTINCT RefrenceNumber FROM dbo.TAXDTL WHERE TAG={SendTag} AND NUMBER BETWEEN 1000001 AND 1000120 AND ISNULL(RefrenceNumber,'')<>''").ToList();
        db.DoExecuteSQL($"UPDATE dbo.TAXDTL SET TheStatus=N'PENDING' WHERE TAG={SendTag} AND NUMBER BETWEEN 1000001 AND 1000120");
        var iq = InquiryRunner.Run(cs, false, refs, mockBaseUrl);
        var statuses = db.DoGetDataSQL<string>(
            $"SELECT DISTINCT ISNULL(TheStatus,'(null)') FROM dbo.TAXDTL WHERE TAG={SendTag} AND NUMBER BETWEEN 1000001 AND 1000120").ToList();
        check("۳۱-۶ استعلامِ بی‌پنجره وضعیتِ همه‌ی کدهای رهگیری را از PENDING به‌روز کرد",
              iq.Error == null && refs.Count > 0 && iq.Items.Count == refs.Count && !statuses.Contains("PENDING") && iq.Items.All(i => !string.IsNullOrEmpty(i.Status)),
              $"کدها={refs.Count} نتیجه={iq.Items.Count} وضعیت‌ها={string.Join(",", statuses)} خطا={iq.Error}");

        // ---- ۵: بی‌تکلیفِ قدیمی بی‌استعلام منقضی می‌شود ----
        db.DoExecuteSQL($"UPDATE dbo.TAXDTL SET TheStatus=N'PENDING', CRT=DATEADD(DAY,-40,GETDATE()) WHERE RefrenceNumber=N'{refs[0]}'");
        Reset(http, mockBaseUrl);
        var iq2 = InquiryRunner.Run(cs, false, new[] { refs[0] }, mockBaseUrl);
        var st = db.DoGetDataSQL<string>($"SELECT DISTINCT TheStatus FROM dbo.TAXDTL WHERE RefrenceNumber=N'{refs[0]}'").ToList();
        check("۳۱-۷ ارسالِ بی‌تکلیفِ بیش از ۳۱ روز EXPIRED شد (همه‌ی ردیف‌هایش)",
              iq2.Items.Single().Status == "EXPIRED" && st.Count == 1 && st[0] == "EXPIRED" && CL_CCNNMANAGER.ConnectionOverride == null,
              string.Join(",", st));
    }

    /// <summary>Progress&lt;T&gt; گزارش را روی SynchronizationContext می‌فرستد؛ اینجا باید همان لحظه ثبت شود.</summary>
    private sealed class SyncProgress : IProgress<int>
    {
        private readonly Action<int> _a;
        public SyncProgress(Action<int> a) => _a = a;
        public void Report(int value) => _a(value);
    }

    private static Dictionary<string, JsonNode> Payloads(HttpClient http, string baseUrl)
    {
        var json = http.GetStringAsync(Root(baseUrl) + "/__payloads").GetAwaiter().GetResult();
        return JsonNode.Parse(json)!.AsObject().ToDictionary(kv => kv.Key, kv => kv.Value!);
    }

    private static void Reset(HttpClient http, string baseUrl) =>
        http.PostAsync(Root(baseUrl) + "/__reset",
                       new StringContent("{}", System.Text.Encoding.UTF8, "application/json"))
            .GetAwaiter().GetResult();

    private static string Root(string baseUrl) => baseUrl.TrimEnd('/').Replace("/sandbox", "");

    private static string One(string s)
    {
        s = s.Replace("\r", " ").Replace("\n", " ").Trim();
        return s.Length > 100 ? s.Substring(0, 100) + "…" : s;
    }
}
