using Prg_Moadian.CNNMANAGER;

namespace MoadianLocalTest;

/// <summary>
/// گروه ۲۵ — یافته‌های پژوهش سند، راستی‌آزمایی‌شده با دادهٔ واقعی.
///
/// سه گزارش پژوهشی مستقل دربارهٔ چند قاعدهٔ مهم با هم تناقض داشتند. به‌جای
/// پذیرفتن هیچ‌کدام، هر ادعا با فاکتورهای واقعیِ *پذیرفته‌شده* سنجیده شد.
/// این گروه نتیجه را قفل می‌کند:
///
///   • جایی که دادهٔ واقعی حکم داد  → به‌عنوان رفتار درست قفل می‌شود.
///   • جایی که دادهٔ واقعی ساکت بود → به‌عنوان «هرگز استفاده نشده» ثبت می‌شود،
///     نه «درست». اگر روزی استفاده شود، تست می‌گوید اول باید تصمیم گرفت.
///
/// هیچ فرمولی اینجا عوض نشده؛ این گروه فقط دیده‌بان است.
/// </summary>
internal static class SpecFindings
{
    public static void Run(Action<string, bool, string> check, Action<string> line, bool withDb)
    {
        int n = 1;
        void C(string name, bool ok, string detail = "") => check($"۲۵-{Fa(n++)} {name}", ok, detail);

        if (!withDb)
        {
            line("      · این گروه به دیتابیس واقعی نیاز دارد (با ‎-Full‎ اجرا شود)");
            return;
        }

        var db = new CL_CCNNMANAGER();

        // ============================================================ عوارض
        //
        // سند (ص۷۳ جدول ۵۳) فرمول مبلغ کل قلم را چنین می‌دهد:
        //     Os = <مبلغ قلم> + Ks + Ks2 + Ks3
        // که Ks2 = odam (سایر مالیات و عوارض) و Ks3 = olam (سایر وجوه قانونی).
        // هر سه مسیر این برنامه Tsstam = Adis + Vam حساب می‌کنند، یعنی odam و
        // olam را کنار می‌گذارند.
        //
        // این *امروز* اختلافی نمی‌سازد، چون در کل جدول هیچ مقدار غیرصفری برای
        // odam/olam/todam وجود ندارد و برنامه هم آنها را همیشه صفر می‌فرستد.
        // ولی اگر روزی پر شوند، فرمول فعلی بی‌سروصدا کم‌تر گزارش می‌دهد و
        // سامانه صورتحساب را رد می‌کند.
        //
        // پس به‌جای عوض کردن فرمولِ کارکرده، اینجا دیده‌بان می‌گذاریم.

        long odamNz = Scalar(db, "SELECT COUNT(*) FROM dbo.TAXDTL WHERE ISNULL(Odam,0) <> 0");
        long olamNz = Scalar(db, "SELECT COUNT(*) FROM dbo.TAXDTL WHERE ISNULL(Olam,0) <> 0");
        long todamNz = Scalar(db, "SELECT COUNT(*) FROM dbo.TAXDTL WHERE ISNULL(Todam,0) <> 0");

        C("عوارض (odam) هنوز هیچ‌جا غیرصفر نیست — فرمول فعلی مبلغ قلم امن است",
          odamNz == 0,
          odamNz == 0 ? "صفر ردیف" :
              $"{odamNz} ردیف عوارض غیرصفر دارد — طبق ص۷۳ جدول ۵۳ باید Tsstam = Adis+Vam+Odam+Olam شود");
        C("سایر وجوه قانونی (olam) هنوز هیچ‌جا غیرصفر نیست",
          olamNz == 0,
          olamNz == 0 ? "صفر ردیف" : $"{olamNz} ردیف — همان تصمیم بالا لازم است");
        C("مجموع عوارض سرصفحه (todam) هنوز هیچ‌جا غیرصفر نیست — فرمول Tbill امن است",
          todamNz == 0,
          todamNz == 0 ? "صفر ردیف" :
              $"{todamNz} ردیف — طبق سند باید Tbill = Tadis+Tvam+Todam شود");

        // ============================================================ تسویه
        //
        // سه گزارش سه فرمول متفاوت دادند و همه‌شان ناقص بودند، چون سند *دو*
        // قاعدهٔ جدا دارد و هرکدام جای خودش:
        //
        //   • setm = ۳ (نقدی/نسیه) → ص۴۶/۴۷ :  cap + insp = tbill − tvam − todam
        //   • setm = ۱ و ۲        → سند قاعده‌ای ندارد؛ ص۴۵ جدول ۲۴ ردیف ۳ این
        //                            دو فیلد را فقط برای نقدی/نسیه اجباری می‌کند.
        //
        // برای setm ۱ و ۲ پس دادهٔ واقعی تنها داور است، و حکم می‌دهد
        // cap + insp = tbill.

        long vatOk = Scalar(db, @"
            SELECT COUNT(*) FROM (SELECT DISTINCT Taxid,Tbill,Tvam,Todam,Cap,Insp FROM dbo.TAXDTL
              WHERE TheStatus='SUCCESS' AND Cap IS NOT NULL AND Insp IS NOT NULL
                AND ISNULL(Tvam,0) > 0) X
            WHERE Cap + Insp = Tbill");
        long vatAll = Scalar(db, @"
            SELECT COUNT(*) FROM (SELECT DISTINCT Taxid,Tbill,Tvam,Todam,Cap,Insp FROM dbo.TAXDTL
              WHERE TheStatus='SUCCESS' AND Cap IS NOT NULL AND Insp IS NOT NULL
                AND ISNULL(Tvam,0) > 0) X");

        C("روی فاکتورهای مالیات‌دارِ پذیرفته‌شده، cap + insp = مجموع صورتحساب است",
          vatAll > 0 && vatOk * 100 >= vatAll * 90,
          $"{vatOk} از {vatAll} — یعنی مالیات داخل مبلغ تسویه می‌آید، نه بیرون آن");

        // روش تسویه نقدی/نسیه (setm=3) هرگز استفاده نشده است — ولی فرمولش
        // *درست* است. متن سند:
        //
        //   ص۴۶ جدول ۲۵ ردیف ۲ :  C  = Xs − W2 − W − Cr
        //   ص۴۷ جدول ۲۶ ردیف ۲ :  Cr = Xs − W2 − W − C
        //
        // یعنی برای نقدی/نسیه، مالیات و عوارض از مبنای تسویه *کم می‌شوند* —
        // دقیقا کاری که CalculateCapInsp می‌کند.
        //
        // و ص۴۵ جدول ۲۴ ردیف ۳ می‌گوید این دو فیلد «در صورتی که روش تسویه
        // نقدی/نسیه باشد اجباری است» — یعنی این قاعده به setm ۱ و ۲ کاری
        // ندارد. پس رفتار متفاوت آن دو (cap = کل صورتحساب) ناسازگاری نیست،
        // و ۱٬۰۰۶ فاکتور مالیات‌دارِ پذیرفته‌شده هم تأییدش می‌کنند.
        //
        // این تست فقط ثبت می‌کند که setm=3 هنوز شاهد عملیاتی ندارد.
        long setm3 = Scalar(db, "SELECT COUNT(DISTINCT Taxid) FROM dbo.TAXDTL WHERE Setm = 3");
        line($"      · روش تسویه نقدی/نسیه: {setm3} فاکتور — فرمولش با ص۴۶/۴۷ می‌خواند، " +
             "ولی هنوز در عمل آزموده نشده");

        // ============================================================ مهلت
        //
        // هر سه گزارش روی مهلت ۱۲ روزه هم‌نظرند. AutoArticle9 عمدا خاموش است
        // (CLAUDE.md، فهرست دست‌نزدنی‌ها) و نباید خودکار روشن شود.
        //
        // این تست فقط نگهبانی می‌کند: تا وقتی همه‌چیز داخل مهلت است، خاموش بودن
        // AutoArticle9 هیچ هزینه‌ای ندارد. روزی که فاکتوری از مهلت رد شود،
        // اینجا قرمز می‌شود و کاربر باید دربارهٔ insr تصمیم بگیرد.
        long late = Scalar(db, @"
            SELECT COUNT(*) FROM (SELECT DISTINCT Taxid, Indatim_Sec, Indati2m_Sec FROM dbo.TAXDTL
              WHERE TheStatus='SUCCESS' AND Indatim_Sec IS NOT NULL AND Indati2m_Sec IS NOT NULL) X
            WHERE (Indati2m_Sec - Indatim_Sec) / 86400000.0 > 12");

        C("هیچ فاکتور پذیرفته‌شده‌ای از مهلت ۱۲ روزه رد نشده — خاموش بودن insr بی‌هزینه است",
          late == 0,
          late == 0 ? "صفر فاکتور خارج از مهلت"
                    : $"{late} فاکتور بیش از ۱۲ روز فاصله دارد — دربارهٔ insr تصمیم لازم است");
    }

    // ---------------------------------------------------------------- کمکی

    private static long Scalar(CL_CCNNMANAGER db, string sql) =>
        db.DoGetDataSQL<long>(sql).FirstOrDefault();

    private static string Fa(int x) =>
        string.Concat(x.ToString().Select(c => (char)('۰' + (c - '0'))));
}
