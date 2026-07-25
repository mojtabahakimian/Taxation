using Prg_Moadian.Generaly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using static Prg_Moadian.CNNMANAGER.TaxModel;

namespace Prg_Moadian.FUNCTIONS
{
    public static class InvoiceValidator
    {
        public class ValidationResult
        {
            public bool IsValid => !Errors.Any();
            public List<string> Errors { get; set; } = new List<string>();
            public List<string> Warnings { get; set; } = new List<string>();

            public void AddError(string msg) => Errors.Add(msg);
            public void AddWarning(string msg) => Warnings.Add(msg);
        }

        // تلورانس مجاز برای اختلافات گرد کردن (۵ ریال خطای مجاز)
        private const decimal Tolerance = 5;

        public static ValidationResult Validate(InvoiceModel.Header header, List<InvoiceModel.Body> bodies)
        {
            var result = new ValidationResult();

            if (header == null)
            {
                result.AddError("خطا: هدر صورتحساب یافت نشد.");
                return result;
            }

            if (bodies == null || !bodies.Any())
            {
                result.AddError("خطا: اقلام (بدنه) صورتحساب یافت نشد.");
                return result;
            }

            // 1. بررسی شناسه‌ها و کدهای اقتصادی (بر اساس V7.8)
            ValidateIdentities(header, result);

            // 2. بررسی تاریخ‌ها (باگ 02002 و مهلت 21 روزه)
            ValidateDates(header, result);

            // 3. بررسی محاسبات سطری (بدنه)
            ValidateLineItems(bodies, result);

            // 4. بررسی سرجمع‌ها (هدر با مجموع بدنه)
            ValidateTotals(header, bodies, result);

            // 5. بررسی روش تسویه و مبالغ پرداختی
            ValidatePayment(header, result);

            return result;
        }

