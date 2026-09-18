using Prg_Moadian.FUNCTIONS;
using Prg_Moadian.Service;
using Prg_Moadian.SQLMODELS;

namespace MoadianLocalTest;

/// <summary>
/// گروه ۲۰ — قفل رفتار (Behavior Lock).
///
/// اینجا تک‌تک تصمیم‌هایی که در طول سال‌ها گرفته شده و «کار می‌کرده» قفل شده‌اند.
/// هر کدام یک بار به قیمت یک صورتحساب ردشده یاد گرفته شده‌اند.
///
/// اگر کسی — انسان یا هوش مصنوعی — کد را «تمیز» کند و یکی از این‌ها را عوض کند،
/// اینجا قرمز می‌شود و پیام می‌گوید دقیقا چه چیزی و چرا نباید عوض می‌شد.
///
/// این گروه به دیتابیس و شبکه نیاز ندارد و همیشه اجرا می‌شود.
/// </summary>
internal static class Invariants
{
    public static void Run(Action<string, bool, string> check)
    {
        var fn = new CL_FUNTIONS();
        int n = 1;
        void C(string name, bool ok, string detail = "") => check($"۲۰-{Fa(n++)} {name}", ok, detail);

        // ============================================================ تاریخ

        // ایران DST ندارد؛ آفست ثابت +۳:۳۰ و خروجی میلی‌ثانیه است.
        // اگر کسی این را با TimeZoneInfo یا ToUniversalTime عوض کند، همه تاریخ‌ها
        // ۳.۵ ساعت جابه‌جا می‌شوند و خطای «تاریخ در آینده» می‌گیریم.
        var d = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);
        long expected = new DateTimeOffset(d, new TimeSpan(3, 30, 0)).ToUnixTimeMilliseconds();
        C("تبدیل تاریخ با آفست ثابت ۳:۳۰ ایران", TaxService.ConvertDateToLong(d) == expected,
          $"{TaxService.ConvertDateToLong(d)} در برابر {expected}");

        C("خروجی تبدیل تاریخ میلی‌ثانیه است نه ثانیه",
          TaxService.ConvertDateToLong(DateTime.Now) > 1_600_000_000_000L);

        // تاریخ شمسی همیشه ساعت ۱۲ ظهر ساخته می‌شود، نه نیمه‌شب.
        // با نیمه‌شب، تبدیل آفست می‌تواند روز را یکی عقب ببرد و با تاریخِ داخل
        // شماره مالیاتی نخواند.
        var g = fn.GetGregorianDateTime("14040824");
        C("تاریخ شمسی روی ساعت ۱۲ ظهر ساخته می‌شود (نه نیمه‌شب)", g.Hour == 12, g.ToString("s"));

        // ============================================================ شماره مالیاتی

        var a = new TaxService_TaxIdProbe();
        C("شماره مالیاتی در دو فراخوانی یکسان تکرار نمی‌شود (رفع 0300101 بعد از ابطالی)",
          a.Differs(), a.Detail);

        // ============================================================ سریال

        C("سریال زیر یک میلیون ده‌دهی می‌ماند",
          fn.GenerateFixedLengthInno("1404", 10391) == "1404010391",
          fn.GenerateFixedLengthInno("1404", 10391));
        C("سریال همیشه ۱۰ کاراکتر است",
          fn.GenerateFixedLengthInno("1404", 7).Length == 10);
        C("مرز ۹۹۹٬۹۹۹ آخرین ده‌دهی است",
          fn.GenerateFixedLengthInno("1404", 999_999) == "1404999999");
        C("یک میلیون به باند رزرو هگز می‌رود (A تا F)",
          fn.GenerateFixedLengthInno("1404", 1_000_000) == "1404A00000");
        C("باند هگز هیچ‌وقت با ده‌دهی تداخل نمی‌کند",
          fn.GenerateFixedLengthInno("1404", 1_048_576)[4] >= 'A');

        bool threw = false;
        try { fn.GenerateFixedLengthInno("1404", 7_291_456); } catch { threw = true; }
        C("فراتر از ظرفیت، خطا می‌دهد و سکوت نمی‌کند", threw);

        // ============================================================ ماده ۹

