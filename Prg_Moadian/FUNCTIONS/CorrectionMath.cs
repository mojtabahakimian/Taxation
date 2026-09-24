using Prg_Moadian.SQLMODELS;

namespace Prg_Moadian.FUNCTIONS
{
    /// <summary>
    /// ریاضیاتِ خالصِ فرم صورتحساب اصلاحی.
    ///
    /// این کد قبلاً داخل WIN_MODIFYINVOICE.ReCalculateTotals بود و چون آنجا
    /// به پنجره وابسته بود، هیچ تستی نمی‌توانست به آن برسد — در حالی که همین
    /// چند خط تعیین می‌کند چه مبلغی به سامانه می‌رود.
    ///
    /// فرمول‌ها *عیناً* از همان‌جا منتقل شده‌اند. هیچ گردکردنی، هیچ ترتیبی و
    /// هیچ شرطی عوض نشده است.
    /// </summary>
    public static class CorrectionMath
    {
        /// <summary>
        /// مبالغ ریالی: حذف اعشار با برش.
        ///
        /// مهم: این باید دقیقاً همان عملیاتی باشد که RecalculateRow انجام می‌دهد.
        /// قبلاً محاسبه با Math.Truncate و کنترل با Math.Round بود؛ هر جا کسر
        /// اعشار بالای ۰٫۵ می‌شد، برنامه پیام «اختلاف رندینگ VAT» می‌داد در حالی
        /// که هیچ اشکالی وجود نداشت — یعنی خودش خودش را رد می‌کرد.
        /// </summary>
        public static decimal RoundIrr(decimal v) => Math.Truncate(v);

        /// <summary>رندینگ تعداد/مقدار، تا ۴ رقم اعشار.</summary>
        public static decimal RoundQty(decimal v) => Math.Round(v, 4);

        /// <summary>
        /// محاسبه مجدد مبالغ یک ردیف.
        ///
        /// ترتیب عملیات معنادار است:
        ///     تعداد  → گرد تا ۴ رقم
        ///     مبلغ واحد و تخفیف → برش
        ///     قبل از تخفیف = برش(تعداد × مبلغ واحد)
        ///     بعد از تخفیف = قبل از تخفیف − تخفیف
        ///     مالیات = برش(بعد از تخفیف × نرخ ÷ ۱۰۰)
        ///     مبلغ کل قلم = بعد از تخفیف + مالیات
        ///
        /// توجه: عوارض و سایر وجوه قانونی (Odam/Olam) عمداً در مبلغ کل قلم
        /// وارد نمی‌شوند. مسیر تکی و گروهی هم همین‌طورند.
        /// </summary>
        public static void RecalculateRow(TAXDTL item)
        {
            if (item == null) return;

            item.Am = RoundQty((decimal)item.Am);          // تعداد/مقدار // MEGHk
            item.Fee = Math.Truncate((decimal)item.Fee);   // مبلغ واحد   // MABL
            item.Dis = Math.Truncate((decimal)item.Dis);   // مبلغ تخفیف  // N_MOIN

            var MABL_K = Math.Truncate((decimal)(item.Am * item.Fee));
            item.Prdis = MABL_K;                           // مبلغ قبل از تخفیف // MABL_K

            item.Adis = item.Prdis - (item.Dis ?? 0);      // مبلغ بعد از تخفیف

            var IMBAA = Math.Truncate((decimal)(item.Adis * (item.Vra ?? 0) / 100));
            item.Vam = IMBAA;                              // مالیات بر ارزش افزوده

            item.Tsstam = item.Adis + item.Vam;            // مبلغ کل کالا/خدمت
        }

        /// <summary>مجموع‌های سرصفحه را از روی ردیف‌ها می‌سازد.</summary>
        public static (decimal Tprdis, decimal Tdis, decimal Tadis, decimal Tvam, decimal Tbill)
            HeaderSums(IEnumerable<TAXDTL> rows)
        {
            decimal tprdis = 0, tdis = 0, tadis = 0, tvam = 0, tbill = 0;
            foreach (var r in rows)
            {
                tprdis += r.Prdis ?? 0;
                tdis += r.Dis ?? 0;
                tadis += r.Adis ?? 0;
                tvam += r.Vam ?? 0;
                tbill += r.Tsstam ?? 0;
            }
            return (tprdis, tdis, tadis, tvam, tbill);
        }

        /// <summary>
        /// همان کنترلی که فرم قبل از ارسال انجام می‌دهد: مالیات هر ردیف با
        /// فرمول بخواند و جمع‌های سرصفحه با اقلام تراز باشند.
        /// </summary>
        public static bool TotalsAreConsistent(IEnumerable<TAXDTL> rows, out string message)
        {
            message = string.Empty;
            var list = rows?.ToList() ?? new List<TAXDTL>();
            if (list.Count == 0) return true;

            foreach (var r in list)
            {
                var adis = r.Adis ?? 0;
                var vra = r.Vra ?? 0;
                var expected = RoundIrr(adis * vra / 100m);
                if ((r.Vam ?? 0) != expected)
                {
                    message = $"مالیات ردیف با فرمول نمی‌خواند: {r.Vam ?? 0} در برابر {expected}";
                    return false;
                }
                if ((r.Tsstam ?? 0) != adis + (r.Vam ?? 0))
                {
                    message = $"مبلغ کل قلم با «بعد از تخفیف + مالیات» نمی‌خواند: {r.Tsstam ?? 0}";
                    return false;
                }
            }

            var s = HeaderSums(list);
            if (s.Tbill != s.Tadis + s.Tvam)
            {
                message = $"مجموع صورتحساب با «مجموع بعد از تخفیف + مجموع مالیات» نمی‌خواند: " +
                          $"{s.Tbill} در برابر {s.Tadis + s.Tvam}";
                return false;
            }
            if (s.Tadis != s.Tprdis - s.Tdis)
            {
                message = $"مجموع بعد از تخفیف با «قبل از تخفیف منهای تخفیف» نمی‌خواند: " +
                          $"{s.Tadis} در برابر {s.Tprdis - s.Tdis}";
                return false;
            }
            return true;
        }
    }
}
