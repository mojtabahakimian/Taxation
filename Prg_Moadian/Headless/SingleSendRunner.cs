using System.Runtime.CompilerServices;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.FUNCTIONS;
using Prg_Moadian.Generaly;
using Prg_Moadian.SQLMODELS;
using static Prg_Moadian.Generaly.CL_Generaly;

namespace Prg_Moadian.Headless
{
    /// <summary>
    /// ارسال تکی بدون پنجره — همان کاری که Prg_Graphicy/Payewin.xaml.cs (یعنی
    /// MOADIAN.EXE) می‌کند، برای برنامه‌ای که پنجره ندارد (سرور سفیر).
    ///
    /// تفاوت‌ها با Payewin، همه عمدی:
    /// • رشتهٔ اتصال از میزبان می‌آید (CL_CCNNMANAGER.ConnectionOverride)، نه از CNR.udl.
    /// • هر «آیا ادامه می‌دهید؟» به Confirm داده می‌شود. اگر false برگرداند، اجرا
    ///   مثل «لغو» در پنجره متوقف می‌شود و متن پرسش در Result.Question برمی‌گردد تا
    ///   میزبان آن را به کاربر نشان دهد و با پاسخ «ادامه» دوباره صدا بزند.
    /// • حالت استاتیک هسته پیش از هر اجرا پاک می‌شود، چون پروسهٔ میزبان مثل
    ///   MOADIAN.EXE بعد از هر ارسال بسته نمی‌شود.
    /// • هسته پر از عضو استاتیک است؛ پس در هر لحظه فقط یک اجرا در پروسه.
    ///
    /// متن پرسش‌ها و ترتیب بررسی‌ها عین Payewin است. اگر آنجا عوض شد، اینجا هم.
    /// </summary>
    public static class SingleSendRunner
    {
        public const string MainUrl = "https://tp.tax.gov.ir/req/api/";
        public const string SandboxUrl = "https://sandboxrc.tax.gov.ir/req/api/";

        public sealed class Request
        {
            public string ConnectionString { get; set; } = "";
            public long Number { get; set; }
            /// <summary>تگ ارسال؛ برای فاکتور فروش MrCorrect همان ۲ (حواله).</summary>
            public int Tag { get; set; } = 2;
            /// <summary>true = سامانهٔ اصلی، false = سامانهٔ آزمایشی.</summary>
            public bool MainSystem { get; set; }
            /// <summary>«m» = MrCorrect (پیش‌فرض)، «d» = دناافراز.</summary>
            public string Caller { get; set; } = "m";
            /// <summary>فقط برای تست با سرور ساختگی؛ میزبان واقعی خالی می‌گذارد.</summary>
            public string? TaxUrlOverride { get; set; }
            /// <summary>هر پرسش «ادامه می‌دهید؟». null یعنی هیچ پرسشی تأیید نشود.</summary>
            public Func<string, bool>? Confirm { get; set; }
        }

        public sealed class Result
        {
            /// <summary>صورتحساب به سامانه رفت و کد رهگیری گرفت.</summary>
            public bool Sent { get; set; }
            /// <summary>اجرا روی این پرسش ایستاد؛ با تأییدش دوباره صدا بزنید.</summary>
            public string? Question { get; set; }
            /// <summary>خطای متوقف‌کننده، به زبان کاربر.</summary>
            public string? Error { get; set; }
            /// <summary>خطاها یا پیام‌های جزئی (کالای بی‌شناسه، پیام‌های استعلام).</summary>
            public List<string> Messages { get; } = new List<string>();
            public string? TaxId { get; set; }
            public string? ReferenceNumber { get; set; }
            /// <summary>آخرین وضعیت استعلام همان لحظه (SUCCESS / PENDING / FAILED / …).</summary>
            public string? Status { get; set; }
        }