        private static void ValidateIdentities(InvoiceModel.Header header, ValidationResult result)
        {
            // بررسی شماره مالیاتی (Taxid)
            if (string.IsNullOrWhiteSpace(header.Taxid))
                result.AddError("شماره منحصر به فرد مالیاتی (Taxid) خالی است.");
            else if (header.Taxid.Length != 22)
                result.AddError($"طول شماره مالیاتی باید دقیقاً ۲۲ کاراکتر باشد. طول فعلی: {header.Taxid.Length}");

            // بررسی سریال داخلی
            if (string.IsNullOrWhiteSpace(header.Inno))
                result.AddError("سریال داخلی صورتحساب (Inno) خالی است.");

            // بررسی فاکتورهای ارجاعی (اصلاحی=2، ابطالی=3، برگشتی=4)
            if (header.Ins == 2 || header.Ins == 3 || header.Ins == 4)
            {
                if (string.IsNullOrWhiteSpace(header.Irtaxid))
                    result.AddError("برای صورتحساب‌های اصلاحی، ابطالی یا برگشتی، داشتن شناسه مرجع (Irtaxid) صد در صد الزامی است.");
                else if (header.Irtaxid.Length != 22)
                    result.AddError($"شناسه مرجع (Irtaxid) باید دقیقاً ۲۲ کاراکتر باشد. مقدار وارد شده نامعتبر است.");
            }

            // بررسی شناسه فروشنده
            if (string.IsNullOrWhiteSpace(header.Tins))
            {
                result.AddError("شناسه اقتصادی فروشنده (Tins) الزامی است.");
            }
            else
            {
                if (header.Tins.Length == 10 && !IsValidNationalCode(header.Tins))
                    result.AddError($"کد ملی فروشنده (Tins: {header.Tins}) از نظر الگوریتم کنترلی نامعتبر است.");
                else if (header.Tins.Length == 11 && !IsValidLegalNationalId(header.Tins))
                    result.AddError($"شناسه ملی حقوقی فروشنده (Tins: {header.Tins}) از نظر الگوریتم کنترلی نامعتبر است.");
                else if (header.Tins.Length != 10 && header.Tins.Length != 11 && header.Tins.Length != 14)
                    result.AddError($"طول شناسه فروشنده ({header.Tins}) استاندارد نیست (باید ۱۰، ۱۱ یا ۱۴ رقم باشد).");
            }

            // بررسی خریدار بر اساس نوع (Tob) طبق سند V7.8
            if (header.Tob == 1) // 1 = شخص حقیقی
            {
                if (string.IsNullOrWhiteSpace(header.Bid))
                    result.AddError("برای خریدار حقیقی، وارد کردن کد ملی (Bid) الزامی است.");
                else if (header.Bid.Length != 10 || !IsValidNationalCode(header.Bid))
                    result.AddError($"کد ملی خریدار حقیقی (Bid: {header.Bid}) نامعتبر است.");
            }
            else if (header.Tob == 2 || header.Tob == 3) // 2 = حقوقی ، 3 = مشارکت مدنی
            {
                string legalId = !string.IsNullOrWhiteSpace(header.Tinb) ? header.Tinb : header.Bid;
                if (string.IsNullOrWhiteSpace(legalId))
                    result.AddError("برای خریدار حقوقی/مشارکت مدنی، شناسه ملی (Tinb یا Bid) الزامی است.");
                else if (legalId.Length != 11 || !IsValidLegalNationalId(legalId))
                    result.AddError($"شناسه ملی حقوقی خریدار ({legalId}) نامعتبر است یا ۱۱ رقمی نیست.");
            }
            else if (header.Tob == 4) // 4 = اتباع غیر ایرانی
            {
                if (string.IsNullOrWhiteSpace(header.Bid))
                    result.AddError("برای اتباع خارجی، وارد کردن کد فراگیر (Bid) الزامی است.");
            }

            // بررسی کد پستی (Bpc) - اختیاری است اما اگر پر شد باید درست باشد
            if (!string.IsNullOrWhiteSpace(header.Bpc) && !Regex.IsMatch(header.Bpc, @"^\d{10}$"))
            {
                result.AddWarning($"کد پستی خریدار ({header.Bpc}) باید دقیقاً ۱۰ رقم باشد.");
            }
        }

        private static void ValidateDates(InvoiceModel.Header header, ValidationResult result)
        {
            DateTime indatim = DateTimeOffset.FromUnixTimeMilliseconds(header.Indatim).DateTime;

            // بدست آوردن ساعت سرور یا ساعت لوکال (بسته به تایم‌سینک شما)
            DateTime serverNow = DateTime.UtcNow + CL_Generaly.TokenLifeTime.ServerClockSkew;
            if (CL_Generaly.TokenLifeTime.ServerClockSkew == TimeSpan.Zero)
            {
                serverNow = DateTime.UtcNow;
            }

            // قانون عدم مجاز بودن تاریخ آینده (تلورانس 5 دقیقه برای اختلاف کلاک)
            if (indatim > serverNow.AddMinutes(5))
            {
                result.AddError($"خطای مهم (02002): تاریخ صدور فاکتور ({indatim.ToLocalTime():yyyy/MM/dd HH:mm}) در آینده است!");
            }

            // قانون ۱۲ روزه دارایی (طبق آخرین بخشنامه‌های اجرایی)
            var daysDiff = (serverNow - indatim).TotalDays;
            if (daysDiff > 12)
            {
                result.AddError($"تاریخ صدور ({indatim.ToLocalTime():yyyy/MM/dd}) بیش از ۱۲ روز ({daysDiff:F0} روز) با امروز فاصله دارد. سامانه مودیان قطعاً این فاکتور را به دلیل اتمام مهلت قانونی رد می‌کند.");
            }
        }