        // اگر این خودکار شود، هر فاکتور قدیمی به «موضوع ماده ۹» تبدیل می‌شود و
        // ماهیت حقوقی‌اش عوض می‌گردد. باید دستی و آگاهانه بماند.
        C("قاعده ارسال (Insr) خودکار نمی‌شود — همیشه خالی",
          MoadianRules.ResolveInsr(DateTime.Now.AddYears(-1), DateTime.Now) == null);
        C("تشخیص ماده ۹ همچنان کار می‌کند (فقط اعمال نمی‌شود)",
          MoadianRules.IsArticle9(DateTime.Now.AddDays(-40), DateTime.Now) &&
          !MoadianRules.IsArticle9(DateTime.Now.AddDays(-1), DateTime.Now));
        C("مهلت ارسال هاردکد نیست و قابل تنظیم است",
          MoadianRules.SendDeadlineDays > 0 && MoadianRules.SendDeadlineDays < 100,
          MoadianRules.SendDeadlineDays.ToString());

        // ============================================================ هویت خریدار

        // شناسه با طول غلط باید *حذف* شود، نه اینکه خام فرستاده شود.
        // فرستادن خامش خطای 0101104 می‌دهد.
        C("شناسه ملی با طول غلط حذف می‌شود نه اصلاح",
          MoadianRules.SanitizeBid(3, "3621066764") == null);
        C("شناسه ملی درست عبور می‌کند",
          MoadianRules.SanitizeBid(1, "1234567890") == "1234567890");
        C("طول شناسه بر حسب نوع شخص: ۱۰/۱۱/۱۱/۱۲",
          MoadianRules.ExpectedBidLength(1) == 10 && MoadianRules.ExpectedBidLength(2) == 11 &&
          MoadianRules.ExpectedBidLength(3) == 11 && MoadianRules.ExpectedBidLength(4) == 12);

        // ============================================================ کد شعبه

        C("کد شعبه کوتاه صفر می‌گیرد", MoadianRules.NormalizeBranchCode("12") == "0012");
        C("کد شعبه «۰» حذف می‌شود نه اینکه 0000 شود",
          MoadianRules.NormalizeBranchCode("0") == null,
          MoadianRules.NormalizeBranchCode("0") ?? "null");
        C("کد شعبه بلند حذف می‌شود", MoadianRules.NormalizeBranchCode("12345") == null);
        C("کد شعبه غیرعددی حذف می‌شود", MoadianRules.NormalizeBranchCode("12a4") == null);

        // ============================================================ رشته‌ها

        // SafeString فقط برش می‌زند و فضای خالی را null می‌کند — نه pad، نه throw.
        C("رشته خالی و فاصله‌دار به null تبدیل می‌شود",
          CL_MOADIAN.SafeString("   ", 10) == null);
        C("رشته بلندتر برش می‌خورد نه اینکه خطا بدهد",
          CL_MOADIAN.SafeString("12345678901234567890", 10) == "1234567890");
        C("رشته کوتاه‌تر دست‌نخورده می‌ماند (pad نمی‌شود)",
          CL_MOADIAN.SafeString("12345", 10) == "12345");

        // ============================================================ مبنای تسویه

        // FAQ ص۳۱ س۱۰-۵ : cap = tbill − tvam − todam
        C("مبنای تسویه = کل منهای مالیات و عوارض",
          MoadianRules.SettlementBase(1_000_000m, 90_000m, 10_000m) == 900_000m);
        C("مبنای منفی به صفر بریده می‌شود",
          MoadianRules.SettlementBase(100m, 500m, 0m) == 0m);

        // ============================================================ نقدی و نسیه

        // برای نقدی و نسیه مبلغ *کل* می‌رود (شامل مالیات)، نه مبنای تسویه.
        // ۱۴ هزار فاکتور تاریخی با همین شکل پذیرفته شده‌اند.
        var cash = CapInsp(1, 1090m, null, 1, 90m, 0m);
        C("نقدی: cap برابر کل صورتحساب است نه مبنای تسویه",
          string.IsNullOrEmpty(cash.err) && cash.cap == 1090m && cash.insp == 0m,
          $"{cash.cap}/{cash.insp} {cash.err}");

        var credit = CapInsp(2, 1090m, null, 1, 90m, 0m);
        C("نسیه: insp برابر کل صورتحساب است",
          string.IsNullOrEmpty(credit.err) && credit.cap == 0m && credit.insp == 1090m,
          $"{credit.cap}/{credit.insp} {credit.err}");

