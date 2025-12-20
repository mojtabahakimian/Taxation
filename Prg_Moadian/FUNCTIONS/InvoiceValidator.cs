using Prg_Moadian.Generaly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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

            public void AddError(string msg) => Errors.Add(msg); // آیکون در نمایشگر اضافه می‌شود
            public void AddWarning(string msg) => Warnings.Add(msg);
        }

        // تلورانس مجاز برای اختلافات گرد کردن (ریال)
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
                result.AddError("خطا: اقلام صورتحساب یافت نشد.");
                return result;
            }

            // 1. بررسی شناسه‌ها و کدهای اقتصادی
            ValidateIdentities(header, result);

            // 2. بررسی تاریخ‌ها
            ValidateDates(header, result);

            // 3. بررسی محاسبات سطری (بدنه)
            ValidateLineItems(bodies, result);

            // 4. بررسی سرجمع‌ها (هدر با مجموع بدنه)
            ValidateTotals(header, bodies, result);

            // 5. بررسی روش تسویه
            ValidatePayment(header, result);

            return result;
        }

        private static void ValidateLineItems(List<InvoiceModel.Body> bodies, ValidationResult result)
        {
            foreach (var item in bodies)
            {
                decimal line_Prdis = item.Am * item.Fee;
                if (Math.Abs(line_Prdis - item.Prdis) > Tolerance)
                {
                    result.AddError($"ردیف {item.Sstid}: حاصلضرب مقدار ({item.Am}) در فی ({item.Fee:N0}) برابر {line_Prdis:N0} می‌شود اما مبلغ قبل از تخفیف {item.Prdis:N0} درج شده است.");
                }

                decimal line_Adis = item.Prdis - item.Dis;
                if (Math.Abs(line_Adis - item.Adis) > Tolerance)
                {
                    result.AddError($"ردیف {item.Sstid}: تفاضل مبلغ قبل از تخفیف و تخفیف ({line_Adis:N0}) با مبلغ بعد از تخفیف ({item.Adis:N0}) مغایرت دارد.");
                }

                // محاسبه مالیات طبق استاندارد سامانه مودیان (Truncate)
                decimal line_Vam = Math.Truncate(item.Adis * item.Vra / 100);

                if (Math.Abs(line_Vam - item.Vam) > Tolerance)
                {
                    result.AddError($"ردیف {item.Sstid}: مبلغ مالیات (Vam) با نرخ ({item.Vra}%) مغایرت دارد. محاسباتی: {line_Vam:N0}, ارسالی: {item.Vam:N0}");
                }

                // بررسی جمع کل ردیف (Tsstam)
                // Tsstam = Adis + Vam + (Odam + Olam + Consfee...)
                decimal otherTaxLine = (item.Odam) + (item.Olam) + (item.Consfee);
                decimal line_Tsstam = item.Adis + item.Vam + otherTaxLine;

                if (Math.Abs(line_Tsstam - item.Tsstam) > Tolerance)
                {
                    result.AddError($"ردیف {item.Sstid}: جمع کل ردیف (Tsstam: {item.Tsstam:N0}) با مجموع اجزا (مبلغ بعد تخفیف + مالیات + عوارض = {line_Tsstam:N0}) همخوانی ندارد.");
                }

                // بررسی شناسه کالا (جدید)
                if (string.IsNullOrWhiteSpace(item.Sstid))
                    result.AddError("شناسه کالا/خدمت در یکی از ردیف‌ها خالی است.");
                else if (item.Sstid.Length != 13)
                    result.AddError($"ردیف {item.Sstid}: شناسه کالا/خدمت ({item.Sstid}) باید دقیقاً 13 رقم باشد.");
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
                result.AddError($"مجموع مبلغ قبل از تخفیف در هدر ({header.Tprdis:N0}) با جمع ردیف‌ها ({sumPrdis:N0}) مغایرت دارد.");

            if (Math.Abs(header.Tdis - sumDis) > Tolerance)
                result.AddError($"مجموع تخفیف در هدر ({header.Tdis:N0}) با جمع ردیف‌ها ({sumDis:N0}) مغایرت دارد.");

            if (Math.Abs(header.Tadis - sumAdis) > Tolerance)
                result.AddError($"مجموع مبلغ بعد از تخفیف در هدر ({header.Tadis:N0}) با جمع ردیف‌ها ({sumAdis:N0}) مغایرت دارد.");

            if (Math.Abs(header.Tvam - sumVam) > Tolerance)
                result.AddError($"مجموع مالیات در هدر ({header.Tvam:N0}) با جمع ردیف‌ها ({sumVam:N0}) مغایرت دارد.");

            // بررسی Tbill
            decimal tbill_diff1 = Math.Abs(sumTsstam - header.Tbill);
            decimal tbill_diff2 = Math.Abs((sumTsstam + header.Todam + header.Tax17) - header.Tbill);

            if (tbill_diff1 > Tolerance && tbill_diff2 > Tolerance)
            {
                result.AddError($"مجموع صورتحساب (Tbill: {header.Tbill:N0}) با جمع ردیف‌ها همخوانی ندارد.");
            }
        }

        private static void ValidatePayment(InvoiceModel.Header header, ValidationResult result)
        {
            decimal tbill = header.Tbill;
            decimal cap = header.Cap; // نقد
            decimal insp = header.Insp; // نسیه

            // اگر روش تسویه نقدی است (1)
            if (header.Setm == 1)
            {
                if (Math.Abs(tbill - cap) > Tolerance)
                {
                    result.AddWarning($"در روش تسویه نقدی (Setm=1)، مبلغ پرداختی نقدی ({cap:N0}) باید برابر با کل صورتحساب ({tbill:N0}) باشد.");
                }

                if (insp > 0)
                {
                    result.AddWarning("در روش تسویه نقدی، مبلغ نسیه باید صفر باشد.");
                }
            }
            // اگر نسیه است (2)
            else if (header.Setm == 2)
            {
                if (Math.Abs(tbill - insp) > Tolerance)
                {
                    result.AddWarning($"در روش تسویه نسیه (Setm=2)، مبلغ نسیه ({insp:N0}) باید برابر با کل صورتحساب ({tbill:N0}) باشد.");
                }

                if (cap > 0)
                {
                    result.AddWarning("در روش تسویه نسیه، مبلغ نقدی باید صفر باشد.");
                }
            }
            // اگر نقد و نسیه است (3)
            else if (header.Setm == 3)
            {
                if (Math.Abs(tbill - (cap + insp)) > Tolerance)
                {
                    result.AddWarning($"مجموع نقد ({cap:N0}) و نسیه ({insp:N0}) با مبلغ کل صورتحساب ({tbill:N0}) برابر نیست.");
                }
            }
        }

        private static void ValidateDates(InvoiceModel.Header header, ValidationResult result)
        {
            DateTime indatim = DateTimeOffset.FromUnixTimeMilliseconds(header.Indatim).DateTime;

            // الف) بررسی تاریخ آینده (Error 2002)
            DateTime serverNow = DateTime.UtcNow + CL_Generaly.TokenLifeTime.ServerClockSkew;
            if (CL_Generaly.TokenLifeTime.ServerClockSkew == TimeSpan.Zero)
            {
                serverNow = DateTime.UtcNow;
            }

            if (indatim > serverNow.AddMinutes(5))
            {
                result.AddError($"خطای مهم (2002): تاریخ صدور ({indatim.ToLocalTime()}) در آینده است! (زمان سرور: {serverNow.ToLocalTime()})");
            }

            // ب) بررسی فاصله 21 روز
            var daysDiff = (serverNow - indatim).TotalDays;
            if (daysDiff > 22)
            {
                result.AddWarning($"تاریخ صدور ({indatim.ToLocalTime()}) بیش از 21 روز با زمان حال فاصله دارد ({daysDiff:F0} روز). ممکن است سامانه خطا بگیرد.");
            }
        }

        private static void ValidateIdentities(InvoiceModel.Header header, ValidationResult result)
        {
            // بررسی شماره منحصر به فرد مالیاتی
            if (string.IsNullOrWhiteSpace(header.Taxid))
                result.AddError("شماره منحصر به فرد مالیاتی (Taxid) خالی است.");
            else if (header.Taxid.Length != 22)
                result.AddError($"طول شماره مالیاتی باید 22 کاراکتر باشد. طول فعلی: {header.Taxid.Length}");

            if (string.IsNullOrWhiteSpace(header.Inno))
                result.AddError("سریال داخلی صورتحساب (Inno) خالی است.");

            // بررسی شناسه اقتصادی فروشنده
            if (!string.IsNullOrEmpty(header.Tins))
            {
                if (header.Tins.Length == 11) { } // OK
                else if (header.Tins.Length == 14) { } // OK
                else if (header.Tins.Length == 10 && !IsValidNationalCode(header.Tins))
                {
                    result.AddError($"شناسه اقتصادی فروشنده (Tins: {header.Tins}) نامعتبر است.");
                }
                else if (header.Tins.Length != 10 && header.Tins.Length != 11 && header.Tins.Length != 14)
                {
                    result.AddError($"طول شناسه اقتصادی فروشنده ({header.Tins}) استاندارد نیست.");
                }
            }

            // بررسی خریدار بر اساس نوع (Tob)
            if (header.Tob == 1) // حقیقی
            {
                if (string.IsNullOrWhiteSpace(header.Bid))
                {
                    result.AddError("برای خریدار حقیقی، شناسه ملی (Bid) الزامی است.");
                }
                else
                {
                    if (header.Bid.Length != 10)
                    {
                        result.AddWarning($"برای خریدار حقیقی، شناسه ملی (Bid) باید 10 رقم باشد. (مقدار فعلی: {header.Bid})");
                    }
                    else if (!IsValidNationalCode(header.Bid))
                    {
                        result.AddError($"شناسه ملی خریدار (Bid: {header.Bid}) نامعتبر است (الگوریتم ده رقمی).");
                    }
                }
            }
            else if (header.Tob == 2) // حقوقی
            {
                if (string.IsNullOrWhiteSpace(header.Tinb) && string.IsNullOrWhiteSpace(header.Bid))
                {
                    result.AddError("برای خریدار حقوقی، کد اقتصادی یا شناسه ملی الزامی است.");
                }

                // اگر Bid پر شده و 10 رقمی است، هشدار (چون حقوقی 11 رقمی است)
                if (!string.IsNullOrEmpty(header.Bid) && header.Bid.Length == 10)
                {
                    result.AddWarning("نوع خریدار حقوقی است اما شناسه ملی (Bid) 10 رقمی (فرمت حقیقی) وارد شده است.");
                }
            }

            // کد پستی
            if (!string.IsNullOrEmpty(header.Bpc) && !Regex.IsMatch(header.Bpc, @"^\d{10}$"))
            {
                result.AddWarning($"کد پستی خریدار ({header.Bpc}) فرمت صحیح 10 رقمی ندارد.");
            }
        }

        public static bool IsValidNationalCode(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return false;
            // بررسی طول و اینکه فقط عدد باشد
            if (!Regex.IsMatch(input, @"^\d{10}$")) return false;

            var check = Convert.ToInt32(input.Substring(9, 1));
            var sum = Enumerable.Range(0, 9)
                .Select(x => Convert.ToInt32(input.Substring(x, 1)) * (10 - x))
                .Sum() % 11;

            return (sum < 2 && check == sum) || (sum >= 2 && check + sum == 11);
        }
    }
}
