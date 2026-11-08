using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.FUNCTIONS;
using TaxCollectData.Library.Business;
using TaxCollectData.Library.Dto.Config;
using TaxCollectData.Library.Dto.Content;
using TaxCollectData.Library.Dto.Properties;
using TaxCollectData.Library.Enums;

namespace MoadianLocalTest;

/// <summary>
/// ابزار استعلام زنده و مستقیم از سامانه مودیان برای دیتابیس‌های مشتریان.
/// بدون هیچ کلید یا رمزی در کد؛ همه تنظیمات مستقیماً از جدول SAZMAN خوانده می‌شود.
///
/// نمونه‌های فراخوانی:
///   1) با شماره فاکتور:
///      dotnet run --project TestTools\LocalMoadian -- --inquiry-live=10018
///
///   2) با کد رهگیری:
///      dotnet run --project TestTools\LocalMoadian -- --inquiry-live=tEwYUabIK1BJq9UBpSRwes8MWFdpCWGbIW51Lw
///
///   3) برای محیط سندباکس:
///      dotnet run --project TestTools\LocalMoadian -- --inquiry-live=10018 --sandbox
///
///   4) به‌روزرسانی دیتابیس در صورت نیاز:
///      dotnet run --project TestTools\LocalMoadian -- --inquiry-live=10018 --update-db
///
///   5) با رشته اتصال دلخواه:
///      dotnet run --project TestTools\LocalMoadian -- --inquiry-live=10018 --cs="Data Source=...;Initial Catalog=...;Integrated Security=True;TrustServerCertificate=True;"
/// </summary>
internal static class InquiryLive
{
    private const string MainUrl = "https://tp.tax.gov.ir/req/api/";
    private const string SandboxUrl = "https://sandboxrc.tax.gov.ir/req/api/";