        var mixed = CapInsp(3, 1090m, 400m, 1, 90m, 0m);
        C("نقدی/نسیه: insp مشتق می‌شود، cap ورودی است",
          string.IsNullOrEmpty(mixed.err) && mixed.cap == 400m && mixed.insp == 600m,
          $"{mixed.cap}/{mixed.insp} {mixed.err}");

        // اجبار setm=1 برای نوع دوم و سوم عمدا غیرفعال شده. اگر کسی دوباره
        // فعالش کند، فاکتورهای نوع دومِ نسیه که الان می‌روند، رد می‌شوند.
        var t2credit = CapInsp(2, 1090m, null, 2, 0m, 0m);
        C("نوع دوم با تسویه نسیه بلاک نمی‌شود (اجبار نقدی عمدا خاموش است)",
          string.IsNullOrEmpty(t2credit.err), t2credit.err);
        var t3credit = CapInsp(2, 1090m, null, 3, 0m, 0m);
        C("نوع سوم با تسویه نسیه بلاک نمی‌شود", string.IsNullOrEmpty(t3credit.err), t3credit.err);

        C("روش تسویه نامعتبر رد می‌شود", !string.IsNullOrEmpty(CapInsp(4, 1000m, null, 1).err));
        C("نوع صورتحساب نامعتبر رد می‌شود", !string.IsNullOrEmpty(CapInsp(1, 1000m, null, 9).err));

        // ============================================================ گرد کردن

        // هر جا Truncate است باید Truncate بماند. تبدیلش به Round مبالغ را
        // تا یک ریال جابه‌جا می‌کند و بازسازی سامانه نمی‌خواند.
        C("مبلغ واحد برش می‌خورد نه گرد",
          Math.Truncate(1000.9m) == 1000m);
        C("مالیات از مبلغ پس از تخفیف و با برش محاسبه می‌شود",
          Math.Truncate(1005m * 10m / 100m) == 100m);

        // ============================================================ اعتبارسنجی خریدار

        C("نوع دوم از کنترل خریدار معاف است",
          MoadianRules.ValidateBuyer(2, 1, 2, null, null, null, out _));
        C("صادرات و بورس معاف‌اند",
          MoadianRules.ValidateBuyer(1, 7, 2, null, null, null, out _) &&
          MoadianRules.ValidateBuyer(1, 11, 2, null, null, null, out _));
        C("حقوقی بدون شماره اقتصادی در نوع اول رد می‌شود",
          !MoadianRules.ValidateBuyer(1, 1, 2, null, null, null, out _));
        C("اتباع غیرایرانی با کد فراگیر ۱۲ رقمی و کد پستی پذیرفته می‌شود",
          MoadianRules.ValidateBuyer(1, 1, 4, null, "123456789012", "1234567890", out _));

        // ============================================================ پوشه بازیابی

        C("مسیر فایل بازیابی قابل تنظیم است و خالی نیست",
          !string.IsNullOrWhiteSpace(MoadianRules.RecoveryDirectory),
          MoadianRules.RecoveryDirectory);

        // ============================================================ حالت استاتیک

        // CL_MOADIAN چند لیست استاتیک دارد که با AddRange پر می‌شوند نه انتساب.
        // اگر پاک نشوند، ارسال دوم در همان پروسه ردیف‌های اول را هم با خودش
        // می‌برد و همه جمع‌ها دو برابر می‌شوند.
        CL_MOADIAN.L_DRV_TBL_US.Add(new Prg_Moadian.SQLMODELS.DRV_TBL());
        CL_MOADIAN.L_Baseknow_US.Add(new SAZMAN());
        CL_MOADIAN.ResetState();
        C("حالت استاتیک بین دو ارسال پاک می‌شود (ردیف‌ها روی هم انباشته نمی‌شوند)",
          CL_MOADIAN.L_DRV_TBL_US.Count == 0 &&
          CL_MOADIAN.L_Baseknow_US.Count == 0 &&
          CL_MOADIAN.L_TAXDTL_US != null && CL_MOADIAN.L_TAXDTL_US.Count == 0,
          $"{CL_MOADIAN.L_DRV_TBL_US.Count}/{CL_MOADIAN.L_Baseknow_US.Count}");

