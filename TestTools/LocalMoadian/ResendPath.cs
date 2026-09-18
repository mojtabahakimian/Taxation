using System.Reflection;
using System.Text.RegularExpressions;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.FUNCTIONS;
using Prg_Moadian.SQLMODELS;

namespace MoadianLocalTest;

/// <summary>
/// گروه ۲۴ — دکمه «ارسال مجدد فاکتور» در Prg_TrackSentInvoice.
///
/// این دکمه یک نسخهٔ *عیناً تکراری* از یک صورتحساب را دوباره به سامانه
/// می‌فرستد. علتش این است که سامانه گاهی صورتحساب را روزها روی PENDING
/// نگه می‌دارد؛ ارسال مجدد یا رد می‌شود (چون تکراری است) یا پذیرفته می‌شود
/// (چون بار اول اصلا نرسیده بود). در هر دو حالت تکلیف کاربر روشن می‌شود.
///
/// یک نقص واقعی داشت: ردیف تازه‌ای که در TAXDTL درج می‌شد ستون‌های NUMBER و
/// TAG را نداشت. هر دو ستون nullable هستند، پس insert بی‌سروصدا موفق می‌شد و
/// در تست دستی چیزی دیده نمی‌شد — ولی گرید پیگیری با NUMBER به HEAD_LST وصل
/// می‌شود و جستجوی کاربر هم روی NUMBER است، پس ردیف ارسال مجدد بدون شماره
/// فاکتور و بدون نام مشتری ظاهر می‌شد و پیدا نمی‌شد.
///
/// اینجا هم شکل دستور insert از سورس قفل می‌شود، هم واقعا یک ردیف با همان
/// دستورِ تولید در دیتابیس درج و دوباره خوانده می‌شود تا ثابت شود این دو
/// ستون سالم منتقل می‌شوند.
/// </summary>
internal static class ResendPath
{
    // شماره و تگ نشانه‌دار. تگ ۷۷ عمدا انتخاب شده چون اثر انگشتِ داده واقعی
    // (در run-tests.ps1) تگ‌های ۷۷ و ۸۸ را کنار می‌گذارد، پس این ردیف موقت
    // نمی‌تواند کنترلِ «داده واقعی دست نخورد» را به‌هم بریزد.
    private const double MarkerNumber = 9_999_777;
    private const double MarkerTag = 77;

