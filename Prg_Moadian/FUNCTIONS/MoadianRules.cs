using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Prg_Moadian.FUNCTIONS
{
    /// <summary>
    /// قواعد مشترک سامانه مؤدیان که هر سه مسیر ارسال (تکی، گروهی، ارجاعی) باید یکسان
    /// رعایت کنند. مرجع: دستورالعمل صدور صورتحساب الکترونیکی V7.9 تیرماه ۱۴۰۵.
    /// </summary>
    public static class MoadianRules
    {
        #region مهلت ارسال (ماده ۹)

        /// <summary>
        /// مهلت مجاز از صدور تا ارسال، بر حسب روز.
        ///
        /// سند هیچ عدد ثابتی نمی‌دهد — جدول ۸۶ ص۹۳ و ص۲۵ همه‌جا می‌گویند «مهلت مجاز
        /// اعلام شده توسط سازمان امور مالیاتی کشور». پس این مقدار باید قابل تنظیم باشد
        /// و نباید در کد هاردکد شود.
        /// </summary>
        public static int SendDeadlineDays { get; set; } = 12;

        /// <summary>
        /// آیا این صورتحساب موضوع ماده ۹ است؟ ملاک، فاصلهٔ صدور تا ارسالِ *همین*
        /// صورتحساب است — نه سن صورتحساب مرجع (ص۲۵ ردیف‌های ۵ تا ۷).
        /// </summary>
        public static bool IsArticle9(DateTime issuedAt, DateTime sendingAt)
        {
            return (sendingAt - issuedAt).TotalDays > SendDeadlineDays;
        }

        /// <summary>
        /// آیا مسیر ماده ۹ به صورت خودکار فعال شود؟
        ///
        /// پیش‌فرض false و این عمدی است.
        ///
        /// مهلت ۱۲ روزه (از ۱۵ آبان ۱۴۰۳، کاهش از ۲۱ روز) قطعی است. ولی روشن
        /// کردن خودکار این کلید ماهیت حقوقی سند را عوض می‌کند: صورتحساب را
        /// «موضوع ماده ۹» اعلام می‌کند، که تبعات جریمه‌ای دارد. چنین تصمیمی
        /// نباید بی‌خبر و خودکار گرفته شود — باید هشدار بدهد و کاربر تأیید کند.
        ///
        /// ضمنا امروز اصلا فعال نمی‌شود: در دادهٔ عملیاتی هیچ صورتحساب
        /// پذیرفته‌شده‌ای بیش از ۱۲ روز فاصلهٔ صدور تا ثبت ندارد (بیشینه ۱۱ روز
        /// روی ۳٬۶۳۸ صورتحساب موفق). تست ۲۵-۶ همین را نگهبانی می‌کند و روزی که
        /// فاکتوری از مهلت رد شود قرمز خواهد شد.
        /// </summary>
        public static bool AutoArticle9 { get; set; } = false;

        /// <summary>
        /// مقدار فیلد insr. طبق جدول ۸۶ ص۹۳ ردیف ۲، داخل مهلت مجاز این فیلد «خارج از
        /// الگو» است؛ خارج از مهلت باید ۱ باشد. ولی چون مهلت واقعی قطعی نیست، تا
        /// فعال‌شدن <see cref="AutoArticle9"/> همیشه null برمی‌گردد.
        /// </summary>
        public static int? ResolveInsr(DateTime issuedAt, DateTime sendingAt)
        {
            if (!AutoArticle9) return null;
            return IsArticle9(issuedAt, sendingAt) ? 1 : (int?)null;
        }

        #endregion

        #region شناسه خریدار

        /// <summary>
        /// کنترل شناسه خریدار طبق جدول ۱۱ صفحات ۳۳ و ۳۴.
        ///
        /// ردیف ۲ : برای نوع دوم و سوم ثبت اطلاعات خریدار الزامی نیست.
        /// ردیف ۴ : نوع اول + خریدار حقیقی/اتباع غیرایرانی →
        ///          «شماره اقتصادی» یا «شماره ملی/کد فراگیر به همراه کد پستی».
        /// ردیف ۵ : نوع اول + خریدار حقوقی/مشارکت مدنی → شماره اقتصادی.
        ///
        /// طول‌ها: V7.9 ص۳۳ برای tinb فقط می‌گوید «حسب مورد ۱۱ و ۱۴» و تفکیک به‌تفکیک
        ///        نوع شخص را نمی‌دهد؛ منبع آن تفکیک، FAQ ص۱۱ سؤال ۳-۱۲ است:
        ///        حقیقی ۱۴، حقوقی ۱۱، مشارکت مدنی ۱۱، اتباع غیرایرانی ۱۴.
        ///        bid (V7.9 ص۳۳): شماره ملی ۱۰، شناسه ملی ۱۱، مشارکت مدنی ۱۱، کد فراگیر ۱۲.
        /// </summary>
        public static bool ValidateBuyer(int inty, int inp, int tob, string tinb, string bid, string bpc, out string error)
        {
            error = null;

            // جدول ۱۱ ص۳۴ ردیف ۲ (متن کامل):
            //   «در صورتی که صورتحساب از نوع اول صادرات و بورس اوراق بهادار مبتنی بر
            //    کالا، نوع دوم و یا سوم باشد، ثبت اطلاعات مربوط به خریدار الزامی نیست.»
            // پس معافیت فقط «نوع دوم و سوم» نیست؛ نوع اول با الگوی صادرات یا بورس هم
            // معاف است. شماره الگوها طبق جدول ۹ ص۳۱: صادرات = ۷ ، بورس = ۱۱.
            if (inty != 1)
                return true;

            if (inp == 7 || inp == 11)
                return true;

            if (tob < 1 || tob > 4)
            {
                error = $"نوع شخص خریدار ({tob}) نامعتبر است. مقادیر مجاز: ۱ حقیقی، ۲ حقوقی، ۳ مشارکت مدنی، ۴ اتباع غیرایرانی.";
                return false;
            }

            bool isRealPerson = (tob == 1 || tob == 4);           // حقیقی یا اتباع غیرایرانی
            int expectedTinbLength = isRealPerson ? 14 : 11;      // ص۳۳

            bool hasTinb = !string.IsNullOrWhiteSpace(tinb);
            bool hasBid = !string.IsNullOrWhiteSpace(bid);
            bool hasBpc = !string.IsNullOrWhiteSpace(bpc);

            // --- مسیر اول: شماره اقتصادی خریدار ---
            if (hasTinb)
            {
                tinb = tinb.Trim();

                if (!IsAllDigits(tinb) || tinb.Length != expectedTinbLength)
                {
                    error = $"شماره اقتصادی خریدار ({tinb}) باید دقیقاً {expectedTinbLength} رقم عددی باشد " +
                            $"(نوع شخص: {TobName(tob)}). سامانه این فیلد را با الگوی ^\\d{{{expectedTinbLength}}}$ کنترل می‌کند.";
                    return false;
                }

                return true;
            }

            // --- مسیر دوم: فقط برای حقیقی/اتباع — شماره ملی یا کد فراگیر + کد پستی ---
            if (isRealPerson)
            {
                if (!hasBid)
                {
                    error = "برای خریدار حقیقی/اتباع غیرایرانی باید یا «شماره اقتصادی» ثبت شود یا «شماره ملی/کد فراگیر به همراه کد پستی».";
                    return false;
                }

                bid = bid.Trim();
                int expectedBidLength = (tob == 1) ? 10 : 12; // شماره ملی ۱۰ — کد فراگیر ۱۲

                if (!IsAllDigits(bid) || bid.Length != expectedBidLength)
                {
                    error = tob == 1
                        ? $"شماره ملی خریدار ({bid}) باید دقیقاً ۱۰ رقم عددی باشد."
                        : $"کد فراگیر اتباع غیرایرانی ({bid}) باید دقیقاً ۱۲ رقم عددی باشد.";
                    return false;
                }

                if (!hasBpc || !Regex.IsMatch(bpc.Trim(), @"^\d{10}$"))
                {
                    error = "در مسیر «شماره ملی/کد فراگیر»، ثبت کد پستی خریدار (۱۰ رقم) هم الزامی است — جدول ۱۱ ردیف ۴.";
                    return false;
                }

                return true;
            }

            // حقوقی و مشارکت مدنی: فقط مسیر شماره اقتصادی
            error = $"برای خریدار {TobName(tob)}، ثبت شماره اقتصادی ({expectedTinbLength} رقمی) الزامی است — جدول ۱۱ ردیف ۵.";
            return false;
        }

        /// <summary>
        /// طول مورد انتظار فیلد bid بر حسب نوع شخص (V7.9 ص۳۳):
        /// حقیقی ۱۰ (شماره ملی)، حقوقی ۱۱ (شناسه ملی)، مشارکت مدنی ۱۱، اتباع ۱۲ (کد فراگیر).
        /// </summary>
        public static int ExpectedBidLength(int tob)
        {
            switch (tob)
            {
                case 1: return 10;
                case 2: return 11;
                case 3: return 11;
                case 4: return 12;
                default: return 0;
            }
        }

        /// <summary>
        /// فیلد bid را فقط در صورتی برمی‌گرداند که با نوع شخص بخواند، وگرنه null.
        ///
        /// چرا: این فیلد اختیاری است و تا پیش از این اصلا ارسال نمی‌شد (۱۴۳۹۹ صورتحساب
        /// موفق، همه با bid خالی). فرستادنِ یک مقدار با طول اشتباه، صورتحسابی را که
        /// بدون این فیلد پذیرفته می‌شد رد می‌کند — خطای 0101104 با الگوی ^\d{N}$.
        /// نمونه واقعی: Tob=3 با bid ده‌رقمی «3621066764» در حالی که سامانه ۱۱ رقم
        /// می‌خواهد. پس مقدار نامعتبر به جای ارسال، حذف می‌شود.
        /// </summary>
        public static string SanitizeBid(int tob, string bid)
        {
            if (string.IsNullOrWhiteSpace(bid)) return null;

            bid = bid.Trim();

            var expected = ExpectedBidLength(tob);
            if (expected == 0) return null;

            if (!IsAllDigits(bid) || bid.Length != expected) return null;

            return bid;
        }

        public static string TobName(int tob)
        {
            switch (tob)
            {
                case 1: return "حقیقی";
                case 2: return "حقوقی";
                case 3: return "مشارکت مدنی";
                case 4: return "اتباع غیرایرانی";
                default: return $"نامعتبر({tob})";
            }
        }

        #endregion

        #region کد شعبه

        /// <summary>
        /// کد شعبه خریدار/فروشنده طول ۴ دارد (ص۳۳). سامانه با ^\d{4}$ کنترل می‌کند.
        /// مقدار خالی یا صفر باید null شود، نه اینکه ناقص ارسال گردد.
        /// </summary>
        public static string NormalizeBranchCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;

            code = code.Trim();

            if (!IsAllDigits(code)) return null;
            if (long.TryParse(code, out var v) && v <= 0) return null;

            // کوتاه‌تر از ۴ با صفر پر می‌شود؛ بلندتر از ۴ نامعتبر است و حذف می‌گردد.
            if (code.Length < 4) return code.PadLeft(4, '0');
            if (code.Length > 4) return null;

            return code;
        }

        #endregion

        #region تسویه

        /// <summary>
        /// مبنای تقسیم نقد/نسیه.
        ///
        /// جدول ۲۵ ص۴۶ : C  = Xs - W2 - W - Cr
        /// جدول ۲۶ ص۴۷ : Cr = Xs - W2 - W - C
        /// FAQ ص۳۱ س۱۰-۵ : cap = tbill - todam - tvam - insp
        ///
        /// یعنی cap + insp برابر مبلغ کل *منهای* مالیات و عوارض است، نه خود مبلغ کل.
        ///
        /// دقت: در جدول‌های ۲۵ و ۲۶ فقط ردیف ۱ («کوچکتر از مجموع صورتحساب») و ستون
        /// اجباری/اختیاری صریحاً مشروط به «روش تسویه نقدی/نسیه» هستند؛ ردیف ۲ (خود
        /// فرمول) و ردیف ۳ (بزرگتر از صفر) بدون قید نوشته شده‌اند. با این حال در عمل
        /// سامانه برای setm=1 و setm=2 مقدار برابر کل صورتحساب را می‌پذیرد، پس این
        /// فرمول فقط در حالت نقدی/نسیه اعمال می‌شود.
        /// </summary>
        public static decimal SettlementBase(decimal tbill, decimal tvam, decimal todam)
        {
            var b = tbill - tvam - todam;
            return b < 0 ? 0 : b;
        }

        #endregion

        #region فایل بازیابی

        /// <summary>
        /// وقتی صورتحساب به سامانه رفته ولی ثبت در دیتابیس شکست خورده، شماره
        /// مالیاتی و کد رهگیری نباید گم شوند. این تابع هرگز استثنا پرتاب نمی‌کند.
        /// </summary>
        /// <returns>مسیر فایل ساخته‌شده، یا null اگر نوشتن ممکن نشد.</returns>
        public static string WriteRecoveryFile(object payload)
        {
            try
            {
                if (!System.IO.Directory.Exists(RecoveryDirectory))
                    System.IO.Directory.CreateDirectory(RecoveryDirectory);

                string path = System.IO.Path.Combine(
                    RecoveryDirectory,
                    DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss-fff") + "-RECOVERY.json");

                System.IO.File.WriteAllText(
                    path,
                    System.Text.Json.JsonSerializer.Serialize(payload),
                    System.Text.Encoding.UTF8);

                return path;
            }
            catch
            {
                // بازیابی نباید خودش باعث خطای جدید شود.
                return null;
            }
        }

        /// <summary>پوشهٔ نگهداری فایل‌های بازیابی.</summary>
        public static string RecoveryDirectory { get; set; } = @"C:\CORRECT\RECOVERY";

        #endregion

        #region ارسال تکراری

        /// <summary>
        /// ارسال قبلیِ یک فاکتور که هنوز «زنده» است: رد نشده و ابطال هم نشده.
        /// </summary>
        public sealed class PriorSend
        {
            public double NUMBER { get; set; }
            public string Taxid { get; set; }
            public string TheStatus { get; set; }
            public DateTime? CRT { get; set; }
        }

        /// <summary>
        /// آیا ارسالی با این وضعیت ممکن است در کارپوشه نشسته باشد؟
        /// فقط رد صریح سامانه (FAILED) و خطای پیش از ارسال (LOCAL_ERROR) قطعاً چیزی
        /// در کارپوشه نگذاشته‌اند. PENDING و UNKNOWN و EXPIRED و وضعیت خالی
        /// ممکن است هنوز پذیرفته شوند.
        /// </summary>
        public static bool IsLiveStatus(string status)
        {
            var s = (status ?? "").Trim().ToUpperInvariant();
            return s != "FAILED" && s != "LOCAL_ERROR";
        }

        /// <summary>
        /// ارسال‌های زندهٔ قبلیِ این فاکتورها به‌عنوان صورتحساب اصلی (Ins=1).
        ///
        /// چرا: در یزدسپار ۸۸ فاکتور یک بار از ارسال گروهی و بعد دوباره (بیشتر از
        /// ارسال تکی) با شماره مالیاتی تازه رفتند و هر دو «موفق» شدند؛ یعنی دو بار
        /// در کارپوشه نشستند. هیچ مسیری پیش از ارسال این را نمی‌پرسید.
        ///
        /// ارسال‌هایی که ابطالیِ موفق دارند زنده حساب نمی‌شوند: فرستادن دوبارهٔ
        /// فاکتوری که ابطال شده، روال درست است.
        ///
        /// TAG می‌تواند NULL باشد (بخش ۴ CLAUDE.md)؛ آن ردیف‌ها هم دیده می‌شوند.
        /// </summary>
        public static List<PriorSend> FindLiveOriginals(
            CNNMANAGER.CL_CCNNMANAGER db, IEnumerable<long> numbers, int tag, bool isMainApi)
        {
            var list = numbers.Select(n => (double)n).Distinct().ToList();
            if (list.Count == 0) return new List<PriorSend>();

            const string sql = @"
                SELECT t.NUMBER, t.Taxid, MAX(t.TheStatus) AS TheStatus, MAX(t.CRT) AS CRT
                FROM dbo.TAXDTL t
                LEFT JOIN (SELECT DISTINCT Irtaxid FROM dbo.TAXDTL
                           WHERE Ins = 3 AND TheStatus = 'SUCCESS' AND Irtaxid IS NOT NULL) c
                       ON c.Irtaxid = t.Taxid
                WHERE t.NUMBER IN @Numbers
                  AND (t.TAG = @Tag OR t.TAG IS NULL)
                  AND t.ApiTypeSent = @Api
                  AND ISNULL(t.Ins, 1) = 1
                  AND ISNULL(t.Taxid, '') <> ''
                  AND c.Irtaxid IS NULL
                GROUP BY t.NUMBER, t.Taxid";

            var rows = new List<PriorSend>();
            // سقف پارامترهای SQL Server حدود ۲۱۰۰ است؛ فهرست‌های بزرگ تکه‌تکه می‌روند.
            foreach (var chunk in list.Chunk(1000))
                rows.AddRange(db.DoGetDataSQL<PriorSend>(sql, new { Numbers = chunk, Tag = (double)tag, Api = isMainApi }));

            return rows.Where(r => IsLiveStatus(r.TheStatus))
                       .OrderBy(r => r.NUMBER).ThenBy(r => r.CRT)
                       .ToList();
        }

        /// <summary>
        /// آیا این خطای ارسال یعنی «نمی‌دانیم صورتحساب به سامانه رسید یا نه»؟
        ///
        /// قطع شبکه، تایم‌اوت، یا پاسخِ بی‌نتیجه و بی‌خطا: درخواست ممکن است رسیده و
        /// ثبت شده باشد. در مقابل، رد صریح سامانه (با کد خطا) یا خطای پیش از ارسال
        /// قطعاً چیزی در کارپوشه نگذاشته است.
        /// </summary>
        public static bool IsOutcomeUnknown(Exception ex)
        {
            var pending = new Stack<Exception>();
            pending.Push(ex);
            while (pending.Count > 0)
            {
                var e = pending.Pop();
                if (e == null) continue;
                if (e is Service.MoadianOutcomeUnknownException
                      or System.Threading.Tasks.TaskCanceledException
                      or TimeoutException
                      or System.Net.Http.HttpRequestException
                      or System.IO.IOException
                      or System.Net.Sockets.SocketException
                      or System.Net.WebException)
                    return true;
                if (e is AggregateException agg)
                    foreach (var inner in agg.InnerExceptions) pending.Push(inner);
                pending.Push(e.InnerException);
            }
            return false;
        }

        /// <summary>متن هشدار برای ارسال دوبارهٔ فاکتوری که ارسال زنده دارد.</summary>
        public static string DescribePriorSends(IEnumerable<PriorSend> prior, int maxShown = 10)
        {
            var items = prior.ToList();
            var byInvoice = items.GroupBy(p => (long)p.NUMBER).ToList();
            var sb = new System.Text.StringBuilder();

            sb.AppendLine(byInvoice.Count == 1
                ? $"فاکتور {byInvoice[0].Key} قبلاً به سامانه ارسال شده و آن ارسال رد یا ابطال نشده است:"
                : $"{byInvoice.Count} فاکتور قبلاً به سامانه ارسال شده‌اند و آن ارسال‌ها رد یا ابطال نشده‌اند:");

            foreach (var g in byInvoice.Take(maxShown))
            {
                var last = g.Last();
                sb.AppendLine($"  • فاکتور {g.Key}: شماره مالیاتی {last.Taxid}، وضعیت {StatusName(last.TheStatus)}" +
                              (last.CRT.HasValue ? $"، زمان ارسال {last.CRT:yyyy/MM/dd HH:mm}" : ""));
            }
            if (byInvoice.Count > maxShown)
                sb.AppendLine($"  • و {byInvoice.Count - maxShown} فاکتور دیگر");

            sb.AppendLine();
            sb.AppendLine("ارسال دوباره یک صورتحساب اصلیِ تازه با شماره مالیاتی جدید می‌سازد. اگر هر دو پذیرفته شوند، " +
                          "فاکتور دو بار در کارپوشه ثبت می‌شود و یکی باید ابطال شود.");
            sb.Append("اگر ارسال قبلی هنوز «در صف» است، به‌جای این کار در برنامهٔ پیگیری استعلام بگیرید " +
                      "یا «ارسال مجدد» را بزنید که همان شماره مالیاتی را می‌فرستد.");
            return sb.ToString();
        }

        private static string StatusName(string status) =>
            (status ?? "").Trim().ToUpperInvariant() switch
            {
                "SUCCESS" => "موفق",
                "PENDING" or "IN_PROGRESS" => "در صف سامانه",
                "UNKNOWN" => "نامعلوم (پاسخ سامانه نرسید)",
                "EXPIRED" => "بی‌پاسخ ماند (منقضی)",
                "" or "NULL" => "نامشخص",
                var s => s
            };

        #endregion

        internal static bool IsAllDigits(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var c in s)
                if (c < '0' || c > '9') return false;
            return true;
        }
    }
}