    public static int Run(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var arg = args.First(a => a.StartsWith("--inquiry-live="));
        var target = arg.Split('=')[1].Trim();

        var csArg = args.FirstOrDefault(a => a.StartsWith("--cs="));
        if (csArg != null)
        {
            var customCs = csArg.Substring("--cs=".Length).Trim('"', '\'');
            CL_CCNNMANAGER.ConnectionOverride = customCs;
        }

        bool forceSandbox = args.Contains("--sandbox");
        bool updateDb = args.Contains("--update-db");

        Console.WriteLine("======================================================================");
        Console.WriteLine("                ابزار استعلام زنده سامانه مؤدیان                      ");
        Console.WriteLine("======================================================================");

        var db = new CL_CCNNMANAGER();
        string dbName;
        try
        {
            dbName = db.DoGetDataSQL<string>("SELECT DB_NAME()").FirstOrDefault() ?? "نامشخص";
            Console.WriteLine($"دیتابیس متصل  : {dbName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ خطا در اتصال به دیتابیس: {ex.Message}");
            return 2;
        }

        // بررسی اینکه آیا target شماره فاکتور، شماره مالیاتی، یا کد رهگیری است
        TaxRow? taxRow = null;
        if (double.TryParse(target, out var num))
        {
            taxRow = db.DoGetDataSQL<TaxRow>(
                "SELECT TOP 1 NUMBER, Taxid, UID, RefrenceNumber, TheStatus, ApiTypeSent, TheError, TheWarning, CRT " +
                "FROM dbo.TAXDTL WHERE NUMBER = @num ORDER BY CRT DESC",
                new { num }).FirstOrDefault();

            if (taxRow != null)
            {
                Console.WriteLine($"فاکتور یافت شد : شماره {taxRow.NUMBER} | وضعیت ثبت‌شده: {taxRow.TheStatus ?? "NULL"}");
                Console.WriteLine($"شماره مالیاتی : {taxRow.Taxid ?? "ندارد"}");
                Console.WriteLine($"کد رهگیری ثبت : {taxRow.RefrenceNumber ?? "ندارد"}");
                Console.WriteLine($"UID ثبت‌شده   : {taxRow.UID ?? "ندارد"}");
                Console.WriteLine($"تاریخ ارسال   : {taxRow.CRT:yyyy-MM-dd HH:mm:ss}");
            }
            else
            {
                Console.WriteLine($"اطلاعاتی در TAXDTL برای شماره فاکتور {target} یافت نشد؛ ورودی به عنوان کد رهگیری تلقی می‌شود.");
            }
        }
        else if (target.Length == 22)
        {
            taxRow = db.DoGetDataSQL<TaxRow>(
                "SELECT TOP 1 NUMBER, Taxid, UID, RefrenceNumber, TheStatus, ApiTypeSent, TheError, TheWarning, CRT " +
                "FROM dbo.TAXDTL WHERE Taxid = @taxid ORDER BY CRT DESC",
                new { taxid = target }).FirstOrDefault();
        }

        string referenceNumber = taxRow?.RefrenceNumber ?? target;
        string? uid = taxRow?.UID;
        string? taxid = taxRow?.Taxid;
        bool isSandbox = forceSandbox || (taxRow?.ApiTypeSent == false);
        string taxUrl = isSandbox ? SandboxUrl : MainUrl;

        // خواندن کلید و شناسه حافظه مالیاتی از جدول SAZMAN
        SazmanRow? sazman;
        try
        {
            sazman = db.DoGetDataSQL<SazmanRow>("SELECT TOP 1 MEMORYID, MEMORYIDsand, PRIVIATEKEY FROM dbo.SAZMAN").FirstOrDefault();
            if (sazman == null)
            {
                Console.WriteLine("❌ هیچ رکوردی در جدول SAZMAN یافت نشد.");
                return 3;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ خطا در خواندن جدول SAZMAN: {ex.Message}");
            return 3;
        }

        string memoryId = (isSandbox ? sazman.MEMORYIDsand : sazman.MEMORYID)?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(memoryId))
        {
            Console.WriteLine($"❌ شناسه حافظه مالیاتی ({(isSandbox ? "MEMORYIDsand" : "MEMORYID")}) در SAZMAN خالی است.");
            return 3;
        }

        string rawKey = sazman.PRIVIATEKEY ?? string.Empty;
        string privateKey = rawKey
            .Replace("-----BEGIN PRIVATE KEY-----\r\n", "")
            .Replace("-----BEGIN PRIVATE KEY-----", "")
            .Replace("\r\n-----END PRIVATE KEY-----\r\n", "")
            .Replace("-----END PRIVATE KEY-----", "")
            .Replace("\r", "")
            .Replace("\n", "")
            .Trim();

        if (string.IsNullOrWhiteSpace(privateKey))
        {
            Console.WriteLine("❌ کلید خصوصی (PRIVIATEKEY) در SAZMAN خالی است.");
            return 3;
        }

        Console.WriteLine($"محیط          : {(isSandbox ? "سندباکس (آزمایشی)" : "اصلی (سامانه مؤدیان)")}");
        Console.WriteLine($"شناسه حافظه   : {memoryId}");
        Console.WriteLine($"کد رهگیری هدف : {referenceNumber}");
        Console.WriteLine($"آدرس سامانه   : {taxUrl}");
        Console.WriteLine("----------------------------------------------------------------------");

        // گام ۱: مقداردهی اولیه SDK
        Console.Write("۱. ارتباط با سرویس SDK...");
        try
        {
            TaxApiService.Instance.Init(
                memoryId,
                new SignatoryConfig(privateKey, null),
                new NormalProperties(ClientType.SELF_TSP),
                taxUrl);
            Console.WriteLine(" [موفق]");
        }
        catch (Exception ex)
        {
            Console.WriteLine($" [ناموفق] - {ex.Message}");
            return 4;
        }

        // گام ۲: بررسی دسترسی به سرور و اختلاف ساعت
        Console.Write("۲. دریافت زمان سرور مؤدیان...");
        try
        {
            var serverInfo = TaxApiService.Instance.TaxApis.GetServerInformation();
            var serverTime = DateTimeOffset.FromUnixTimeMilliseconds(serverInfo.ServerTime)
                                           .ToOffset(TimeSpan.FromHours(3.5));
            Console.WriteLine($" [موفق: {serverTime:yyyy-MM-dd HH:mm:ss} به وقت ایران]");
        }
        catch (Exception ex)
        {
            Console.WriteLine($" [هشدار] - سرور پاسخ نداد: {ex.Message}");
        }

        // گام ۳: دریافت توکن
        Console.Write("۳. دریافت توکن احراز هویت...");
        try
        {
            var token = TaxApiService.Instance.TaxApis.RequestToken();
            Console.WriteLine($" [موفق - اعتبار: {token.ExpiresIn} میلی‌ثانیه]");
        }
        catch (Exception ex)
        {
            Console.WriteLine($" [شکست] - احراز هویت انجام نشد: {ex.Message}");
            if (ex.InnerException != null)
                Console.WriteLine($"   جزئیات: {ex.InnerException.Message}");
            return 5;
        }

        // گام ۴: استعلام
        Console.WriteLine($"۴. استعلام با کد رهگیری '{referenceNumber}'...");
        InquiryResultModel? inquiryResult = null;
        try
        {
            var list = TaxApiService.Instance.TaxApis.InquiryByReferenceId(new List<string> { referenceNumber });
            inquiryResult = list?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ خطا در فراخوانی InquiryByReferenceId: {ex.Message}");
            if (ex.InnerException != null)
                Console.WriteLine($"   جزئیات: {ex.InnerException.Message}");
        }

        // در صورتی که استعلام با کد رهگیری پاسخی نداشت و UID موجود باشد، با UID هم استعلام می‌گیریم
        if ((inquiryResult == null || string.IsNullOrWhiteSpace(inquiryResult.Status) || inquiryResult.Status == "NOT_FOUND") && !string.IsNullOrWhiteSpace(uid))
        {
            Console.WriteLine($"   تلاش تکمیلی: استعلام با UID '{uid}' و شناسه حافظه '{memoryId}'...");
            try
            {
                var uidResults = TaxApiService.Instance.TaxApis.InquiryByUidAndFiscalId(
                    new List<UidAndFiscalId> { new UidAndFiscalId(uid, memoryId) });
                var match = uidResults?.FirstOrDefault(x => x != null &&
                    (string.IsNullOrWhiteSpace(x.Uid) || string.Equals(x.Uid.Trim(), uid.Trim(), StringComparison.OrdinalIgnoreCase)));
                if (match != null && !string.IsNullOrWhiteSpace(match.Status))
                {
                    inquiryResult = match;
                    Console.WriteLine($"   پاسخ از طریق UID دریافت شد (Status={match.Status})");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   استعلام با UID ناموفق بود: {ex.Message}");
            }
        }

        Console.WriteLine("----------------------------------------------------------------------");
        Console.WriteLine("                            نتیجه سامانه                              ");
        Console.WriteLine("----------------------------------------------------------------------");

        if (inquiryResult == null)
        {
            Console.WriteLine("❌ سامانه پاسخی برنگرداند (نتیجه خالی).");
            return 1;
        }

        Console.WriteLine($"وضعیت پاسخ (Status) : {inquiryResult.Status ?? "(خالی)"}");
        Console.WriteLine($"کد رهگیری           : {inquiryResult.ReferenceNumber ?? "(خالی)"}");
        Console.WriteLine($"شناسه بسته (UID)    : {inquiryResult.Uid ?? "(خالی)"}");

        string? dataJson = inquiryResult.Data?.ToString();
        if (!string.IsNullOrWhiteSpace(dataJson))
        {
            try
            {
                var parsed = JObject.Parse(dataJson);
                var confRef = parsed["confirmationReferenceId"]?.ToString();
                if (!string.IsNullOrWhiteSpace(confRef))
                    Console.WriteLine($"کد پیگیری مالیاتی   : {confRef}");

                var errors = parsed["error"] as JArray;
                if (errors != null && errors.Count > 0)
                {
                    Console.WriteLine("خطاها:");
                    foreach (var e in errors)
                        Console.WriteLine($"  ⛔ [{e["code"]}] {e["message"]}");
                }

                var warnings = parsed["warning"] as JArray;
                if (warnings != null && warnings.Count > 0)
                {
                    Console.WriteLine("هشدارها:");
                    foreach (var w in warnings)
                        Console.WriteLine($"  ⚠️  [{w["code"]}] {w["message"]}");
                }
            }
            catch
            {
                Console.WriteLine($"داده خام:\n{dataJson}");
            }
        }

        Console.WriteLine();
        var normStatus = (inquiryResult.Status ?? "").Trim().ToUpperInvariant();
        switch (normStatus)
        {
            case "SUCCESS":
                Console.WriteLine("✅ فاکتور با موفقیت در سامانه مؤدیان ثبت و تأیید شده است.");
                break;
            case "PENDING":
            case "IN_PROGRESS":
                Console.WriteLine("⏳ صورتحساب در صف پردازش سامانه است (هنوز نهایی نشده).");
                break;
            case "FAILED":
                Console.WriteLine("❌ صورتحساب توسط سامانه مؤدیان رد شده است.");
                break;
            case "NOT_FOUND":
                Console.WriteLine("🔍 صورتحساب با این مشخصات در سامانه یافت نشد.");
                break;
            default:
                Console.WriteLine($"ℹ️ وضعیت گزارش‌شده: {inquiryResult.Status}");
                break;
        }

        // به‌روزرسانی در دیتابیس در صورت درخواست کاربر
        if (updateDb)
        {
            Console.WriteLine();
            Console.Write("در حال به‌روزرسانی TAXDTL بر اساس پاسخ سامانه... ");
            try
            {
                var written = CL_ESTELAM.SaveInquiryResult(
                    db, inquiryResult, referenceNumber, taxid: taxid, uid: uid);
                Console.WriteLine($"[انجام شد: وضعیت ثبت‌شده = {written ?? "بدون تغییر"}]");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[خطا در ثبت: {ex.Message}]");
            }
        }

        Console.WriteLine("======================================================================");
        return 0;
    }

    private class TaxRow
    {
        public double NUMBER { get; set; }
        public string? Taxid { get; set; }
        public string? UID { get; set; }
        public string? RefrenceNumber { get; set; }
        public string? TheStatus { get; set; }
        public bool? ApiTypeSent { get; set; }
        public string? TheError { get; set; }
        public string? TheWarning { get; set; }
        public DateTime? CRT { get; set; }
    }

    private class SazmanRow
    {
        public string? MEMORYID { get; set; }
        public string? MEMORYIDsand { get; set; }
        public string? PRIVIATEKEY { get; set; }
    }
}