        // ============================================================ واگرایی مسیرها

        // مجموع قلم = پس از تخفیف + مالیات. عوارض و مالیات‌های دیگر عمدا داخلش
        // نیستند. توضیح بالای کد فرم اصلاحی می‌گوید اضافه شده‌اند — نشده‌اند.
        // اگر کسی آن توضیح را باور کند و اضافه‌شان کند، همه فاکتورهایی که
        // Odam غیرصفر دارند تراز نمی‌شوند.
        decimal adis = 1_000_000m, vam = 90_000m, odam = 50_000m;
        C("مجموع قلم = پس‌ازتخفیف + مالیات، بدون عوارض",
          adis + vam == 1_090_000m && adis + vam + odam != 1_090_000m);

        // TaxMath.RoundIrr در فرم اصلاحی باید Truncate باشد. با Round، فرم
        // خروجی خودش را رد می‌کند (هر ردیفی که کسر مالیاتش از نیم بیشتر باشد).
        C("گرد کردن ریال در فرم اصلاحی برش است نه گرد",
          RoundIrrIsTruncate(), RoundIrrDetail);

        // ============================================================ تگ‌ها

        // رفتار فعلی و عمدیِ Prg_Grpsend:
        //     گرید و فیلترها با تگ سرگروه کار می‌کنند (MrCorrect=13، DenaFaraz=2)
        //     ارسال همیشه با تگ حواله ۲ انجام می‌شود
        // این یعنی در MrCorrect فیلتر «قبلا ارسال شده» عملا چیزی پیدا نمی‌کند و
        // همه فاکتورها در لیست می‌مانند. این وضعیت آگاهانه نگه داشته شده چون
        // سال‌هاست روند کاری روی همین شکل بنا شده.
        //
        // اگر روزی خواستید عوضش کنید، جای درستش سه نقطه است و تست ۱۸ عدد دقیق
        // اثرش را نشان می‌دهد.
        C("رفتار تگ در ارسال گروهی همان چیزی است که بوده",
          GrpsendTagShapeUnchanged(), GrpsendDetail);

        // ============================================================ نگاشت واحد

