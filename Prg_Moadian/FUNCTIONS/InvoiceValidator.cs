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

            // بررسی خریدار طبق جدول ۱۱ صفحات ۳۳ و ۳۴ V7.9.
            // دو مسیر مجاز وجود دارد: «شماره اقتصادی» یا «شماره ملی/کد فراگیر + کد پستی».
            if (!MoadianRules.ValidateBuyer(header.Inty, header.Inp, header.Tob, header.Tinb, header.Bid, header.Bpc, out var buyerError))
                result.AddError(buyerError);

            // صحت‌سنجی الگوریتمی — فقط وقتی مقدار موجود است (جدا از الزام سند).
            if (header.Tob == 1 && !string.IsNullOrWhiteSpace(header.Bid) && header.Bid.Trim().Length == 10
                && !IsValidNationalCode(header.Bid.Trim()))
            {
                result.AddError($"کد ملی خریدار حقیقی (Bid: {header.Bid}) از نظر الگوریتم کنترلی نامعتبر است.");
            }

            if ((header.Tob == 2 || header.Tob == 3) && !string.IsNullOrWhiteSpace(header.Bid)
                && header.Bid.Trim().Length == 11 && !IsValidLegalNationalId(header.Bid.Trim()))
            {
                result.AddError($"شناسه ملی حقوقی خریدار (Bid: {header.Bid}) از نظر الگوریتم کنترلی نامعتبر است.");
            }

            // بررسی کد پستی (Bpc) - اختیاری است اما اگر پر شد باید درست باشد
            if (!string.IsNullOrWhiteSpace(header.Bpc) && !Regex.IsMatch(header.Bpc.Trim(), @"^\d{10}$"))
            {
                result.AddWarning($"کد پستی خریدار ({header.Bpc}) باید دقیقاً ۱۰ رقم باشد.");
            }

            // کد شعبه خریدار/فروشنده: سامانه با ^\d{4}$ کنترل می‌کند (ص۳۳).
            if (!string.IsNullOrWhiteSpace(header.Bbc) && !Regex.IsMatch(header.Bbc.Trim(), @"^\d{4}$"))
                result.AddError($"کد شعبه خریدار ({header.Bbc}) باید دقیقاً ۴ رقم باشد.");

            if (!string.IsNullOrWhiteSpace(header.Sbc) && !Regex.IsMatch(header.Sbc.Trim(), @"^\d{4}$"))
                result.AddError($"کد شعبه فروشنده ({header.Sbc}) باید دقیقاً ۴ رقم باشد.");
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

            // مهلت ارسال — سند هیچ عدد ثابتی نمی‌دهد (ص۲۵ ردیف‌های ۵ تا ۷ و جدول ۸۶ ص۹۳
            // می‌گویند «مهلت مجاز اعلام شده توسط سازمان»). پس عدد از تنظیمات می‌آید.
            var daysDiff = (serverNow - indatim).TotalDays;
            if (daysDiff > MoadianRules.SendDeadlineDays)
            {
                // گذشتن از مهلت به خودی خود رد نیست؛ مسیر ماده ۹ وجود دارد.
                if (header.Insr == 1)
                {
                    // جدول ۸۶ ص۹۳ ردیف ۱: با insr=1 و تاریخ ثبت مطابق قواعد، صورتحساب
                    // به عنوان «موضوع ماده ۹» پذیرفته می‌شود.
                    if (header.Indati2m <= 0)
                    {
                        result.AddError("برای صورتحساب موضوع ماده ۹، فیلد تاریخ و زمان ثبت صورتحساب (Indati2m) باید مطابق قواعد پر شود.");
                    }
                    else
                    {
                        // ص۲۸ ردیف ۷: فاصله «تاریخ و زمان ثبت صورتحساب» تا ارسال آن هم
                        // نباید از مهلت مجاز بیشتر باشد. اگر Indati2m روی تاریخ صدورِ
                        // گذشته مانده باشد، پر کردن Insr به‌تنهایی سند را معتبر نمی‌کند.
                        DateTime indati2m = DateTimeOffset.FromUnixTimeMilliseconds(header.Indati2m).DateTime;
                        var regDays = (serverNow - indati2m).TotalDays;

                        if (regDays > MoadianRules.SendDeadlineDays)
                        {
                            result.AddError(
                                $"تاریخ ثبت صورتحساب ({indati2m.ToLocalTime():yyyy/MM/dd}) {regDays:F0} روز با امروز فاصله دارد و از مهلت " +
                                $"{MoadianRules.SendDeadlineDays} روزه گذشته است (ص۲۸ ردیف ۷). برای مسیر ماده ۹، تاریخ ثبت باید لحظه واقعی ثبت باشد، " +
                                "نه تاریخ صدور گذشته.");
                        }
                        else
                        {
                            result.AddWarning($"فاصله صدور تا ارسال {daysDiff:F0} روز است و صورتحساب به عنوان «موضوع ماده ۹» ارسال می‌شود (Insr=1).");
                        }
                    }
                }
                else
                {
                    result.AddError(
                        $"تاریخ صدور ({indatim.ToLocalTime():yyyy/MM/dd}) {daysDiff:F0} روز با امروز فاصله دارد و از مهلت " +
                        $"{MoadianRules.SendDeadlineDays} روزه گذشته است. برای ارسال باید قاعده ارسال (Insr) با مقدار ۱ " +
                        $"پر شود تا صورتحساب موضوع ماده ۹ تلقی گردد، وگرنه نامعتبر می‌شود (ص۲۵ ردیف ۷).");
                }
            }
            else if (header.Insr == 1)
            {
                // جدول ۸۶ ص۹۳ ردیف ۲: داخل مهلت، این فیلد خارج از الگوست. پر کردنش
                // باعث رد نمی‌شود ولی بی‌مورد است.
                result.AddWarning("فاصله صدور تا ارسال داخل مهلت مجاز است؛ در این حالت فیلد قاعده ارسال (Insr) خارج از الگو است و بهتر است خالی بماند.");
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

                // جدول ۳۱ ص۵۱: تعداد/مقدار > ۰  — خطای 0103605
                if (item.Am <= 0)
                    result.AddError($"ردیف {rowNum}: تعداد/مقدار ({item.Am}) باید بزرگتر از صفر باشد (جدول ۳۱ ص۵۱).");

                // جدول ۳۴ ص۵۳: مبلغ واحد > ۰  — خطای 0103705
                if (item.Fee <= 0)
                    result.AddError($"ردیف {rowNum}: مبلغ واحد ({item.Fee:N0}) باید بزرگتر از صفر باشد (جدول ۳۴ ص۵۳).");

                // جدول ۴۰ ص۵۸: مبلغ قبل از تخفیف > ۰
                if (item.Prdis <= 0)
                    result.AddError($"ردیف {rowNum}: مبلغ قبل از تخفیف ({item.Prdis:N0}) باید بزرگتر از صفر باشد (جدول ۴۰ ص۵۸).");

                // جدول ۴۱ ص۵۹: ۰ <= تخفیف <= مبلغ قبل از تخفیف  (تخفیف ۱۰۰٪ مجاز است)
                if (item.Dis < 0)
                    result.AddError($"ردیف {rowNum}: مبلغ تخفیف ({item.Dis:N0}) نمی‌تواند منفی باشد.");
                else if (item.Dis - item.Prdis > Tolerance)
                    result.AddError($"ردیف {rowNum}: مبلغ تخفیف ({item.Dis:N0}) نباید از مبلغ قبل از تخفیف ({item.Prdis:N0}) بیشتر باشد (جدول ۴۱ ص۵۹).");

                // جدول ۴۲ ص۶۰: مبلغ بعد از تخفیف >= ۰
                if (item.Adis < -Tolerance)
                    result.AddError($"ردیف {rowNum}: مبلغ بعد از تخفیف ({item.Adis:N0}) نمی‌تواند منفی باشد (جدول ۴۲ ص۶۰).");

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

            if (header.Setm < 1 || header.Setm > 3)
            {
                result.AddError($"روش تسویه (Setm = {header.Setm}) نامعتبر است (جدول ۲۴ ص۴۵: فقط ۱ نقدی، ۲ نسیه، ۳ نقدی/نسیه).");
                return;
            }

            // جدول ۲۴ ص۴۵ ردیف ۳: ثبت cap و insp فقط در حالت «نقدی/نسیه» اجباری است.
            //
            // دقت: در جدول‌های ۲۵ و ۲۶ فقط ردیف ۱ صریحاً مشروط به setm=3 است؛ ردیف ۲
            // (فرمول) و ردیف ۳ (بزرگتر از صفر) بدون قید آمده‌اند. پس ادعای «سند برای
            // setm=1/2 قاعده‌ای ندارد» دقیق نیست. دلیل واقعیِ اعمال‌نکردن، رفتار خودِ
            // سامانه است: در داده عملیاتی بیش از ۱۴ هزار صورتحساب با setm=1/2 و
            // cap/insp برابر کل صورتحساب با موفقیت ثبت شده‌اند.
            if (header.Setm != 3)
                return;

            // جدول ۲۵ ص۴۶ و جدول ۲۶ ص۴۷ و FAQ ص۳۱ (س۱۰-۵ و ۱۰-۶):
            //     C  = Xs - W2 - W - Cr
            //     Cr = Xs - W2 - W - C
            // یعنی مبنای تقسیم، مبلغ کل منهای مالیات و سایر عوارض است.
            decimal basis = MoadianRules.SettlementBase(tbill, header.Tvam, header.Todam);

            if (Math.Abs(basis - (cap + insp)) > Tolerance)
            {
                result.AddError(
                    $"در تسویه نقد/نسیه، مجموع نقدی ({cap:N0}) و نسیه ({insp:N0}) باید برابر " +
                    $"«مبلغ کل منهای مالیات و عوارض» یعنی {basis:N0} باشد " +
                    $"(Tbill={tbill:N0} − Tvam={header.Tvam:N0} − Todam={header.Todam:N0})، نه خود Tbill.");
            }

            // جدول ۲۵ ردیف ۳ و جدول ۲۶ ردیف ۳: هر دو باید بزرگتر از صفر باشند.
            if (cap <= 0)
                result.AddError("در تسویه نقد/نسیه، مبلغ پرداختی نقدی (Cap) باید بزرگتر از صفر باشد.");

            if (insp <= 0)
                result.AddError("در تسویه نقد/نسیه، مبلغ نسیه (Insp) باید بزرگتر از صفر باشد.");

            // جدول ۲۵ ردیف ۱ و جدول ۲۶ ردیف ۱: هر کدام باید از مجموع صورتحساب کوچکتر باشند.
            if (cap >= tbill)
                result.AddError($"مبلغ پرداختی نقدی ({cap:N0}) باید از مجموع صورتحساب ({tbill:N0}) کوچکتر باشد.");

            if (insp >= tbill)
                result.AddError($"مبلغ نسیه ({insp:N0}) باید از مجموع صورتحساب ({tbill:N0}) کوچکتر باشد.");
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