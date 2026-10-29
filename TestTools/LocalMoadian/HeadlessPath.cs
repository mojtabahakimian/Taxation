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