        public static Result Run(Request request)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.ConnectionString))
                return new Result { Error = "رشتهٔ اتصال دیتابیس خالی است." };

            // رشته‌ی اتصال پیش از هر دسترسی به CL_MOADIAN گذاشته می‌شود: مقداردهی استاتیکِ آن یک
            // CL_CCNNMANAGER می‌سازد و بدونِ این سراغِ CNR.udl می‌رفت.
            using (HeadlessGate.Enter(request.ConnectionString))
                return RunCore(request);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Result RunCore(Request request)
        {
            var result = new Result();
            string? unanswered = null;

            bool Ask(string question)
            {
                if (request.Confirm != null && request.Confirm(question)) return true;
                unanswered ??= question;
                return false;
            }

            string? savedUrl = CL_MOADIAN.TaxURL;
            string savedMode = CL_Generaly.MrCorrect;
            CL_CCNNMANAGER dbms;
            try
            {
                dbms = new CL_CCNNMANAGER();
                CL_MOADIAN.dbms = dbms;
                dbms.DoGetDataSQL<string>("SELECT TOP 1 YEA FROM dbo.SAZMAN").FirstOrDefault();
            }
            catch (Exception)
            {
                result.Error = "خطا در ارتباط با دیتابیس";
                return result;
            }

            try
            {
                ResetState();
                CL_MOADIAN.TaxURL = request.TaxUrlOverride
                                    ?? (request.MainSystem ? MainUrl : SandboxUrl);

                CL_ScriptUpdateDB.Go(); // مثل Payewin: ستون‌ها و جدول‌های TAXDTL به‌روز شوند

                long number = request.Number;
                int tag = request.Tag;
                string factorNum = number.ToString().TrimStart('0');

                var head = dbms.DoGetDataSQL<HEAD_LST_EXTENDED>(
                    $"SELECT * FROM dbo.HEAD_LST_EXTENDED WHERE NUMBER = {number} AND TGU = {tag}").FirstOrDefault();
                if (head?.inty is null)
                    throw new NullyExceptiony("HEAD_EXTENDED is null");

                if (CL_MOADIAN.TaxURL == MainUrl && !PriorSendsConfirmed(dbms, head, number, tag, factorNum, Ask))
                {
                    result.Question = unanswered;
                    return result;
                }

                CL_MOADIAN.OnValidationWarning = Ask;
                CL_MOADIAN.DoSendInvoice(new[] { $"{number}_{tag}_{request.Caller}" });
            }
            catch (Exception er)
            {
                if (unanswered != null)
                {
                    // «لغو» روی یک پرسش؛ خطای «ارسال لغو شد» هسته فقط پیامد همان است.
                    result.Question = unanswered;
                    return result;
                }
                result.Error = new CustomExceptErMsg().ExpecMsgEr(er)
                               ?? "خطا در انجام عملیات , ادامه عملیات فعلا ممکن نیست.";
            }
            finally
            {
                CL_MOADIAN.OnValidationWarning = null;
                CL_MOADIAN.TaxURL = savedUrl;
                CL_Generaly.MrCorrect = savedMode;
            }

            // همان فهرست Payewin در finally: کالای بدون واحد یا شناسهٔ مؤدیان.
            foreach (var item in CL_ERRLST.ERROR_BODY_LST)
            {
                if (item.MU is null) result.Messages.Add($"کالای شماره {item.CODE} واحد مودیان انتخاب نشده");
                if (item.SSTID is null) result.Messages.Add($"کالای شماره {item.CODE} شناسه کالا یا خدمات مودیان انتخاب نشده");
            }
            // خطای آغازین که هسته بی‌صدا با return رد می‌شود (کلید یا حافظهٔ مالیاتی).
            foreach (var e in CL_ERRLST.ERROR_MAIN_LST.Where(e => e.ER_HAPPENED))
                result.Messages.Add(e.ER_MSG ?? "");

            var distinct = result.Messages.Where(m => !string.IsNullOrWhiteSpace(m)).Distinct().ToList();
            result.Messages.Clear();
            result.Messages.AddRange(distinct);

            if (result.Error is null && result.Messages.Count == 0 &&
                !string.IsNullOrEmpty(FactorInfoSent.ReferenceNumber))
            {
                result.Sent = true;
                result.TaxId = FactorInfoSent.TaxID;
                result.ReferenceNumber = FactorInfoSent.ReferenceNumber;
                ReadInquiry(dbms, result);
            }
            else if (result.Error is null && result.Messages.Count == 0)
            {
                result.Error = "صورتحساب ارسال نشد و سامانه کد رهگیری نداد.";
            }
            return result;
        }

        /// <summary>
        /// بخش DUP_CHECK پنجرهٔ Payewin، فقط برای سامانهٔ اصلی. false یعنی کاربر
        /// یکی از پرسش‌ها را تأیید نکرد.
        /// </summary>
        private static bool PriorSendsConfirmed(CL_CCNNMANAGER dbms, HEAD_LST_EXTENDED head, long number, int tag,
                                                string factorNum, Func<string, bool> ask)
        {
            if (head.ins == 1) //اصلی
            {
                var sent = dbms.DoGetDataSQL<string>(
                    $"SELECT TheStatus FROM dbo.TAXDTL WHERE ApiTypeSent = 1 AND Ins = 1 AND TheStatus IN ('SUCCESS', 'PENDING', 'UNKNOWN') AND NUMBER = {number} AND TAG = {tag}  ").ToList();
                if (sent.Count > 0)
                {
                    if (sent.Contains("SUCCESS"))
                        return ask($"این صورت حساب از نوع اصلی (فروش) به شماره (حواله) {factorNum} قبلا ارسال و ثبت شده آیا مایلید مجددا ارسال کنید ؟");
                    if (sent.Contains("PENDING"))
                        return ask($"این صورت حساب اصلی (فروش) به شماره (حواله) {factorNum} قبلا ارسال شده اما وضعیت آن هنوز در انتظار (PENDING) است , آیا مایلید مجددا ارسال کنید ؟");
                    if (sent.Contains("UNKNOWN"))
                        return ask($"این صورت حساب اصلی (فروش) به شماره (حواله) {factorNum} قبلا ارسال شده اما پاسخ سامانه دریافت نشده و وضعیتش نامعلوم است. ممکن است در کارپوشه ثبت شده باشد. توصیه میشود ابتدا با شماره مالیاتی در کارپوشه جستجو کنید , آیا مایلید مجددا ارسال کنید ؟");
                }
                return true;
            }

            string kind, kindShort;
            switch (head.ins)
            {
                case 2: kind = "اصلاحی"; kindShort = "اصلاحی"; break;
                case 3: kind = "ابطالی"; kindShort = "ابطالی"; break;
                case 4: kind = "برگشتی"; kindShort = "برگشتی"; break;
                default: return true;
            }

            var prior = dbms.DoGetDataSQL<string>(
                $"SELECT TheStatus FROM dbo.TAXDTL WHERE ApiTypeSent = 1 AND Ins = {head.ins} AND NUMBER = {number} AND TAG = {tag}  ").ToList();

            // «وضیعت» در متن ابطالی و برگشتی Payewin همین‌طور است؛ متن را عوض نکرده‌ام.
            string pendingWord = head.ins == 2 ? "وضعیت" : "وضیعت";
            bool ok = true;
            if (prior.Contains("PENDING"))
                ok = ask($"این صورت حساب از نوع {kind} به شماره (حواله) {factorNum} قبلا ارسال شده , اما {pendingWord} آن هنوز *درانتظار* است, آیا میخواهید مجددا ارسال کنید ؟");
            else if (prior.Contains("SUCCESS"))
                ok = ask($"این صورت حساب از نوع {kind} به شماره (حواله) {factorNum} قبلا با موفقیت ارسال و در سامانه ثبت شده , آیا میخواهید مجددا ارسال کنید ؟");
            else if (prior.Contains("UNKNOWN"))
                ok = ask($"این صورت حساب {kindShort} به شماره (حواله) {factorNum} قبلا ارسال شده اما پاسخ سامانه دریافت نشده و وضعیتش نامعلوم است. ممکن است در کارپوشه ثبت شده باشد. توصیه میشود ابتدا با شماره مالیاتی در کارپوشه جستجو کنید , آیا مایلید مجددا ارسال کنید ؟");
            else if (prior.Contains("FAILED"))
                ok = ask($"این صورت حساب {kindShort} به شماره (حواله) {factorNum} قبلا ارسال شده اما وضعیت آن در ناموفق (FAILED) شده است , گاهی اوقات به دلیل اشکالات سامانه , میتواند صورت حسابی که ناموفق خورده نیز در کارپوشه ثبت شده باشد لزا پیشنهاد میشود قبل از ارسال , با شماره مالیاتی در کارپوشه جستجو کنید ,آیا مایلید مجددا ارسال کنید ؟");
            if (!ok) return false;

            if (string.IsNullOrEmpty(head.irtaxid))
                throw new NullyExceptiony("irtaxid is null");
            return true;
        }

        /// <summary>«JustTryToEstelam» پنجرهٔ Payewin: نتیجهٔ استعلامی که هسته بلافاصله گرفته.</summary>
        private static void ReadInquiry(CL_CCNNMANAGER dbms, Result result)
        {
            try
            {
                var rows = dbms.DoGetDataSQL<InquiryRow>(
                    "SELECT TheError, TheStatus FROM dbo.TAXDTL WHERE RefrenceNumber = @r",
                    new { r = result.ReferenceNumber }).ToList();
                foreach (var row in rows)
                {
                    if (!string.IsNullOrWhiteSpace(row.TheStatus)) result.Status = row.TheStatus;
                    if (!string.IsNullOrWhiteSpace(row.TheError) && row.TheError != "NULL")
                    {
                        foreach (var m in new CL_FUNTIONS().GetNormilizedMsg(row.TheError))
                            if (!string.IsNullOrWhiteSpace(m.MessageText_U)) result.Messages.Add(m.MessageText_U!);
                    }
                }
            }
            catch (Exception) { /* مثل Payewin: استعلام فقط تلاش است */ }
        }

        private sealed class InquiryRow
        {
            public string? TheError { get; set; }
            public string? TheStatus { get; set; }
        }

        /// <summary>هر چیز استاتیکی که در MOADIAN.EXE با بسته شدن پروسه پاک می‌شد.</summary>
        internal static void ResetState()
        {
            CL_MOADIAN.ResetState();
            CL_MOADIAN.IDD_OF_TAXDTL = -1;
            CL_MOADIAN.OnValidationWarning = null;
            CL_ERRLST.ERROR_BODY_LST.Clear();
            foreach (var e in CL_ERRLST.ERROR_MAIN_LST) e.ER_HAPPENED = false;
            FactorInfoSent.TaxID = null!;
            FactorInfoSent.ReferenceNumber = null!;
            FactorInfoSent.NUMBER = null!;
        }
    }
}