        private static void ValidateLineItems(List<InvoiceModel.Body> bodies, ValidationResult result)
        {
            for (int i = 0; i < bodies.Count; i++)
            {
                var item = bodies[i];
                int rowNum = i + 1;

                if (string.IsNullOrWhiteSpace(item.Sstid) || item.Sstid.Length != 13)
                    result.AddError($"ردیف {rowNum}: شناسه کالا/خدمت ({item.Sstid}) نامعتبر است. (باید دقیقاً ۱۳ رقم باشد)");

                decimal prdis = item.Am * item.Fee;
                if (Math.Abs(prdis - item.Prdis) > Tolerance)
                    result.AddError($"ردیف {rowNum}: مقدار ({item.Am}) × فی ({item.Fee:N0}) = {prdis:N0}، اما 'قبل از تخفیف' {item.Prdis:N0} ثبت شده است.");

                decimal adis = item.Prdis - item.Dis;
                if (Math.Abs(adis - item.Adis) > Tolerance)
                    result.AddError($"ردیف {rowNum}: مبلغ قبل تخفیف ({item.Prdis:N0}) - تخفیف ({item.Dis:N0}) = {adis:N0}، اما 'بعد تخفیف' {item.Adis:N0} ثبت شده است.");

                // محاسبه مالیات بر اساس استاندارد مودیان (حذف اعشار / Truncate)
                decimal calculatedVam = Math.Truncate(item.Adis * item.Vra / 100m);
                if (Math.Abs(calculatedVam - item.Vam) > Tolerance)
                    result.AddError($"ردیف {rowNum}: مالیات با نرخ {item.Vra}٪ باید {calculatedVam:N0} باشد، اما {item.Vam:N0} ثبت شده است.");

                // محاسبه جمع کل سطر: Tsstam = Adis + Vam + Odam + Olam (طبق جدول 53 مستند V7.8 - حذف Consfee)
                decimal otherTaxes = item.Odam + item.Olam;
                decimal expectedTsstam = item.Adis + item.Vam + otherTaxes;
                if (Math.Abs(expectedTsstam - item.Tsstam) > Tolerance)
                    result.AddError($"ردیف {rowNum}: جمع کل ردیف (Tsstam) باید {expectedTsstam:N0} باشد، اما {item.Tsstam:N0} درج شده است.");
            }
        }

        private static void ValidateTotals(InvoiceModel.Header header, List<InvoiceModel.Body> bodies, ValidationResult result)
        {
            decimal sumPrdis = bodies.Sum(x => x.Prdis);
            decimal sumDis = bodies.Sum(x => x.Dis);
            decimal sumAdis = bodies.Sum(x => x.Adis);
            decimal sumVam = bodies.Sum(x => x.Vam);
            decimal sumTsstam = bodies.Sum(x => x.Tsstam);

            if (Math.Abs(header.Tprdis - sumPrdis) > Tolerance)
                result.AddError($"جمع مبالغ قبل تخفیف در هدر ({header.Tprdis:N0}) با جمع ردیف‌ها ({sumPrdis:N0}) مغایرت دارد.");

            if (Math.Abs(header.Tdis - sumDis) > Tolerance)
                result.AddError($"جمع تخفیف‌ها در هدر ({header.Tdis:N0}) با جمع ردیف‌ها ({sumDis:N0}) مغایرت دارد.");

            if (Math.Abs(header.Tadis - sumAdis) > Tolerance)
                result.AddError($"مبلغ خالص (Tadis) در هدر ({header.Tadis:N0}) با جمع ردیف‌ها ({sumAdis:N0}) مغایرت دارد.");

            if (Math.Abs(header.Tvam - sumVam) > Tolerance)
                result.AddError($"مجموع مالیات در هدر ({header.Tvam:N0}) با جمع ردیف‌ها ({sumVam:N0}) مغایرت دارد.");

            // بررسی Tbill (باید برابر با مجموع Tsstam سطرها باشد)
            if (Math.Abs(header.Tbill - sumTsstam) > Tolerance)
                result.AddError($"مبلغ کل صورتحساب (Tbill: {header.Tbill:N0}) با مجموع ردیف‌ها ({sumTsstam:N0}) همخوانی ندارد.");

            // بررسی ریاضی Tbill در سطح هدر: Tbill = Tadis + Tvam + Todam
            decimal expectedHeaderBill = header.Tadis + header.Tvam + header.Todam;
            if (Math.Abs(header.Tbill - expectedHeaderBill) > Tolerance)
                result.AddError($"تراز هدر بر هم خورده است! خالص({header.Tadis:N0}) + مالیات({header.Tvam:N0}) + سایر({header.Todam:N0}) = {expectedHeaderBill:N0} اما Tbill = {header.Tbill:N0} است.");
        }