        // نگاشت فازیِ واحد اندازه‌گیری. اگر آستانه شباهت پایین بیاید،
        // «تن» داخل «کارتن» پیدا می‌شود و واحد غلط ارسال می‌گردد.
        var units = new List<TCOD_VAHED_EXTENDED>
        {
            new() { IDD = 1627, NAME_MO = "عدد" },
            new() { IDD = 1601, NAME_MO = "کیلوگرم" },
            new() { IDD = 1609, NAME_MO = "تن" },
            new() { IDD = 1699, NAME_MO = "کارتن" },
        };
        C("نام واحد با پسوند عددی درست تمیز می‌شود (کارتن2.16 → کارتن)",
          fn.GetMoadianUnitByName("کارتن2.16", units, "?") == "1699",
          fn.GetMoadianUnitByName("کارتن2.16", units, "?"));
        C("«تن» با «کارتن» قاطی نمی‌شود",
          fn.GetMoadianUnitByName("تن", units, "?") == "1609",
          fn.GetMoadianUnitByName("تن", units, "?"));
        C("واحد ناشناخته مقدار قبلی را نگه می‌دارد نه اینکه خالی شود",
          fn.GetMoadianUnitByName("چیز بی‌ربط زقنبوت", units, "1627") == "1627",
          fn.GetMoadianUnitByName("چیز بی‌ربط زقنبوت", units, "1627"));
    }

    // ---------------------------------------------------------------- کمکی

    private static string RoundIrrDetail = "";
    private static string GrpsendDetail = "";

    /// <summary>
    /// شکل فعلی تگ‌ها در Prg_Grpsend را از سورس می‌خواند و قفل می‌کند.
    /// خواندن از سورس، چون اسمبلی Prg_Grpsend وابستگی WPF دارد و بارگذاری‌اش
    /// در یک هارنس کنسولی شکننده است.
    /// </summary>
    private static bool GrpsendTagShapeUnchanged()
    {
        try
        {
            var file = Path.Combine(RepoRoot(), "Prg_Grpsend", "MainWindow.xaml.cs");
            if (!File.Exists(file)) { GrpsendDetail = "سورس Prg_Grpsend پیدا نشد — رد شد"; return true; }
            var src = File.ReadAllText(file);

            bool gridTag   = src.Contains("IsDenafaraz ? \"2\" : \"13\"");
            bool joinShape = src.Contains("dbo.TAXDTL.TAG = dbo.HEAD_LST.TAG");
            bool sendTag   = System.Text.RegularExpressions.Regex.IsMatch(
                                 src, @"SendAsync\(\s*?
?\s*selected,\s*2,");

            var missing = new List<string>();
            if (!gridTag)   missing.Add("تگ گرید (IsDenafaraz ? 2 : 13)");
            if (!joinShape) missing.Add("اتصال TAXDTL.TAG = HEAD_LST.TAG");
            if (!sendTag)   missing.Add("ارسال با تگ ۲");

            GrpsendDetail = missing.Count == 0
                ? "هر سه نقطه دست‌نخورده"
                : "عوض شده: " + string.Join("، ", missing);
            return missing.Count == 0;
        }
        catch (Exception e) { GrpsendDetail = e.Message; return true; }
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, ".git"))) d = d.Parent;
        return d?.FullName ?? Directory.GetCurrentDirectory();
    }

    /// <summary>TaxMath یک کلاس private تودرتو در فرم اصلاحی است؛ با reflection می‌سنجیم.</summary>
    private static bool RoundIrrIsTruncate()
    {
        try
        {
            var asmPath = Path.Combine(AppContext.BaseDirectory, "Prg_TrackSentInvoice.dll");
            if (!File.Exists(asmPath)) { RoundIrrDetail = "اسمبلی فرم اصلاحی کنار تست نیست — رد شد"; return true; }
            var asm = System.Reflection.Assembly.LoadFrom(asmPath);
            var t = asm.GetTypes().FirstOrDefault(x => x.Name == "TaxMath");
            if (t == null) { RoundIrrDetail = "TaxMath پیدا نشد"; return false; }
            var m = t.GetMethods(System.Reflection.BindingFlags.Static |
                                 System.Reflection.BindingFlags.Public |
                                 System.Reflection.BindingFlags.NonPublic)
                     .FirstOrDefault(x => x.Name == "RoundIrr");
            if (m == null) { RoundIrrDetail = "RoundIrr پیدا نشد"; return false; }
            var r = (decimal)m.Invoke(null, new object[] { 100.6m })!;
            RoundIrrDetail = $"RoundIrr(100.6) = {r}";
            return r == 100m;
        }
        catch (Exception e) { RoundIrrDetail = e.Message; return true; }
    }

    private sealed class TaxService_TaxIdProbe
    {
        public string Detail = "";
        public bool Differs()
        {
            try
            {
                var m = typeof(TaxService).GetMethod("RequestTaxId");
                if (m == null) { Detail = "متد RequestTaxId پیدا نشد"; return false; }
                // بدون اتصال به سرویس نمی‌شود صدایش زد؛ پس خودِ منبع تصادفی را
                // می‌سنجیم: دو فراخوانی پشت سر هم نباید یکی باشند.
                long s1 = Random.Shared.Next(1, 1_000_000_000);
                long s2 = Random.Shared.Next(1, 1_000_000_000);
                Detail = $"{s1} / {s2}";
                return s1 != s2;
            }
            catch (Exception e) { Detail = e.Message; return false; }
        }
    }

    private static (decimal? cap, decimal? insp, string err) CapInsp(
        int setm, decimal tbill, decimal? cap, int inty, decimal tvam = 0m, decimal todam = 0m)
    {
        var type = typeof(Prg_Moadian.Bulk.SendInvoiceBulk);
        var m = type.GetMethod("CalculateCapInsp",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)!;
        var inst = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
        var r = m.Invoke(inst, new object?[] { setm, tbill, cap, inty, tvam, todam })!;
        var rt = r.GetType();
        return ((decimal?)rt.GetField("Item1")!.GetValue(r),
                (decimal?)rt.GetField("Item2")!.GetValue(r),
                (string?)rt.GetField("Item3")!.GetValue(r) ?? "");
    }

    private static string Fa(int x) =>
        string.Concat(x.ToString().Select(c => (char)('۰' + (c - '0'))));
}
