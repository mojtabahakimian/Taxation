using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.Generaly;

namespace Prg_Moadian.FUNCTIONS
{
    /// <summary>
    /// سابقهٔ هر هشدار و پاسخ کاربر، در دیتابیس (جدول <c>MOADIAN_WARNLOG</c>).
    ///
    /// چرا: سیاست این برنامه «هشدار بده، جلو را نگیر، ثبت کن» است (سامانهٔ مؤدیان ناپایدار است و
    /// قاعده‌هایش عوض می‌شود). ولی تا ۱۴۰۵/۰۸/۲۶ پاسخ کاربر به هشدارها هیچ‌جا ثبت نمی‌شد. مشتریِ
    /// حوالهٔ ۱۰۲۴۲ یزدسپار گفت هشداری نگرفته و راهی برای بررسی ادعا نبود (این بار حق با او بود: آن
    /// حالت هشداری نداشت). حالا هر جا کاربر به «ادامه می‌دهید؟» جواب می‌دهد، یا برنامه بی‌پرسش ادامه
    /// می‌دهد، یک ردیف ثبت می‌شود.
    ///
    /// ثبت هرگز نباید جلوی ارسال را بگیرد: هر خطا بلعیده و فقط در فایل لاگ نوشته می‌شود.
    /// جدول در اولین نوشتن ساخته می‌شود.
    /// </summary>
    public static class WarningLog
    {
        public const string Continued = "ادامه";
        public const string Cancelled = "لغو";
        public const string Excluded = "کنار گذاشته شد";
        public const string CancelledAll = "لغو کل ارسال";
        public const string NoPrompt = "بی‌پرسش، ادامه";

        public const string SourceSingle = "تکی (MOADIAN.EXE)";
        public const string SourceBulk = "گروهی";
        public const string SourceSafirSingle = "سفیر، تکی";
        public const string SourceSafirBulk = "سفیر، گروهی";
        public const string SourceTracking = "پنجرهٔ پیگیری";
        public const string SourceCorrection = "فرم اصلاحی/ابطالی";

        /// <summary>کاربر برنامه (اگر میزبان می‌داند، مثل ورود Prg_Grpsend). کاربر ویندوز جدا ثبت می‌شود.</summary>
        public static string? AppUser { get; set; }

        private static volatile bool _tableReady;

        internal const string CreateTableSql = @"
IF OBJECT_ID(N'dbo.MOADIAN_WARNLOG', N'U') IS NULL
CREATE TABLE dbo.MOADIAN_WARNLOG (
    ID       int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    CRT      datetime       NOT NULL DEFAULT (GETDATE()),
    NUMBER   float          NULL,
    TAG      float          NULL,
    Source   nvarchar(50)   NULL,
    Kind     nvarchar(100)  NULL,
    Decision nvarchar(50)   NOT NULL,
    Message  nvarchar(4000) NULL,
    Env      nvarchar(20)   NULL,
    AppUser  nvarchar(100)  NULL,
    WinUser  nvarchar(100)  NULL,
    Machine  nvarchar(100)  NULL
)";

        /// <param name="mainSystem">true سامانهٔ اصلی، false سندباکس، null نامعلوم.</param>
        public static void Write(string source, double? number, double? tag, string? kind, string? message,
                                 string decision, bool? mainSystem)
        {
            try
            {
                var db = new CL_CCNNMANAGER();
                if (!_tableReady)
                {
                    db.DoExecuteSQL(CreateTableSql);
                    _tableReady = true;
                }
                db.DoExecuteSQL(@"INSERT INTO dbo.MOADIAN_WARNLOG (NUMBER, TAG, Source, Kind, Decision, Message, Env, AppUser, WinUser, Machine)
                                  VALUES (@NUMBER, @TAG, @Source, @Kind, @Decision, @Message, @Env, @AppUser, @WinUser, @Machine)",
                    new
                    {
                        NUMBER = number,
                        TAG = tag,
                        Source = Cut(source, 50),
                        Kind = Cut(kind, 100),
                        Decision = Cut(decision, 50) ?? "",
                        Message = Cut(message, 4000),
                        Env = mainSystem is null ? null : mainSystem.Value ? "اصلی" : "آزمایشی",
                        AppUser = Cut(AppUser, 100),
                        WinUser = Cut(Environment.UserName, 100),
                        Machine = Cut(Environment.MachineName, 100)
                    });
            }
            catch (Exception ex)
            {
                try { CL_Generaly.DoGetwriteAppenLog($"WarningLog failed ({ex.Message}): {source} #{number} {decision} | {message}"); }
                catch (Exception) { }
            }
        }

        /// <summary>آدرس سامانه را به «اصلی/آزمایشی» تبدیل می‌کند (هر آدرسی که «sandbox» دارد آزمایشی است).</summary>
        public static bool? IsMain(string? taxUrl) =>
            string.IsNullOrWhiteSpace(taxUrl) ? null : !taxUrl.Contains("sandbox", StringComparison.OrdinalIgnoreCase);

        private static string? Cut(string? s, int max) =>
            s is null ? null : (s.Length > max ? s.Substring(0, max) : s);
    }
}