    public static void Run(Action<string, bool, string> check, Action<string> line, bool withDb)
    {
        int n = 1;
        void C(string name, bool ok, string detail = "") => check($"۲۴-{Fa(n++)} {name}", ok, detail);

        // ---------------- مدل: دو ستون باید روی FULL_TAXDTL باشند ----------------

        var t = typeof(FULL_TAXDTL);
        var pNumber = t.GetProperty("NUMBER", BindingFlags.Public | BindingFlags.Instance);
        var pTag = t.GetProperty("TAG", BindingFlags.Public | BindingFlags.Instance);

        C("مدل FULL_TAXDTL ستون NUMBER را دارد (وگرنه شماره فاکتور منتقل نمی‌شود)",
          pNumber != null, pNumber?.PropertyType.ToString() ?? "ندارد");
        C("مدل FULL_TAXDTL ستون TAG را دارد",
          pTag != null, pTag?.PropertyType.ToString() ?? "ندارد");

        // ---------------- دستور insert: از سورس خوانده و قفل می‌شود ----------------

        var src = ResendSource();
        if (src == null)
        {
            C("سورس Prg_TrackSentInvoice پیدا شد", false, "فایل MainWindow.xaml.cs نبود");
            return;
        }

        var m = Regex.Match(src, @"INSERT INTO dbo\.TAXDTL \((.*?)\)\s*VALUES \((.*?)\);", RegexOptions.Singleline);
        if (!m.Success)
        {
            C("دستور درج ردیف ارسال مجدد در سورس پیدا شد", false, "الگوی INSERT پیدا نشد");
            return;
        }

        var cols = m.Groups[1].Value.Split(',').Select(x => x.Trim()).ToList();
        var vals = m.Groups[2].Value.Split(',').Select(x => x.Trim()).ToList();

        C("تعداد ستون و تعداد مقدار در دستور درج برابر است",
          cols.Count == vals.Count, $"{cols.Count} ستون در برابر {vals.Count} مقدار");

        var mismatched = cols.Zip(vals, (c, v) => (c, v)).Where(x => x.v != "@" + x.c).ToList();
        C("هر ستون دقیقا به پارامتر هم‌نام خودش وصل است (جابه‌جایی مقدار رخ نداده)",
          mismatched.Count == 0,
          mismatched.Count == 0 ? $"{cols.Count} ستون" :
              string.Join("، ", mismatched.Take(3).Select(x => $"{x.c} ← {x.v}")));

        C("ستون NUMBER در دستور درج هست",
          cols.Contains("NUMBER") && vals.Contains("@NUMBER"),
          cols.Contains("NUMBER") ? "هست" : "نیست — ردیف ارسال مجدد بدون شماره فاکتور درج می‌شود");
        C("ستون TAG در دستور درج هست",
          cols.Contains("TAG") && vals.Contains("@TAG"),
          cols.Contains("TAG") ? "هست" : "نیست");

        // هر پارامتر باید در شیء پارامترها هم مقداردهی شده باشد، وگرنه Dapper
        // در زمان اجرا خطا می‌دهد — یعنی دکمه سر کاربر خراب می‌شود نه اینجا.
        var objStart = src.IndexOf("var p = new", m.Index, StringComparison.Ordinal);
        var supplied = new HashSet<string>();
        if (objStart > 0)
        {
            var objEnd = src.IndexOf("};", objStart, StringComparison.Ordinal);
            var body = src.Substring(objStart, Math.Max(0, objEnd - objStart));
            foreach (Match g in Regex.Matches(body, @"src_item\.(\w+)")) supplied.Add(g.Groups[1].Value);
        }
        var missing = cols.Where(c => !supplied.Contains(c)).ToList();
        C("همه پارامترهای دستور درج در شیء پارامترها مقدار دارند",
          objStart > 0 && missing.Count == 0,
          missing.Count == 0 ? $"{supplied.Count} مقدار" : "بدون مقدار: " + string.Join("، ", missing));

        // ---------------- رفتارهای عمدی که نباید عوض شوند ----------------

        C("فقط PENDING / IN_PROGRESS / FAILED قابل ارسال مجدد است (UNKNOWN عمدا بیرون است)",
          Regex.IsMatch(src, @"s\s*==\s*""PENDING""\s*\|\|\s*s\s*==\s*""IN_PROGRESS""\s*\|\|\s*s\s*==\s*""FAILED"""),
          "شرط فعال‌شدن دکمه");

        C("پیش از ارسال مجدد از کاربر تایید گرفته می‌شود",
          Regex.IsMatch(src, @"RESEND_BTN_Click.*?msgwin\.DialogResult\s*!=\s*true", RegexOptions.Singleline),
          "دیالوگ تایید");

        C("نسخهٔ پایه انتخاب می‌شود، نه یک ارسال مجددِ قبلی",
          src.Contains("NOT LIKE 'ResendDuplicate%'"),
          "فیلتر REMARKS روی UID پایه");

        C("ردیف تازه با REMARKS نشانه‌دار ثبت می‌شود (قابل ردیابی می‌ماند)",
          src.Contains("$\"ResendDuplicate|{windowsUser}"),
          "برچسب REMARKS");

        C("شماره مالیاتی و تاریخ صدور از ردیف اصلی می‌آیند، نه تاریخ امروز",
          src.Contains("h.Taxid = row.Taxid") &&
          src.Contains("h.Indatim = (long)row.Indatim_Sec") &&
          src.Contains("h.Indati2m = (long)row.Indati2m_Sec"),
          "معنای «عینا تکراری»");

        // مهم‌ترین قفل این گروه.
        //
        // در گروه پرسش‌وپاسخ سامانه، همین سناریو بارها گزارش شده و نتیجه‌اش
        // روشن است:
        //
        //   «وقتی مجددا با همون TaxID می‌فرستین یا خطا میده میگه تکراریه که
        //    نشسته در کارپوشه، و یا اینکه خطا نمیده و Success میده.»
        //
        //   «اگه با شماره مالیاتی قبلی ارسال شده باشه خطای شماره مالیاتی
        //    معتبر نیست رو میده. *اگه با شماره مالیاتی جدید بفرستی دوبار ثبت
        //    میشه و باید یکی رو ابطال کنی.*»
        //
        // یعنی شماره مالیاتیِ یکسان همان چیزی است که این دکمه را بی‌خطر می‌کند:
        // بدترین حالتش «تکراری» است. اگر روزی کسی اینجا شماره مالیاتی تازه
        // بسازد، همان فاکتور دو بار در کارپوشه می‌نشیند و باید یکی‌اش ابطال شود.
        var block = ResendBlock(src);
        C("مسیر ارسال مجدد شماره مالیاتی تازه نمی‌سازد (وگرنه فاکتور دوبار ثبت می‌شود)",
          block != null && !block.Contains("RequestTaxId") &&
                           !block.Contains("GenerateFixedLengthInno"),
          block == null
              ? "مرز بلوک ارسال مجدد پیدا نشد — احتمالا RESEND_BTN_Click یا " +
                "InsertNewTaxDtlRecord تغییر نام داده؛ این شکستِ تست است نه شکستِ کد"
              : "نه RequestTaxId نه تولید سریال تازه");

        // ---------------- رفت‌وبرگشت واقعی با دیتابیس ----------------

        if (!withDb)
        {
            line("      · رفت‌وبرگشت دیتابیس رد شد (بدون ‎-Full‎)");
            return;
        }

        var db = new CL_CCNNMANAGER();
        int? newIdd = null;
        try
        {
            var source = db.DoGetDataSQL<FULL_TAXDTL>(
                "SELECT TOP (1) * FROM dbo.TAXDTL WHERE NUMBER IS NOT NULL AND TAG IS NOT NULL ORDER BY IDD DESC")
                .FirstOrDefault();

            if (source == null)
            {
                line("      · ردیف نمونه‌ای در TAXDTL نبود — رفت‌وبرگشت رد شد");
                return;
            }

            // سمت خواندن: همان SELECT * که خودِ دکمه می‌زند باید این دو را پر کند.
            C("خواندن ردیف موجود، NUMBER و TAG را پر می‌کند",
              source.NUMBER != null && source.TAG != null,
              $"NUMBER={source.NUMBER?.ToString() ?? "خالی"}، TAG={source.TAG?.ToString() ?? "خالی"}");

            var clone = (FULL_TAXDTL)source.Clone();
            newIdd = db.DoGetDataSQL<int>("SELECT ISNULL(MAX(IDD),0) + 1 FROM dbo.TAXDTL").FirstOrDefault();
            clone.IDD = newIdd.Value;
            clone.NUMBER = MarkerNumber;
            clone.TAG = MarkerTag;
            clone.REMARKS = "ResendDuplicate|TEST24";
            clone.TheStatus = "PENDING";
            clone.CRT = DateTime.Now;

            // دقیقا همان دستوری که کد تولید اجرا می‌کند، از روی سورس.
            var insertSql = "INSERT INTO dbo.TAXDTL (" + m.Groups[1].Value + ") VALUES (" + m.Groups[2].Value + ");";
            db.DoExecuteSQL(insertSql, ToParams(clone, cols));

            var back = db.DoGetDataSQL<FULL_TAXDTL>(
                "SELECT * FROM dbo.TAXDTL WHERE IDD = @idd", new { idd = clone.IDD }).FirstOrDefault();

            C("ردیف ارسال مجدد واقعا درج شد", back != null, $"IDD={clone.IDD}");
            C("شماره فاکتور در ردیف ارسال مجدد باقی می‌ماند (گرید با همین به HEAD_LST وصل می‌شود)",
              back?.NUMBER == MarkerNumber, back?.NUMBER?.ToString() ?? "خالی");
            C("تگ در ردیف ارسال مجدد باقی می‌ماند",
              back?.TAG == MarkerTag, back?.TAG?.ToString() ?? "خالی");
            C("شماره مالیاتی و سریال عینا منتقل شدند",
              back?.Taxid == source.Taxid && back?.Inno == source.Inno,
              $"{back?.Taxid} / {back?.Inno}");
            C("مبلغ کل صورتحساب دست نخورد",
              back?.Tbill == source.Tbill, $"{back?.Tbill} در برابر {source.Tbill}");
        }
        catch (Exception ex)
        {
            C("رفت‌وبرگشت درج ردیف ارسال مجدد", false, ex.Message);
        }
        finally
        {
            if (newIdd != null)
            {
                try { db.DoExecuteSQL($"DELETE FROM dbo.TAXDTL WHERE IDD = {newIdd.Value} AND TAG = {MarkerTag}"); }
                catch (Exception ex) { line("      · هشدار: پاک کردن ردیف تست ناموفق بود — " + ex.Message); }
            }
        }
    }

