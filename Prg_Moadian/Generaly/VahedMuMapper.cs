using Prg_Moadian.SQLMODELS;

namespace Prg_Moadian.Generaly
{
    /// <summary>
    /// مدل جدول واحدهای مودیان (TCOD_VAHED_EXTENDED)
    /// </summary>
    public class TCOD_VAHED_EXTENDED
    {
        public int IDD { get; set; }
        public string NAME_MO { get; set; }
    }

    /// <summary>
    /// نگاشت واحد کالا در نرم‌افزار (TCOD_VAHEDS.NAMES) به شناسه واحد مودیان (TCOD_VAHED_EXTENDED.IDD)
    /// برای هر سطر فاکتور، بر اساس نام واحد موجود در INVO_LST.VAHED_K، معادل مودیانی پیدا می‌کند.
    /// </summary>
    public static class VahedMuMapper
    {
        /// <summary>
        /// برای هر سطر فاکتور که VNAMES دارد، فیلد mu را بر اساس تطابق معنایی با واحدهای مودیان تنظیم می‌کند.
        /// اولویت: تطابق دقیق → واحد نرم‌افزار داخل نام مودیان → نام مودیان داخل واحد نرم‌افزار → بدون تغییر (STUF_DEF.mu)
        /// </summary>
        /// <param name="lines">سطرهای فاکتور با VNAMES پر شده از TCOD_VAHEDS</param>
        /// <param name="extUnits">لیست واحدهای مودیان از TCOD_VAHED_EXTENDED</param>
        public static void ResolveAll(IEnumerable<DRV_TBL> lines, IList<TCOD_VAHED_EXTENDED> extUnits)
        {
            if (extUnits == null || !extUnits.Any()) return;

            foreach (var line in lines)
            {
                var vahedName = line.VNAMES?.Trim();
                if (string.IsNullOrEmpty(vahedName)) continue;

                var matched = FindBestMatch(vahedName, extUnits);
                if (matched != null)
                    line.mu = matched.IDD.ToString();
            }
        }

        /// <summary>
        /// پیدا کردن بهترین تطابق واحد مودیان برای یک نام واحد نرم‌افزاری
        /// </summary>
        private static TCOD_VAHED_EXTENDED FindBestMatch(string vahedName, IList<TCOD_VAHED_EXTENDED> extUnits)
        {
            // 1. تطابق دقیق
            var exact = extUnits.FirstOrDefault(u =>
                string.Equals(u.NAME_MO?.Trim(), vahedName, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            // 2. نام واحد نرم‌افزار داخل نام مودیان باشد (مثلا "کارتن" داخل "(master case) کارتن")
            var containedIn = extUnits.FirstOrDefault(u =>
                u.NAME_MO != null &&
                u.NAME_MO.Contains(vahedName, StringComparison.OrdinalIgnoreCase));
            if (containedIn != null) return containedIn;

            // 3. نام مودیان داخل نام واحد نرم‌افزار باشد
            var containsExt = extUnits.FirstOrDefault(u =>
                u.NAME_MO != null &&
                vahedName.Contains(u.NAME_MO.Trim(), StringComparison.OrdinalIgnoreCase));
            if (containsExt != null) return containsExt;

            return null;
        }
    }
}
