using System.Net.Http.Json;
using System.Text.Json;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.FUNCTIONS;
using Prg_Moadian.Service;
using TaxCollectData.Library.Business;
using TaxCollectData.Library.Dto.Content;

namespace MoadianLocalTest;

/// <summary>
/// هارنس تست محلی. به سرور ساختگی moadian_mock.py وصل می‌شود و سناریوهای
/// واقعی سامانه مؤدیان را بدون نیاز به دیتابیس یا اینترنت اجرا می‌کند.
///
///   1) python TestTools\LocalMoadian\moadian_mock.py
///   2) dotnet run --project TestTools\LocalMoadian
/// </summary>
internal static class Program
{
    private const string MemoryId = "A2HGPP";
    private static string BaseUrl = "http://127.0.0.1:9090/";

    private static readonly HttpClient Control = new();
    private static TaxService _tax = null!;

    private static int _pass, _fail;
    private static readonly List<string> Failures = new();

    private static int Main(string[] args)
    {
        var urlArg = args.FirstOrDefault(a => a.StartsWith("http", StringComparison.OrdinalIgnoreCase));
        if (urlArg != null) BaseUrl = urlArg;
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        Banner("گروه ۱ — سریال صورتحساب (Inno)");
        G1_Serial();

        Banner("گروه ۲ — قواعد مشترک (MoadianRules)");
        G2_Rules();

        Banner("گروه ۲۰ — قفل رفتار (چیزی که کار می‌کرده دست نخورد)");
        Invariants.Run(Check);

        Banner("گروه ۲۳ — ریاضیات فرم صورتحساب اصلاحی");
        CorrectionPath.Run(Check, Console.WriteLine);

        Banner("گروه ۲۴ — دکمه ارسال مجدد فاکتور");
        ResendPath.Run(Check, Console.WriteLine, args.Contains("--bulk"));

        if (!ServerAlive())
        {
            Console.WriteLine();
            Console.WriteLine($"سرور ساختگی روی {BaseUrl} بالا نیست.");
            Console.WriteLine("    python TestTools\\LocalMoadian\\moadian_mock.py");

            // رد شدن از تست‌ها «موفقیت» نیست. اگر کسی انتظار اجرای کامل داشته
            // (یعنی --bulk داده) یا آدرس را صریح مشخص کرده، این یک شکست است.
            bool expectedFull = args.Contains("--bulk") || args.Contains("--record") ||
                                urlArg != null;
            Check("۰-۱ سرور ساختگی در دسترس است", !expectedFull,
                  expectedFull ? $"تست‌های شبکه‌ای و دیتابیسی اجرا نشدند — {BaseUrl}" : "");
            if (!expectedFull)
                Console.WriteLine("  (فقط تست‌های آفلاین اجرا شد — برای اجرای کامل سرور را بالا بیاورید)");
            return Summary();
        }

        Reset();
        string key = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "test_private_key.txt")).Trim();
        _tax = new TaxService(MemoryId, key, BaseUrl);
        _tax.RequestToken();

        Banner("گروه ۳ — شماره منحصربه‌فرد مالیاتی (Taxid)");
        G3_Taxid();

        Banner("گروه ۴ — هویت خریدار");
        G4_Buyer();

        Banner("گروه ۵ — کد شعبه");
        G5_BranchCode();

        Banner("گروه ۶ — تاریخ و مهلت ارسال");
        G6_Dates();

        Banner("گروه ۷ — مبالغ و اقلام");
        G7_Amounts();

        Banner("گروه ۸ — صورتحساب ارجاعی (اصلاحی / ابطالی / برگشتی)");
        G8_References();

        Banner("گروه ۹ — زنجیره اصلاحی پلکانی");
        G9_Chain();

        Banner("گروه ۱۰ — سریال در صورتحساب ارجاعی");
        G10_ReferenceSerial();

        Banner("گروه ۱۱ — ارسال گروهی (چند بسته در یک درخواست)");
        G11_Bulk();

        Banner("گروه ۱۲ — استعلام");
        G12_Inquiry();

        Banner("گروه ۱۳ — نوع صورتحساب (اول / دوم / سوم)");
        G13_InvoiceType();

        Banner("گروه ۱۴ — روش تسویه (نقدی / نسیه / نقدی-نسیه)");
        G14_Settlement();

        Banner("گروه ۱۵ — ترکیب موضوع × نوع × روش تسویه");
        G15_Matrix();

        Banner("گروه ۱۷ — سازگاری با SDK و قالب JSON روی سیم");
        WireFormat.Run(BaseUrl, Control, Check, Console.WriteLine);

        if (args.Contains("--bulk"))
        {
            Banner("گروه ۱۶ — ارسال گروهی سنگین روی دیتابیس واقعی");
            try
            {
                BulkStress.Run(BaseUrl.TrimEnd('/') + "/sandbox/", Check, Console.WriteLine);
            }
            catch (Exception ex)
            {
                Check("۱۶ اجرای تست گروهی", false, ex.Message);
            }

            Banner("گروه ۲۱ و ۲۲ — مسیر ارسال تکی و مقایسه‌اش با گروهی");
            try
            {
                SinglePath.Run(BaseUrl.TrimEnd('/') + "/sandbox/", Control, Check, Console.WriteLine);
            }
            catch (Exception ex)
            {
                Check("۲۱ اجرای تست مسیر تکی", false, ex.Message);
            }

            Banner("گروه ۱۹ — تثبیت رفتار (Golden)");
            try
            {
                Golden.Run(BaseUrl.TrimEnd('/') + "/sandbox/", Control,
                           args.Contains("--record"), Check, Console.WriteLine);
            }
            catch (Exception ex)
            {
                Check("۱۹ اجرای تست تثبیت رفتار", false, ex.Message);
            }

            Banner("گروه ۱۸ — دو محصول: MrCorrect و DenaFaraz");
            try
            {
                TwoProducts.Run(BaseUrl.TrimEnd('/') + "/sandbox/", Check, Console.WriteLine);
            }
            catch (Exception ex)
            {
                Check("۱۸ اجرای تست دو محصول", false, ex.Message);
            }
        }

        return Summary();
    }

    // ================================================================ گروه ۱

    private static void G1_Serial()
    {
        var fn = new CL_FUNTIONS();

        var seen = new Dictionary<string, long>();
        long collisions = 0, first = -1;
        for (long n = 0; n <= 2_000_000; n++)
        {
            string inno = fn.GenerateFixedLengthInno("1405", n);
            if (seen.ContainsKey(inno)) { collisions++; if (first < 0) first = n; }
            else seen[inno] = n;
        }
        Check("۱-۱ سریال ۰ تا ۲٬۰۰۰٬۰۰۰ بدون هیچ تداخلی",
              collisions == 0, collisions == 0 ? "" : $"{collisions} تداخل، اولین در {first}");

        Check("۱-۲ همه سریال‌ها دقیقا ۱۰ کاراکترند",
              seen.Keys.All(k => k.Length == 10));

        Check("۱-۳ صفر → 1405000000", fn.GenerateFixedLengthInno("1405", 0) == "1405000000");
        Check("۱-۴ مرز ده‌دهی ۹۹۹٬۹۹۹ → 1405999999",
              fn.GenerateFixedLengthInno("1405", 999_999) == "1405999999");
        Check("۱-۵ اولین هگز ۱٬۰۰۰٬۰۰۰ → 1405A00000",
              fn.GenerateFixedLengthInno("1405", 1_000_000) == "1405A00000");
        Check("۱-۶ آخرین ظرفیت ۷٬۲۹۱٬۴۵۵ → 1405FFFFFF",
              fn.GenerateFixedLengthInno("1405", 7_291_455) == "1405FFFFFF");

        bool threw = false;
        try { fn.GenerateFixedLengthInno("1405", 7_291_456); }
        catch { threw = true; }
        Check("۱-۷ فراتر از ظرفیت، خطای روشن می‌دهد (سکوت نمی‌کند)", threw);

        Check("۱-۸ سال در چهار کاراکتر اول می‌ماند",
              fn.GenerateFixedLengthInno("1406", 5).StartsWith("1406"));
    }

    // ================================================================ گروه ۲

    private static void G2_Rules()
    {
        // --- ValidateBuyer : معافیت‌ها
        Check("۲-۱ صورتحساب نوع دوم (inty=2) از کنترل خریدار معاف است",
              MoadianRules.ValidateBuyer(2, 1, 2, null, null, null, out _));
        Check("۲-۲ صورتحساب نوع سوم (inty=3) معاف است",
              MoadianRules.ValidateBuyer(3, 1, 2, null, null, null, out _));
        Check("۲-۳ الگوی صادرات (inp=7) معاف است",
              MoadianRules.ValidateBuyer(1, 7, 2, null, null, null, out _));
        Check("۲-۴ الگوی بورس (inp=11) معاف است",
              MoadianRules.ValidateBuyer(1, 11, 2, null, null, null, out _));

        // --- ValidateBuyer : شماره اقتصادی
        Check("۲-۵ حقیقی با شماره اقتصادی ۱۴ رقمی",
              MoadianRules.ValidateBuyer(1, 1, 1, "12345678901234", null, null, out _));
        Check("۲-۶ حقوقی با شماره اقتصادی ۱۱ رقمی",
              MoadianRules.ValidateBuyer(1, 1, 2, "12345678901", null, null, out _));
        Check("۲-۷ مشارکت مدنی با شماره اقتصادی ۱۱ رقمی",
              MoadianRules.ValidateBuyer(1, 1, 3, "12345678901", null, null, out _));
        Check("۲-۸ اتباع غیرایرانی با شماره اقتصادی ۱۴ رقمی",
              MoadianRules.ValidateBuyer(1, 1, 4, "12345678901234", null, null, out _));
        Check("۲-۹ حقوقی با ۱۰ رقم رد می‌شود",
              !MoadianRules.ValidateBuyer(1, 1, 2, "1234567890", null, null, out _));
        Check("۲-۱۰ حقیقی با ۱۱ رقم رد می‌شود",
              !MoadianRules.ValidateBuyer(1, 1, 1, "12345678901", null, null, out _));
        Check("۲-۱۱ شماره اقتصادی با حرف رد می‌شود",
              !MoadianRules.ValidateBuyer(1, 1, 2, "1234567890A", null, null, out _));

        // --- ValidateBuyer : مسیر دوم (شماره ملی / کد فراگیر + کد پستی)
        Check("۲-۱۲ حقیقی با شماره ملی ۱۰ رقمی و کد پستی",
              MoadianRules.ValidateBuyer(1, 1, 1, null, "1234567890", "1234567890", out _));
        Check("۲-۱۳ اتباع با کد فراگیر ۱۲ رقمی و کد پستی",
              MoadianRules.ValidateBuyer(1, 1, 4, null, "123456789012", "1234567890", out _));
        Check("۲-۱۴ حقیقی با شماره ملی ولی بدون کد پستی رد می‌شود",
              !MoadianRules.ValidateBuyer(1, 1, 1, null, "1234567890", null, out _));
        Check("۲-۱۵ اتباع با کد فراگیر ۱۰ رقمی رد می‌شود",
              !MoadianRules.ValidateBuyer(1, 1, 4, null, "1234567890", "1234567890", out _));
        Check("۲-۱۶ حقوقی بدون شماره اقتصادی رد می‌شود (مسیر دوم ندارد)",
              !MoadianRules.ValidateBuyer(1, 1, 2, null, "12345678901", "1234567890", out _));
        Check("۲-۱۷ نوع شخص نامعتبر (tob=5) رد می‌شود",
              !MoadianRules.ValidateBuyer(1, 1, 5, "12345678901", null, null, out _));
        Check("۲-۱۸ پیام خطا خالی نیست",
              !MoadianRules.ValidateBuyer(1, 1, 2, "123", null, null, out var msg) &&
              !string.IsNullOrWhiteSpace(msg));

        // --- ExpectedBidLength
        Check("۲-۱۹ طول شناسه: حقیقی ۱۰", MoadianRules.ExpectedBidLength(1) == 10);
        Check("۲-۲۰ طول شناسه: حقوقی ۱۱", MoadianRules.ExpectedBidLength(2) == 11);
        Check("۲-۲۱ طول شناسه: مشارکت مدنی ۱۱", MoadianRules.ExpectedBidLength(3) == 11);
        Check("۲-۲۲ طول شناسه: اتباع ۱۲", MoadianRules.ExpectedBidLength(4) == 12);

        // --- SanitizeBid
        Check("۲-۲۳ شناسه ملی درست عبور می‌کند",
              MoadianRules.SanitizeBid(2, "12345678901") == "12345678901");
        Check("۲-۲۴ شناسه ملی با طول غلط حذف می‌شود",
              MoadianRules.SanitizeBid(2, "1234567890") == null);
        Check("۲-۲۵ شناسه ملی غیرعددی حذف می‌شود",
              MoadianRules.SanitizeBid(2, "1234567890X") == null);
        Check("۲-۲۶ مقدار خالی null می‌ماند",
              MoadianRules.SanitizeBid(2, "   ") == null);

        // --- NormalizeBranchCode
        Check("۲-۲۷ کد شعبه ۴ رقمی دست‌نخورده", MoadianRules.NormalizeBranchCode("0123") == "0123");
        Check("۲-۲۸ کد شعبه کوتاه صفر می‌گیرد", MoadianRules.NormalizeBranchCode("12") == "0012");
        Check("۲-۲۹ کد شعبه تک‌رقمی → 0007", MoadianRules.NormalizeBranchCode("7") == "0007");
        Check("۲-۳۰ کد شعبه بلند null می‌شود", MoadianRules.NormalizeBranchCode("12345") == null);
        Check("۲-۳۱ کد شعبه غیرعددی null می‌شود", MoadianRules.NormalizeBranchCode("12a4") == null);
        Check("۲-۳۲ کد شعبه خالی null می‌ماند", MoadianRules.NormalizeBranchCode("") == null);

        // --- ماده ۹ / مهلت ارسال
        Check("۲-۳۳ فاکتور امروز موضوع ماده ۹ نیست",
              !MoadianRules.IsArticle9(DateTime.Now, DateTime.Now));
        Check("۲-۳۴ فاکتور ۱۱ روزه هنوز در مهلت است",
              !MoadianRules.IsArticle9(DateTime.Now.AddDays(-11), DateTime.Now));
        Check("۲-۳۵ فاکتور ۴۰ روزه موضوع ماده ۹ است",
              MoadianRules.IsArticle9(DateTime.Now.AddDays(-40), DateTime.Now));
        Check("۲-۳۶ مهلت قابل تنظیم است (هاردکد نیست)",
              MoadianRules.SendDeadlineDays > 0);
        Check("۲-۳۷ ماده ۹ خودکار خاموش است (Insr همیشه null)",
              MoadianRules.ResolveInsr(DateTime.Now.AddDays(-90), DateTime.Now) == null);

        // --- مبنای تسویه (FAQ ص۳۱ س۱۰-۵ : cap = tbill - tvam - todam)
        Check("۲-۳۸ مبنای تسویه = کل منهای مالیات و عوارض",
              MoadianRules.SettlementBase(1_000_000m, 90_000m, 10_000m) == 900_000m);
        Check("۲-۳۹ بدون مالیات، مبنا برابر مبلغ کل است",
              MoadianRules.SettlementBase(500_000m, 0m, 0m) == 500_000m);
        Check("۲-۴۰ نتیجه منفی به صفر بریده می‌شود",
              MoadianRules.SettlementBase(100m, 500m, 0m) == 0m);
    }

    // ================================================================ گروه ۳

    private static void G3_Taxid()
    {
        var inv = NewInvoice(ins: 1);
        Check("۳-۱ شماره مالیاتی دقیقا ۲۲ کاراکتر است", inv.Taxid!.Length == 22, inv.Taxid);
        Check("۳-۲ با شناسه حافظه شروع می‌شود", inv.Taxid!.StartsWith(MemoryId), inv.Taxid);

        var r = Send(inv, "اصلی سالم");
        Check("۳-۳ صورتحساب سالم پذیرفته می‌شود", r.Ok, r.Codes);

        var dup = NewInvoice(ins: 1);
        dup.Taxid = inv.Taxid;
        var r2 = Send(dup, "همان شماره مالیاتی");
        Check("۳-۴ شماره مالیاتی تکراری با 0300101 رد می‌شود",
              !r2.Ok && r2.Codes.Contains("0300101"), r2.Codes);

        var bad = NewInvoice(ins: 1);
        bad.Taxid = "SHORT123";
        var r3 = Send(bad, "شماره مالیاتی کوتاه");
        Check("۳-۵ شماره مالیاتی با طول غلط با 0300101 رد می‌شود",
              !r3.Ok && r3.Codes.Contains("0300101"), r3.Codes);

        var a = _tax.RequestTaxId(MemoryId, DateTime.Now);
        var b = _tax.RequestTaxId(MemoryId, DateTime.Now);
        Check("۳-۶ دو فراخوانی پشت‌سرهم شماره یکسان نمی‌دهند (رفع 0300101 پس از ابطالی)",
              a != b, $"{a} / {b}");
    }

    // ================================================================ گروه ۴

    private static void G4_Buyer()
    {
        var cases = new (int tob, string tinb, string bid, string bpc, bool expectOk, string label)[]
        {
            (1, "12345678901234", null, null, true,  "حقیقی با شماره اقتصادی ۱۴ رقمی"),
            (2, "12345678901",    null, null, true,  "حقوقی با شماره اقتصادی ۱۱ رقمی"),
            (3, "12345678901",    null, null, true,  "مشارکت مدنی با ۱۱ رقم"),
            (4, "12345678901234", null, null, true,  "اتباع غیرایرانی با ۱۴ رقم"),
            (2, "1234567890",     null, null, false, "حقوقی با ۱۰ رقم"),
            (1, "12345678901",    null, null, false, "حقیقی با ۱۱ رقم"),
            (1, null, "1234567890",   "1234567890", true,  "حقیقی با شماره ملی + کد پستی"),
            (4, null, "123456789012", "1234567890", true,  "اتباع با کد فراگیر + کد پستی"),
            (4, null, "1234567890",   "1234567890", false, "اتباع با کد فراگیر ۱۰ رقمی"),
        };

        int i = 1;
        foreach (var c in cases)
        {
            var inv = NewInvoice(ins: 1);
            inv.Tob = c.tob; inv.Tinb = c.tinb; inv.Bid = c.bid; inv.Bpc = c.bpc;
            var r = Send(inv, c.label);
            Check($"۴-{Fa(i++)} {c.label} → {(c.expectOk ? "پذیرفته" : "رد")}",
                  r.Ok == c.expectOk, r.Codes);
        }

        var exp = NewInvoice(ins: 1);
        exp.Inp = 7; exp.Tinb = null; exp.Tob = 2;
        var re = Send(exp, "صادرات بدون شماره اقتصادی");
        Check($"۴-{Fa(i++)} الگوی صادرات بدون شماره اقتصادی پذیرفته می‌شود", re.Ok, re.Codes);

        var bad = NewInvoice(ins: 1);
        bad.Tob = 5;
        var rb = Send(bad, "نوع شخص نامعتبر");
        Check($"۴-{Fa(i)} نوع شخص خریدار نامعتبر رد می‌شود", !rb.Ok, rb.Codes);
    }

    // ================================================================ گروه ۵

    private static void G5_BranchCode()
    {
        var bad = NewInvoice(ins: 1); bad.Bbc = "123";
        var r = Send(bad, "کد شعبه ۳ رقمی");
        Check("۵-۱ کد شعبه ۳ رقمی با 0101504 رد می‌شود",
              !r.Ok && r.Codes.Contains("0101504"), r.Codes);

        var fixedUp = NewInvoice(ins: 1);
        fixedUp.Bbc = MoadianRules.NormalizeBranchCode("123");
        var r2 = Send(fixedUp, "پس از نرمال‌سازی");
        Check("۵-۲ همان کد پس از NormalizeBranchCode پذیرفته می‌شود", r2.Ok, r2.Codes);

        var dropped = NewInvoice(ins: 1);
        dropped.Bbc = MoadianRules.NormalizeBranchCode("12345");
        var r3 = Send(dropped, "کد شعبه بلند حذف‌شده");
        Check("۵-۳ کد شعبه غیرقابل‌اصلاح حذف می‌شود و صورتحساب می‌رود", r3.Ok, r3.Codes);

        var ok4 = NewInvoice(ins: 1); ok4.Bbc = "0042";
        var r4 = Send(ok4, "کد شعبه ۴ رقمی");
        Check("۵-۴ کد شعبه ۴ رقمی پذیرفته می‌شود", r4.Ok, r4.Codes);
    }

    // ================================================================ گروه ۶

    private static void G6_Dates()
    {
        var today = Send(NewInvoice(ins: 1), "امروز");
        Check("۶-۱ صورتحساب امروز پذیرفته می‌شود", today.Ok, today.Codes);

        var d5 = Send(NewInvoice(ins: 1, issuedAt: DateTime.Now.AddDays(-5)), "۵ روز پیش");
        Check("۶-۲ صورتحساب ۵ روزه پذیرفته می‌شود", d5.Ok, d5.Codes);

        var d40 = Send(NewInvoice(ins: 1, issuedAt: DateTime.Now.AddDays(-40)), "۴۰ روز پیش");
        Check("۶-۳ صورتحساب ۴۰ روزه با 0200201 رد می‌شود",
              !d40.Ok && d40.Codes.Contains("0200201"), d40.Codes);

        var future = Send(NewInvoice(ins: 1, issuedAt: DateTime.Now.AddDays(5)), "۵ روز آینده");
        Check("۶-۴ تاریخ آینده با 0200201 رد می‌شود",
              !future.Ok && future.Codes.Contains("0200201"), future.Codes);

        var inv = NewInvoice(ins: 1);
        var back = DateTimeOffset.FromUnixTimeMilliseconds(inv.Indatim)
                                 .ToOffset(new TimeSpan(3, 30, 0)).DateTime;
        Check("۶-۵ تبدیل تاریخ رفت‌وبرگشت درست است (آفست ۳:۳۰ ایران)",
              Math.Abs((back - DateTime.Now).TotalMinutes) < 2, back.ToString("s"));
    }

    // ================================================================ گروه ۷

    private static void G7_Amounts()
    {
        var ok = Send(NewInvoice(ins: 1), "مبالغ سازگار");
        Check("۷-۱ مبالغ سازگار پذیرفته می‌شود", ok.Ok, ok.Codes);

        var bad = NewInvoice(ins: 1);
        bad.Tbill = 9_999_999;                       // با اقلام نمی‌خواند
        var r = Send(bad, "مبلغ کل ناسازگار با اقلام");
        Check("۷-۲ ناسازگاری مبلغ کل با اقلام با 0102705 رد می‌شود",
              !r.Ok && r.Codes.Contains("0102705"), r.Codes);

        var badSstid = NewInvoice(ins: 1);
        var r2 = Send(badSstid, "شناسه کالای نامعتبر", bodyMutator: b => b.Sstid = "12");
        Check("۷-۳ شناسه کالای نامعتبر با 0103301 رد می‌شود",
              !r2.Ok && r2.Codes.Contains("0103301"), r2.Codes);

        var multi = NewInvoice(ins: 1);
        multi.Tbill = 2_180_000; multi.Tvam = 180_000;
        multi.Tprdis = 2_000_000; multi.Tadis = 2_000_000;
        var r3 = SendMulti(multi, 2, "دو قلم کالا");
        Check("۷-۴ صورتحساب دو قلمی با مجموع درست پذیرفته می‌شود", r3.Ok, r3.Codes);
    }

    // ================================================================ گروه ۸

    private static void G8_References()
    {
        // ins=1 نباید مرجع داشته باشد
        var wrong = NewInvoice(ins: 1, irtaxid: "A2HGPP050E0000000000AB");
        var rw = Send(wrong, "اصلی با مرجع");
        Check("۸-۱ صورتحساب اصلی با مرجع رد می‌شود", !rw.Ok, rw.Codes);

        // ins=2 بدون مرجع
        var noRef = NewInvoice(ins: 2);
        var rn = Send(noRef, "اصلاحی بدون مرجع");
        Check("۸-۲ اصلاحی بدون مرجع با 0300601 رد می‌شود",
              !rn.Ok && rn.Codes.Contains("0300601"), rn.Codes);

        // مرجع ناموجود
        var ghost = NewInvoice(ins: 2, irtaxid: "A2HGPP050E9999999999ZZ");
        var rg = Send(ghost, "اصلاحی با مرجع ناموجود");
        Check("۸-۳ مرجع ناموجود با 0300601 رد می‌شود",
              !rg.Ok && rg.Codes.Contains("0300601"), rg.Codes);

        // ins نامعتبر
        var badIns = NewInvoice(ins: 9);
        var rb = Send(badIns, "موضوع صورتحساب = ۹");
        Check("۸-۴ موضوع صورتحساب نامعتبر رد می‌شود", !rb.Ok, rb.Codes);

        // برگشتی روی مرجع تاییدشده
        var baseInv = NewInvoice(ins: 1);
        Send(baseInv, "اصلی برای برگشتی");
        SetStatus(baseInv.Taxid!, "CONFIRMED");
        var ret = NewInvoice(ins: 4, irtaxid: baseInv.Taxid, inno: baseInv.Inno);
        var rr = Send(ret, "برگشتی");
        Check("۸-۵ صورتحساب برگشتی روی مرجع تاییدشده پذیرفته می‌شود", rr.Ok, rr.Codes);

        // ابطالی
        var cancelBase = NewInvoice(ins: 1);
        Send(cancelBase, "اصلی برای ابطال");
        SetStatus(cancelBase.Taxid!, "CONFIRMED");
        var cancel = NewInvoice(ins: 3, irtaxid: cancelBase.Taxid, inno: cancelBase.Inno);
        var rc = Send(cancel, "ابطالی");
        Check("۸-۶ ابطالی روی مرجع تاییدشده پذیرفته می‌شود", rc.Ok, rc.Codes);

        // اصلاحی روی صورتحساب ابطال‌شده
        var afterCancel = NewInvoice(ins: 2, irtaxid: cancelBase.Taxid, inno: cancelBase.Inno);
        var rac = Send(afterCancel, "اصلاحی روی صورتحساب ابطال‌شده");
        Check("۸-۷ اصلاحی روی صورتحساب ابطال‌شده با 0300601 رد می‌شود",
              !rac.Ok && rac.Codes.Contains("0300601"), rac.Codes);

        // وضعیت‌های مجاز مرجع
        foreach (var status in new[] { "SYSTEM_CONFIRMED", "NO_REACTION_NEEDED" })
        {
            var b = NewInvoice(ins: 1);
            Send(b, "اصلی (" + status + ")");
            SetStatus(b.Taxid!, status);
            var c = NewInvoice(ins: 2, irtaxid: b.Taxid, inno: b.Inno);
            var rs = Send(c, "اصلاحی روی مرجع " + status);
            Check($"۸-{Fa(status == "SYSTEM_CONFIRMED" ? 8 : 9)} مرجع با وضعیت {status} پذیرفته می‌شود",
                  rs.Ok, rs.Codes);
        }

        // مرجع ردشده
        var rej = NewInvoice(ins: 1);
        Send(rej, "اصلی (ردشده توسط خریدار)");
        SetStatus(rej.Taxid!, "REJECTED");
        var onRej = NewInvoice(ins: 2, irtaxid: rej.Taxid, inno: rej.Inno);
        var rrj = Send(onRej, "اصلاحی روی مرجع ردشده");
        // ص۱۶ بند ۳ شرط وضعیت را فقط وقتی می‌گذارد که مرجع *خودش* اصلاحی یا
        // برگشتی باشد. روی یک اصلیِ ردشده، متن سند منعی ندارد.
        //
        // ⚠ این برداشت از متن سند است و با سامانه واقعی آزموده نشده. اگر روزی
        //   دیدید سامانه چنین اصلاحی‌ای را رد می‌کند، اینجا و در شبیه‌ساز
        //   (moadian_mock.py، بخش صورتحساب ارجاعی) باید سخت‌گیرانه شود.
        Check("۸-۱۰ اصلاحی روی مرجعِ اصلیِ ردشده پذیرفته می‌شود (برداشت از ص۱۶ — آزموده‌نشده)",
              rrj.Ok, rrj.Codes);
    }

    // ================================================================ گروه ۹

    private static void G9_Chain()
    {
        // این سناریو عینا همان چیزی است که در سندباکس واقعی اتفاق افتاد.
        // پنج ارسال واقعی ثبت شده بود و قاعده زیر هر پنج را توضیح می‌دهد:
        //
        //   ص۱۶ بند ۳ : شرط «مرجع باید تایید شده باشد» فقط وقتی اعمال می‌شود
        //               که مرجع *خودش* اصلاحی یا برگشتی باشد.
        //   شرط دوم   : روی هر مرجع فقط یک اصلاحی/برگشتی غیرابطال‌شده مجاز است،
        //               مستقل از نوع مرجع.

        var original = NewInvoice(ins: 1);
        var r0 = Send(original, "اصلی");
        Check("۹-۱ اصلی ثبت شد", r0.Ok, r0.Codes);

        // اصلاحی روی اصلیِ هنوز تاییدنشده — در سندباکس واقعی این *پذیرفته* شد
        var c1 = NewInvoice(ins: 2, irtaxid: original.Taxid, inno: original.Inno);
        var r1 = Send(c1, "اصلاحی ۱ روی اصلیِ در انتظار واکنش");
        Check("۹-۲ اصلاحی روی اصلیِ تاییدنشده پذیرفته می‌شود (مرجع اصلی است، نه اصلاحی)",
              r1.Ok, r1.Codes);

        // اصلاحی موازی روی همان مرجع — شرط دوم
        var c2 = NewInvoice(ins: 2, irtaxid: original.Taxid, inno: original.Inno);
        var r2 = Send(c2, "اصلاحی موازی روی همان مرجع");
        Check("۹-۳ اصلاحی دوم روی همان مرجع رد می‌شود (زنجیره باید خطی باشد)",
              !r2.Ok && r2.Codes.Contains("0300601"), r2.Codes);

        // اصلاحی روی اصلاحیِ تاییدنشده — همان خطای ۲۲:۴۳ و ۲۲:۵۲ سندباکس
        var c3 = NewInvoice(ins: 2, irtaxid: c1.Taxid, inno: c1.Inno);
        var r3 = Send(c3, "اصلاحی پلکانی روی اصلاحیِ تاییدنشده");
        Check("۹-۴ اصلاحی روی اصلاحیِ تاییدنشده رد می‌شود (همان خطای سندباکس)",
              !r3.Ok && r3.Codes.Contains("0300601"), r3.Codes);

        // حالا با تایید همان اصلاحی
        SetStatus(c1.Taxid!, "CONFIRMED");
        var c4 = NewInvoice(ins: 2, irtaxid: c1.Taxid, inno: c1.Inno);
        var r4 = Send(c4, "اصلاحی پلکانی روی اصلاحیِ تاییدشده");
        Check("۹-۵ اصلاحی پلکانی مرتبه دوم پذیرفته می‌شود", r4.Ok, r4.Codes);

        SetStatus(c4.Taxid!, "CONFIRMED");
        var c5 = NewInvoice(ins: 2, irtaxid: c4.Taxid, inno: c4.Inno);
        var r5 = Send(c5, "اصلاحی پلکانی مرتبه سوم");
        Check("۹-۶ زنجیره سه‌مرحله‌ای تا انتها کار می‌کند", r5.Ok, r5.Codes);

        var c6 = NewInvoice(ins: 2, irtaxid: original.Taxid, inno: original.Inno);
        var r6 = Send(c6, "بازگشت به مرجع اول پس از دو پله");
        Check("۹-۷ بازگشت به مرجع اولِ زنجیره رد می‌شود",
              !r6.Ok && r6.Codes.Contains("0300601"), r6.Codes);

        // ص۱۷ بند ۲ : ابطالی نمی‌تواند مرجع باشد
        var baseForVoid = NewInvoice(ins: 1);
        Send(baseForVoid, "اصلی برای ابطال");
        var voided = NewInvoice(ins: 3, irtaxid: baseForVoid.Taxid, inno: baseForVoid.Inno);
        var rv = Send(voided, "ابطالی");
        if (rv.Ok)
        {
            var onVoid = NewInvoice(ins: 2, irtaxid: voided.Taxid, inno: voided.Inno);
            var rov = Send(onVoid, "اصلاحی روی یک ابطالی");
            Check("۹-۸ از صورتحساب ابطالی نمی‌توان به عنوان مرجع استفاده کرد (ص۱۷ بند ۲)",
                  !rov.Ok && rov.Codes.Contains("0300601"), rov.Codes);
        }
        else
        {
            Check("۹-۸ ابطالی برای آزمون مرجع ثبت شد", false, rv.Codes);
        }
    }

    // ================================================================ گروه ۱۰

    private static void G10_ReferenceSerial()
    {
        var baseInv = NewInvoice(ins: 1);
        Send(baseInv, "اصلی");
        SetStatus(baseInv.Taxid!, "CONFIRMED");

        // همان سریال مرجع — رفتار عادی برنامه
        var same = NewInvoice(ins: 2, irtaxid: baseInv.Taxid, inno: baseInv.Inno);
        var rs = Send(same, "اصلاحی با همان سریال مرجع");
        Check("۱۰-۱ اصلاحی با همان سریال مرجع پذیرفته می‌شود (رد نمی‌شود)",
              rs.Ok, rs.Codes);
        Check("۱۰-۲ و هیچ هشداری هم نمی‌گیرد",
              !rs.Codes.Contains("1300501"), rs.Codes);

        // سریال متفاوت — فقط هشدار، نه خطا
        var other = NewInvoice(ins: 1);
        Send(other, "اصلی دوم");
        SetStatus(other.Taxid!, "CONFIRMED");
        var diff = NewInvoice(ins: 2, irtaxid: other.Taxid);   // سریال تصادفی جدید
        var rd = Send(diff, "اصلاحی با سریال متفاوت");
        Check("۱۰-۳ سریال متفاوت با مرجع فقط هشدار 1300501 می‌گیرد",
              rd.Codes.Contains("1300501"), rd.Codes);
        Check("۱۰-۴ و صورتحساب با وجود هشدار پذیرفته می‌شود", rd.Ok, rd.Codes);
    }

    // ================================================================ گروه ۱۱

    private static void G11_Bulk()
    {
        var good1 = NewInvoice(ins: 1);
        var good2 = NewInvoice(ins: 1);
        var bad = NewInvoice(ins: 1);
        bad.Tob = 2; bad.Tinb = "1234567890";          // طول غلط

        var refs = SendPacketBatch(new[] { good1, good2, bad });
        Check("۱۱-۱ هر سه بسته کد رهگیری گرفتند", refs.Count == 3, refs.Count.ToString());

        var r1 = Lookup(refs[0]); var r2 = Lookup(refs[1]); var r3 = Lookup(refs[2]);
        Check("۱۱-۲ بسته اول پذیرفته شد", r1?.ok == true, Join(r1));
        Check("۱۱-۳ بسته دوم پذیرفته شد", r2?.ok == true, Join(r2));
        Check("۱۱-۴ بسته سوم مستقلا رد شد (بقیه را خراب نکرد)",
              r3?.ok == false && r3.Value.codes.Contains("0101204"), Join(r3));
    }

    // ================================================================ گروه ۱۲

    private static void G12_Inquiry()
    {
        var inv = NewInvoice(ins: 1);
        var r = Send(inv, "برای استعلام");
        Check("۱۲-۱ صورتحساب ثبت شد", r.Ok, r.Codes);
        Check("۱۲-۲ وضعیت اولیه در انتظار پردازش است", r.Status == "IN_PROGRESS", r.Status);

        SetStatus(inv.Taxid!, "CONFIRMED");
        var after = Lookup(_lastReference);
        Check("۱۲-۳ پس از تایید، استعلام وضعیت جدید را برمی‌گرداند",
              after?.status == "CONFIRMED", after?.status);

        var missing = Lookup("NOTHING-HERE");
        Check("۱۲-۴ کد رهگیری ناموجود چیزی برنمی‌گرداند", missing == null);
    }

    // ================================================================ گروه ۱۳

    private static void G13_InvoiceType()
    {
        var t1 = NewInvoice(ins: 1);
        var r1 = Send(t1, "نوع اول با شماره اقتصادی");
        Check("۱۳-۱ نوع اول با شناسایی کامل خریدار پذیرفته می‌شود", r1.Ok, r1.Codes);

        var t1bad = NewInvoice(ins: 1);
        t1bad.Tinb = null; t1bad.Bid = null; t1bad.Bpc = null;
        var r1b = Send(t1bad, "نوع اول بدون هیچ شناسه‌ای");
        Check("۱۳-۲ نوع اول بدون شناسه خریدار رد می‌شود",
              !r1b.Ok && r1b.Codes.Contains("0101204"), r1b.Codes);
        Check("۱۳-۳ و MoadianRules هم همان را می‌گیرد",
              !MoadianRules.ValidateBuyer(1, 1, 3, null, null, null, out _));

        var t2 = NewInvoice(ins: 1);
        t2.Inty = 2; t2.Tinb = null; t2.Bid = null; t2.Bpc = null;
        var r2 = Send(t2, "نوع دوم بدون شناسه خریدار");
        Check("۱۳-۴ نوع دوم بدون شناسه خریدار پذیرفته می‌شود", r2.Ok, r2.Codes);
        Check("۱۳-۵ و MoadianRules نوع دوم را معاف می‌داند",
              MoadianRules.ValidateBuyer(2, 1, 3, null, null, null, out _));

        var t3 = NewInvoice(ins: 1);
        t3.Inty = 3; t3.Tinb = null;
        var r3 = Send(t3, "نوع سوم بدون شماره اقتصادی");
        Check("۱۳-۶ نوع سوم بدون شماره اقتصادی پذیرفته می‌شود", r3.Ok, r3.Codes);

        var t9 = NewInvoice(ins: 1); t9.Inty = 9;
        var r9 = Send(t9, "نوع نهم");
        Check("۱۳-۷ نوع صورتحساب نامعتبر با 0300401 رد می‌شود",
              !r9.Ok && r9.Codes.Contains("0300401"), r9.Codes);
        Check("۱۳-۸ CalculateCapInsp هم inty نامعتبر را رد می‌کند",
              CapInsp(1, 1_000_000m, null, 9).err != null);
    }

    // ================================================================ گروه ۱۴

    private static void G14_Settlement()
    {
        var cash = CapInsp(setm: 1, tbill: 1_090_000m, cap: null, inty: 1);
        Check("۱۴-۱ نقدی: cap = کل صورتحساب، insp = صفر",
              cash.err == null && cash.cap == 1_090_000m && cash.insp == 0m,
              $"{cash.cap}/{cash.insp} {cash.err}");

        var credit = CapInsp(setm: 2, tbill: 1_090_000m, cap: null, inty: 1);
        Check("۱۴-۲ نسیه: cap = صفر، insp = کل صورتحساب",
              credit.err == null && credit.cap == 0m && credit.insp == 1_090_000m,
              $"{credit.cap}/{credit.insp} {credit.err}");

        var mixed = CapInsp(setm: 3, tbill: 1_090_000m, cap: 400_000m, inty: 1, tvam: 90_000m);
        Check("۱۴-۳ نقدی/نسیه: cap + insp = کل منهای مالیات و عوارض",
              mixed.err == null && mixed.cap == 400_000m && mixed.insp == 600_000m,
              $"{mixed.cap}/{mixed.insp} {mixed.err}");

        var noCap = CapInsp(setm: 3, tbill: 1_090_000m, cap: null, inty: 1, tvam: 90_000m);
        Check("۱۴-۴ نقدی/نسیه بدون مبلغ نقدی رد می‌شود", noCap.err != null, noCap.err);

        var tooBig = CapInsp(setm: 3, tbill: 1_090_000m, cap: 1_000_000m, inty: 1, tvam: 90_000m);
        Check("۱۴-۵ نقدی/نسیه با مبلغ نقدی برابر مبنا رد می‌شود (نسیه صفر می‌شود)",
              tooBig.err != null, tooBig.err);

        var overBasis = CapInsp(setm: 3, tbill: 1_090_000m, cap: 1_500_000m, inty: 1, tvam: 90_000m);
        Check("۱۴-۶ نقدی/نسیه با مبلغ نقدی بیش از کل رد می‌شود", overBasis.err != null, overBasis.err);

        var zeroBasis = CapInsp(setm: 3, tbill: 90_000m, cap: 10_000m, inty: 1, tvam: 90_000m);
        Check("۱۴-۷ وقتی کل صورتحساب فقط مالیات است، نقدی/نسیه ممکن نیست",
              zeroBasis.err != null, zeroBasis.err);

        var badSetms = new[] { 0, 4, 7, 99 };
        for (int k = 0; k < badSetms.Length; k++)
        {
            var r = CapInsp(setm: badSetms[k], tbill: 1_000_000m, cap: null, inty: 1);
            Check($"۱۴-{Fa(8 + k)} روش تسویه {badSetms[k]} رد می‌شود (فقط ۱ تا ۳ مجاز است)",
                  r.err != null, r.err);
        }

        var sCash = NewInvoice(ins: 1);
        sCash.Setm = 1; sCash.Cap = 1_090_000; sCash.Insp = 0;
        var rCash = Send(sCash, "نقدی");
        Check("۱۴-۱۲ صورتحساب نقدی پذیرفته می‌شود", rCash.Ok, rCash.Codes);

        var sCredit = NewInvoice(ins: 1);
        sCredit.Setm = 2; sCredit.Cap = 0; sCredit.Insp = 1_090_000;
        var rCredit = Send(sCredit, "نسیه");
        Check("۱۴-۱۳ صورتحساب نسیه پذیرفته می‌شود", rCredit.Ok, rCredit.Codes);

        var sMixed = NewInvoice(ins: 1);
        sMixed.Setm = 3; sMixed.Cap = 400_000; sMixed.Insp = 600_000;
        var rMixed = Send(sMixed, "نقدی/نسیه");
        Check("۱۴-۱۴ صورتحساب نقدی/نسیه پذیرفته می‌شود", rMixed.Ok, rMixed.Codes);

        var sCashBad = NewInvoice(ins: 1);
        sCashBad.Setm = 1; sCashBad.Cap = 1_090_000; sCashBad.Insp = 500_000;
        var rCashBad = Send(sCashBad, "نقدی ولی با مبلغ نسیه");
        Check("۱۴-۱۵ نقدی با مبلغ نسیه غیرصفر رد می‌شود",
              !rCashBad.Ok && rCashBad.Codes.Contains("0103003"), rCashBad.Codes);

        var sCreditBad = NewInvoice(ins: 1);
        sCreditBad.Setm = 2; sCreditBad.Cap = 500_000; sCreditBad.Insp = 1_090_000;
        var rCreditBad = Send(sCreditBad, "نسیه ولی با مبلغ نقدی");
        Check("۱۴-۱۶ نسیه با مبلغ نقدی غیرصفر رد می‌شود",
              !rCreditBad.Ok && rCreditBad.Codes.Contains("0102903"), rCreditBad.Codes);

        var sMixedBad = NewInvoice(ins: 1);
        sMixedBad.Setm = 3; sMixedBad.Cap = 400_000; sMixedBad.Insp = 400_000;
        var rMixedBad = Send(sMixedBad, "نقدی/نسیه با مجموع غلط");
        Check("۱۴-۱۷ نقدی/نسیه با مجموع نادرست رد می‌شود",
              !rMixedBad.Ok && rMixedBad.Codes.Contains("0102903"), rMixedBad.Codes);

        var sSetmBad = NewInvoice(ins: 1); sSetmBad.Setm = 5;
        var rSetmBad = Send(sSetmBad, "روش تسویه ۵");
        Check("۱۴-۱۸ روش تسویه نامعتبر با 0102804 رد می‌شود",
              !rSetmBad.Ok && rSetmBad.Codes.Contains("0102804"), rSetmBad.Codes);
    }

    // ================================================================ گروه ۱۵

    private static void G15_Matrix()
    {
        var subjects = new (int ins, string name)[]
        {
            (1, "اصلی"), (2, "اصلاحی"), (3, "ابطالی"), (4, "برگشتی"),
        };
        var settlements = new (int setm, decimal cap, decimal insp, string name)[]
        {
            (1, 1_090_000m, 0m,         "نقدی"),
            (2, 0m,         1_090_000m, "نسیه"),
            (3, 400_000m,   600_000m,   "نقدی/نسیه"),
        };

        int n = 1;
        foreach (var subject in subjects)
        {
            foreach (var st in settlements)
            {
                string? irtaxid = null, inno = null;
                if (subject.ins != 1)
                {
                    var baseInv = NewInvoice(ins: 1);
                    baseInv.Setm = st.setm; baseInv.Cap = st.cap; baseInv.Insp = st.insp;
                    var rb = Send(baseInv, $"مرجع برای {subject.name}/{st.name}");
                    if (!rb.Ok)
                    {
                        Check($"۱۵-{Fa(n++)} {subject.name} با تسویه {st.name}", false, "مرجع ثبت نشد");
                        continue;
                    }
                    SetStatus(baseInv.Taxid!, "CONFIRMED");
                    irtaxid = baseInv.Taxid; inno = baseInv.Inno;
                }

                var inv = NewInvoice(ins: subject.ins, irtaxid: irtaxid, inno: inno);
                inv.Setm = st.setm; inv.Cap = st.cap; inv.Insp = st.insp;
                var r = Send(inv, $"{subject.name} / {st.name}");
                Check($"۱۵-{Fa(n++)} {subject.name} با تسویه {st.name} پذیرفته می‌شود", r.Ok, r.Codes);
            }
        }

        foreach (var st in settlements)
        {
            var inv = NewInvoice(ins: 1);
            inv.Inty = 2; inv.Tinb = null; inv.Bid = null; inv.Bpc = null;
            inv.Setm = st.setm; inv.Cap = st.cap; inv.Insp = st.insp;
            var r = Send(inv, $"نوع دوم / {st.name}");
            Check($"۱۵-{Fa(n++)} صورتحساب نوع دوم با تسویه {st.name} پذیرفته می‌شود", r.Ok, r.Codes);
        }
    }

    /// <summary>فراخوانی CalculateCapInsp که private است، بدون دست زدن به کد اصلی.</summary>
    private static (decimal? cap, decimal? insp, string? err) CapInsp(
        int setm, decimal tbill, decimal? cap, int inty, decimal tvam = 0m, decimal todam = 0m)
    {
        var type = typeof(Prg_Moadian.Bulk.SendInvoiceBulk);
        var m = type.GetMethod("CalculateCapInsp",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)!;
        var instance = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
        var result = m.Invoke(instance, new object?[] { setm, tbill, cap, inty, tvam, todam })!;
        var rt = result.GetType();
        return ((decimal?)rt.GetField("Item1")!.GetValue(result),
                (decimal?)rt.GetField("Item2")!.GetValue(result),
                (string?)rt.GetField("Item3")!.GetValue(result));
    }

    // ================================================================ کمکی‌ها

    private sealed record SendResult(bool Ok, string Codes, string Status);

    private static string _lastReference = "";

    private static TaxModel.InvoiceModel.Header NewInvoice(
        int ins, string? irtaxid = null, string? inno = null, DateTime? issuedAt = null)
    {
        var when = issuedAt ?? DateTime.Now;
        var fn = new CL_FUNTIONS();

        return new TaxModel.InvoiceModel.Header
        {
            Taxid = _tax.RequestTaxId(MemoryId, when),
            Indatim = TaxService.ConvertDateToLong(when),
            Indati2m = TaxService.ConvertDateToLong(when),
            Inty = 1,
            Inno = inno ?? fn.GenerateFixedLengthInno("1405", Random.Shared.Next(1, 900_000)),
            Irtaxid = irtaxid,
            Inp = 1,
            Ins = ins,
            Tins = "11111111111",
            Tob = 3,
            Tinb = "12345678901",
            Setm = 1,
            Cap = 1_090_000,
            Insp = 0,
            Tbill = 1_090_000,
            Tvam = 90_000,
            Tprdis = 1_000_000,
            Tdis = 0,
            Tadis = 1_000_000,
        };
    }

    private static List<TaxModel.InvoiceModel.Body> NewBody(int count = 1) =>
        Enumerable.Range(0, count).Select(_ => new TaxModel.InvoiceModel.Body
        {
            Sstid = "2820000000000",
            Sstt = "کالای آزمایشی",
            Mu = "1627",
            Am = 10,
            Fee = 100_000,
            Prdis = 1_000_000,
            Dis = 0,
            Adis = 1_000_000,
            Vra = 9,
            Vam = 90_000,
            Tsstam = 1_090_000,
        }).ToList();

    private static SendResult Send(TaxModel.InvoiceModel.Header header, string label,
                                   Action<TaxModel.InvoiceModel.Body>? bodyMutator = null)
        => SendMulti(header, 1, label, bodyMutator);

    private static SendResult SendMulti(TaxModel.InvoiceModel.Header header, int lines, string label,
                                        Action<TaxModel.InvoiceModel.Body>? bodyMutator = null)
    {
        try
        {
            var body = NewBody(lines);
            if (bodyMutator != null) foreach (var b in body) bodyMutator(b);

            var resp = _tax.SendInvoices(header, body, new List<TaxModel.InvoiceModel.Payment>());
            _lastReference = resp.ReferenceNumber;

            var rec = Lookup(resp.ReferenceNumber);
            string codes = rec is null ? "(بدون پاسخ)" : string.Join(",", rec.Value.codes);
            Console.WriteLine($"      · {label}: {(rec?.ok == true ? "پذیرفته" : "رد")} {codes}");
            return new SendResult(rec?.ok == true, codes, rec?.status ?? "");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"      · {label}: استثنا — {ex.Message}");
            return new SendResult(false, "EXCEPTION:" + ex.Message, "");
        }
    }

    /// <summary>چند صورتحساب در یک درخواست — مسیر ارسال گروهی.</summary>
    private static List<string> SendPacketBatch(IEnumerable<TaxModel.InvoiceModel.Header> headers)
    {
        var dtos = headers.Select(h => new InvoiceDto
        {
            Header = new InvoiceHeaderDto
            {
                Taxid = h.Taxid, Indatim = h.Indatim, Indati2m = h.Indati2m,
                Inty = h.Inty, Inno = h.Inno, Irtaxid = h.Irtaxid, Inp = h.Inp, Ins = h.Ins,
                Tins = h.Tins, Tob = h.Tob, Bid = h.Bid, Tinb = h.Tinb, Bpc = h.Bpc, Bbc = h.Bbc,
                Setm = h.Setm, Cap = h.Cap, Tbill = h.Tbill, Tvam = h.Tvam,
                Tprdis = h.Tprdis, Tdis = h.Tdis, Tadis = h.Tadis,
            },
            Body = NewBody().Select(b => new InvoiceBodyDto
            {
                Sstid = b.Sstid, Sstt = b.Sstt, Mu = b.Mu, Am = b.Am, Fee = b.Fee,
                Prdis = b.Prdis, Dis = b.Dis, Adis = b.Adis, Vra = b.Vra, Vam = b.Vam,
                Tsstam = b.Tsstam,
            }).ToList(),
            Payments = new List<PaymentDto>(),
            Extension = new List<InvoiceExtension>(),
        }).ToList();

        var resp = TaxApiService.Instance.TaxApis.SendInvoices(dtos, null);
        return resp?.Body?.Result?.Select(x => x.ReferenceNumber).ToList() ?? new List<string>();
    }

    private static (bool ok, string status, List<string> codes)? Lookup(string reference)
    {
        if (string.IsNullOrEmpty(reference)) return null;
        var json = Control.GetStringAsync(BaseUrl.TrimEnd('/') + "/__state").GetAwaiter().GetResult();
        using var doc = JsonDocument.Parse(json);

        foreach (var bucket in new[] { "invoices", "rejected" })
        {
            if (!doc.RootElement.TryGetProperty(bucket, out var obj)) continue;
            foreach (var p in obj.EnumerateObject())
            {
                if (p.Value.GetProperty("reference").GetString() != reference) continue;
                var codes = new List<string>();
                foreach (var e in p.Value.GetProperty("errors").EnumerateArray())
                    codes.Add(e.GetProperty("code").GetString()!);
                foreach (var w in p.Value.GetProperty("warnings").EnumerateArray())
                    codes.Add(w.GetProperty("code").GetString()!);
                return (p.Value.GetProperty("success").GetBoolean(),
                        p.Value.GetProperty("status").GetString()!,
                        codes);
            }
        }
        return null;
    }

    private static string Fa(int n) =>
        string.Concat(n.ToString().Select(c => (char)('۰' + (c - '0'))));

    private static string Join((bool ok, string status, List<string> codes)? r) =>
        r is null ? "(بدون پاسخ)" : string.Join(",", r.Value.codes);

    private static void SetStatus(string taxid, string status) =>
        Control.PostAsJsonAsync(BaseUrl.TrimEnd('/') + "/__set_status", new { taxid, status })
               .GetAwaiter().GetResult();

    private static void Reset() =>
        Control.PostAsJsonAsync(BaseUrl.TrimEnd('/') + "/__reset", new { })
               .GetAwaiter().GetResult();

    private static bool ServerAlive()
    {
        try
        {
            Control.Timeout = TimeSpan.FromSeconds(3);
            return Control.GetAsync(BaseUrl.TrimEnd('/') + "/__ping")
                          .GetAwaiter().GetResult().IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private static void Banner(string title)
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 70));
        Console.WriteLine("  " + title);
        Console.WriteLine(new string('=', 70));
    }

    private static void Check(string name, bool ok, string? detail = "")
    {
        if (ok) { _pass++; Console.WriteLine("  [OK]   " + name); }
        else
        {
            _fail++;
            Failures.Add(name + (string.IsNullOrEmpty(detail) ? "" : "  ← " + detail));
            Console.WriteLine("  [FAIL] " + name +
                              (string.IsNullOrEmpty(detail) ? "" : "  ← " + detail));
        }
    }

    private static int Summary()
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 70));
        Console.WriteLine($"  موفق: {_pass}    ناموفق: {_fail}");
        if (Failures.Count > 0)
        {
            Console.WriteLine();
            foreach (var f in Failures) Console.WriteLine("   ✗ " + f);
        }
        Console.WriteLine(new string('=', 70));
        return _fail == 0 ? 0 : 1;
    }
}