    // ---------------------------------------------------------------- کمکی

    /// <summary>
    /// شیء پارامتر Dapper را از روی همان فهرست ستون‌های سورس می‌سازد، تا اگر
    /// فردا ستونی به دستور اضافه شد این تست هم خودبه‌خود آن را پوشش بدهد.
    /// </summary>
    private static Dictionary<string, object> ToParams(FULL_TAXDTL row, IEnumerable<string> cols)
    {
        var d = new Dictionary<string, object>();
        foreach (var c in cols)
        {
            var p = typeof(FULL_TAXDTL).GetProperty(c, BindingFlags.Public | BindingFlags.Instance |
                                                       BindingFlags.IgnoreCase);
            d[c] = p?.GetValue(row);
        }
        return d;
    }

    /// <summary>
    /// فقط بدنهٔ متد ارسال مجدد، تا شرط بالا به بقیهٔ فایل سرایت نکند.
    ///
    /// اگر مرزها پیدا نشوند null برمی‌گرداند، نه کل فایل. برگرداندن کل فایل
    /// باعث می‌شد تست با پیامی شکست بخورد که علت واقعی (تغییر نام متد) را
    /// پنهان می‌کرد.
    /// </summary>
    private static string ResendBlock(string src)
    {
        var a = src.IndexOf("RESEND_BTN_Click", StringComparison.Ordinal);
        var b = src.IndexOf("InsertNewTaxDtlRecord(FULL_TAXDTL", StringComparison.Ordinal);
        return (a >= 0 && b > a) ? src.Substring(a, b - a) : null;
    }

    private static string ResendSource()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, ".git"))) d = d.Parent;
        var root = d?.FullName ?? Directory.GetCurrentDirectory();
        var file = Path.Combine(root, "Prg_TrackSentInvoice", "MainWindow.xaml.cs");
        return File.Exists(file) ? File.ReadAllText(file) : null;
    }

    private static string Fa(int x) =>
        string.Concat(x.ToString().Select(c => (char)('۰' + (c - '0'))));
}
