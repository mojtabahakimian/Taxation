using Prg_Moadian.FUNCTIONS;
using Prg_Moadian.SQLMODELS;

namespace MoadianLocalTest;

/// <summary>
/// گروه ۲۳ — ریاضیات فرم صورتحساب اصلاحی.
///
/// این منطق تا حالا داخل یک event handler پنجره گیر افتاده بود و هیچ تستی
/// به آن نمی‌رسید — در حالی که همین چند خط تعیین می‌کند چه مبلغی به سامانه
/// می‌رود. حالا در CorrectionMath است و اینجا قفل می‌شود.
///
/// ضمنا واگرایی شناخته‌شده با مسیر تکی/گروهی هم اینجا *ثبت* می‌شود، نه اینکه
/// «درست» یا «غلط» خوانده شود — تا اگر روزی کسی خواست یکسان‌شان کند، بداند
/// دقیقا چه چیزی عوض می‌شود.
/// </summary>
internal static class CorrectionPath
{
    public static void Run(Action<string, bool, string> check, Action<string> line)
    {
        int n = 1;
        void C(string name, bool ok, string detail = "") => check($"۲۳-{Fa(n++)} {name}", ok, detail);

        // ---------------- گردکردن ----------------

        C("مبالغ ریالی برش می‌خورند نه گرد (وگرنه فرم خروجی خودش را رد می‌کند)",
          CorrectionMath.RoundIrr(100.6m) == 100m && CorrectionMath.RoundIrr(100.4m) == 100m,
          $"{CorrectionMath.RoundIrr(100.6m)}");

        C("تعداد تا ۴ رقم اعشار گرد می‌شود",
          CorrectionMath.RoundQty(2.333335m) == 2.3333m ||
          CorrectionMath.RoundQty(2.333335m) == 2.3334m,
          CorrectionMath.RoundQty(2.333335m).ToString());

        // ---------------- محاسبه یک ردیف ----------------

        var row = Row(am: 3m, fee: 1000.9m, dis: 0m, vra: 10m);
        CorrectionMath.RecalculateRow(row);
        C("مبلغ واحد برش می‌خورد پیش از ضرب",
          row.Fee == 1000m, row.Fee.ToString());
        C("قبل از تخفیف = برش(تعداد × مبلغ واحد)",
          row.Prdis == 3000m, row.Prdis.ToString());
        C("بعد از تخفیف = قبل از تخفیف منهای تخفیف",
          row.Adis == 3000m, row.Adis.ToString());
        C("مالیات = برش(بعد از تخفیف × نرخ ÷ ۱۰۰)",
          row.Vam == 300m, row.Vam.ToString());
        C("مبلغ کل قلم = بعد از تخفیف + مالیات",
          row.Tsstam == 3300m, row.Tsstam.ToString());

        // مالیات کسردار — همان جایی که Round و Truncate از هم جدا می‌شوند
        var frac = Row(am: 1m, fee: 1005m, dis: 0m, vra: 10m);
        CorrectionMath.RecalculateRow(frac);
        C("مالیات کسردار به پایین برش می‌خورد نه گرد (۱۰۰٫۵ → ۱۰۰)",
          frac.Vam == 100m, frac.Vam.ToString());

        // تخفیف
        var disc = Row(am: 7m, fee: 12345m, dis: 1111m, vra: 9m);
        CorrectionMath.RecalculateRow(disc);
        C("مسیر تخفیف درست است",
          disc.Prdis == 86415m && disc.Adis == 85304m && disc.Vam == 7677m,
          $"{disc.Prdis}/{disc.Adis}/{disc.Vam}");

        // کالای بدون مالیات
        var novat = Row(am: 2m, fee: 5000m, dis: 0m, vra: 0m);
        CorrectionMath.RecalculateRow(novat);
        C("کالای با نرخ صفر مالیات نمی‌گیرد",
          novat.Vam == 0m && novat.Tsstam == 10000m, $"{novat.Vam}/{novat.Tsstam}");

        // ---------------- عوارض عمداً بیرون است ----------------

        var odam = Row(am: 1m, fee: 1_000_000m, dis: 0m, vra: 9m);
        odam.Odam = 50_000m; odam.Olam = 25_000m;
        CorrectionMath.RecalculateRow(odam);
        C("عوارض و سایر وجوه قانونی وارد مبلغ کل قلم نمی‌شوند",
          odam.Tsstam == 1_090_000m, odam.Tsstam.ToString());

        // ---------------- جمع‌های سرصفحه ----------------

        var rows = new List<TAXDTL>
        {
            Row(am: 2m, fee: 1000m, dis: 100m, vra: 10m),
            Row(am: 3m, fee: 2000m, dis: 0m,   vra: 10m),
            Row(am: 1m, fee:  500m, dis: 50m,  vra: 0m),
        };
        foreach (var r in rows) CorrectionMath.RecalculateRow(r);
        var s = CorrectionMath.HeaderSums(rows);

        C("مجموع قبل از تخفیف درست است", s.Tprdis == 8500m, s.Tprdis.ToString());
        C("مجموع تخفیفات درست است", s.Tdis == 150m, s.Tdis.ToString());
        C("مجموع بعد از تخفیف = قبل از تخفیف منهای تخفیف",
          s.Tadis == s.Tprdis - s.Tdis, $"{s.Tadis}");
        C("مجموع صورتحساب = مجموع بعد از تخفیف + مجموع مالیات",
          s.Tbill == s.Tadis + s.Tvam, $"{s.Tbill} در برابر {s.Tadis + s.Tvam}");

        // ---------------- کنترل تراز ----------------

        C("مجموعه سالم، تراز تشخیص داده می‌شود",
          CorrectionMath.TotalsAreConsistent(rows, out var m1), m1);

        var broken = rows.Select(Clone).ToList();
        broken[0].Vam = (broken[0].Vam ?? 0) + 1;         // مالیات دستکاری‌شده
        C("مالیات دستکاری‌شده گرفته می‌شود",
          !CorrectionMath.TotalsAreConsistent(broken, out var m2), m2);

        var broken2 = rows.Select(Clone).ToList();
        broken2[1].Tsstam = (broken2[1].Tsstam ?? 0) + 5; // مبلغ کل قلم دستکاری‌شده
        C("مبلغ کل قلمِ ناسازگار گرفته می‌شود",
          !CorrectionMath.TotalsAreConsistent(broken2, out var m3), m3);

        C("مجموعه خالی تراز حساب می‌شود (ابطالی بدون قلم)",
          CorrectionMath.TotalsAreConsistent(new List<TAXDTL>(), out _));

        // ---------------- واگرایی شناخته‌شده با مسیر تکی و گروهی ----------------

        // مسیر تکی و گروهی مالیات را *فقط اگر مقدار ذخیره‌شده بزرگتر از صفر باشد*
        // دوباره حساب می‌کنند. فرم اصلاحی بی‌قید و شرط حساب می‌کند.
        //
        // نتیجه: قلمی که با مالیات صفر روی کالای مالیات‌دار ارسال و پذیرفته شده،
        // از فرم اصلاحی با مالیات غیرصفر برمی‌گردد و مبلغ کل با مرجع نمی‌خواند.
        //
        // این تست آن را «غلط» نمی‌خواند — فقط ثبتش می‌کند تا اگر روزی کسی
        // خواست دو مسیر را یکسان کند، بداند دقیقا چه چیزی عوض می‌شود.
        var zeroVat = Row(am: 1m, fee: 1_000_000m, dis: 0m, vra: 9m);
        zeroVat.Vam = 0m;                                  // آنچه در مرجع ذخیره شده
        CorrectionMath.RecalculateRow(zeroVat);
        bool recomputed = zeroVat.Vam == 90_000m;
        line($"      · واگرایی ثبت‌شده: فرم اصلاحی مالیات صفرِ ذخیره‌شده را " +
             (recomputed ? "بازمحاسبه می‌کند" : "دست‌نخورده می‌گذارد") +
             $" (مالیات نهایی {zeroVat.Vam})");
        C("فرم اصلاحی مالیات را بی‌قید و شرط بازمحاسبه می‌کند (رفتار فعلی)",
          recomputed, zeroVat.Vam?.ToString());

        // ---------------- یکسانی با مسیر گروهی در حالت عادی ----------------

        // وقتی مالیات ذخیره‌شده غیرصفر باشد، هر سه مسیر باید به یک عدد برسند.
        foreach (var (am, fee, dis, vra) in new (decimal, decimal, decimal, decimal)[]
                 { (3m, 1000.9m, 0m, 10m), (7m, 12345m, 1111m, 9m), (1m, 1005m, 0m, 10m) })
        {
            var a = Row(am, fee, dis, vra); a.Vam = 1m;
            CorrectionMath.RecalculateRow(a);

            // بازتولید دقیق فرمول مسیر گروهی
            decimal bFee = Math.Truncate(fee);
            decimal bQty = Math.Round(am, 4);
            decimal bPrdis = Math.Truncate(bQty * bFee);
            decimal bDis = Math.Truncate(dis);
            decimal bAdis = bPrdis - bDis;
            decimal bVam = Math.Truncate(bAdis * vra / 100m);
            decimal bTsstam = bAdis + bVam;

            C($"همخوانی با مسیر گروهی برای ({am}×{fee} تخفیف {dis} نرخ {vra})",
              a.Prdis == bPrdis && a.Adis == bAdis && a.Vam == bVam && a.Tsstam == bTsstam,
              $"اصلاحی {a.Prdis}/{a.Adis}/{a.Vam}/{a.Tsstam} — گروهی {bPrdis}/{bAdis}/{bVam}/{bTsstam}");
        }
    }

    // ---------------------------------------------------------------- کمکی

    private static TAXDTL Row(decimal am, decimal fee, decimal dis, decimal vra) =>
        new TAXDTL { Am = am, Fee = fee, Dis = dis, Vra = vra };

    private static TAXDTL Clone(TAXDTL r) => new TAXDTL
    {
        Am = r.Am, Fee = r.Fee, Dis = r.Dis, Vra = r.Vra,
        Prdis = r.Prdis, Adis = r.Adis, Vam = r.Vam, Tsstam = r.Tsstam,
    };

    private static string Fa(int x) =>
        string.Concat(x.ToString().Select(c => (char)('۰' + (c - '0'))));
}