        private static void ValidatePayment(InvoiceModel.Header header, ValidationResult result)
        {
            decimal tbill = header.Tbill;
            decimal cap = header.Cap;   // نقدی
            decimal insp = header.Insp; // نسیه

            if (header.Setm == 1) // فقط نقدی
            {
                if (Math.Abs(tbill - cap) > Tolerance)
                    result.AddError($"در روش تسویه نقدی، مبلغ نقدی (Cap={cap:N0}) باید دقیقاً برابر کل فاکتور (Tbill={tbill:N0}) باشد.");
                if (insp > 0)
                    result.AddError("در روش تسویه نقدی، مبلغ نسیه (Insp) باید صفر باشد.");
            }
            else if (header.Setm == 2) // فقط نسیه
            {
                if (Math.Abs(tbill - insp) > Tolerance)
                    result.AddError($"در روش تسویه نسیه، مبلغ نسیه (Insp={insp:N0}) باید دقیقاً برابر کل فاکتور (Tbill={tbill:N0}) باشد.");
                if (cap > 0)
                    result.AddError("در روش تسویه نسیه، مبلغ نقدی (Cap) باید صفر باشد.");
            }
            else if (header.Setm == 3) // نقدی/نسیه
            {
                if (Math.Abs(tbill - (cap + insp)) > Tolerance)
                    result.AddError($"در تسویه نقد/نسیه، مجموع نقدی ({cap:N0}) و نسیه ({insp:N0}) باید برابر کل فاکتور ({tbill:N0}) باشد.");
            }
            else
            {
                result.AddError($"روش تسویه (Setm = {header.Setm}) نامعتبر است (باید ۱، ۲ یا ۳ باشد).");
            }
        }

        // ======================= Helper Methods =======================

        // الگوریتم استاندارد صحت‌سنجی کد ملی اشخاص حقیقی
        public static bool IsValidNationalCode(string input)
        {
            if (string.IsNullOrWhiteSpace(input) || !Regex.IsMatch(input, @"^\d{10}$"))
                return false;

            var check = Convert.ToInt32(input.Substring(9, 1));
            var sum = Enumerable.Range(0, 9).Select(x => Convert.ToInt32(input.Substring(x, 1)) * (10 - x)).Sum() % 11;
            return (sum < 2 && check == sum) || (sum >= 2 && check + sum == 11);
        }

        // الگوریتم استاندارد صحت‌سنجی شناسه ملی اشخاص حقوقی (شرکت‌ها)
        public static bool IsValidLegalNationalId(string input)
        {
            if (string.IsNullOrWhiteSpace(input) || !Regex.IsMatch(input, @"^\d{11}$"))
                return false;

            int tens = input[9] - '0';
            int[] multipliers = { 29, 27, 23, 19, 17, 29, 27, 23, 19, 17 };

            int sum = 0;
            for (int i = 0; i < 10; i++)
            {
                int val = (input[i] - '0') + tens;
                sum += val * multipliers[i];
            }

            int check = input[10] - '0';
            int remainder = sum % 11;

            if (remainder == 10) remainder = 0;
            return remainder == check;
        }
    }
}