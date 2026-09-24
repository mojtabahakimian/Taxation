// ============================================================================
//  فقط برای ساخت لینوکسی هارنس. هرگز همراه برنامه منتشر نشود.
// ============================================================================
//
//  مشکل: سازندهٔ CL_CCNNMANAGER فایل C:\correct\CNR.udl را می‌خواند و همان‌جا
//  به SQL Server وصل می‌شود. CL_MOADIAN آن را در سازندهٔ استاتیک می‌سازد، پس
//  اولین دسترسی به هر عضو استاتیک CL_MOADIAN (مثلاً در گروه ۲۰ که «آفلاین» است)
//  با TypeInitializationException کل پروسهٔ هارنس را می‌کشد.
//
//  راه‌حل (فقط در این ساخت، فقط در زمان اجرا، بدون تغییر سورس):
//  اگر و فقط اگر C:\correct\CNR.udl خواندنی نباشد، سازندهٔ CL_CCNNMANAGER با
//  Harmony جایگزین می‌شود: رشتهٔ اتصال را روی یک آدرس ناموجود می‌گذارد و به
//  دیتابیس وصل نمی‌شود. هر کوئری واقعی بعدی همچنان با SqlException شکست
//  می‌خورد — یعنی رفتار «دیتابیس در دسترس نیست»، نه «دیتابیس ساختگی».
//
//  خاموش کردن:  MOADIAN_SHIM_NO_DB_STUB=0

using System.Runtime.CompilerServices;
using HarmonyLib;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.FUNCTIONS;

namespace TaxCollectData.Library.ShimHost
{
    internal static class NoDbStub
    {
        private const string UdlPath = @"C:\correct\CNR.udl";

        internal const string DeadConnection =
            "Data Source=127.0.0.1,1;Initial Catalog=NO_DATABASE_ON_LINUX;User ID=none;Password=none;" +
            "Connect Timeout=1;TrustServerCertificate=True;Encrypt=False";

        [ModuleInitializer]
        internal static void Install()
        {
            if (Environment.GetEnvironmentVariable("MOADIAN_SHIM_NO_DB_STUB") == "0") return;
            try { using var _ = File.OpenRead(UdlPath); return; } catch { /* نیست → وصله */ }

            var ctor = AccessTools.Constructor(typeof(CL_CCNNMANAGER), Type.EmptyTypes);
            new Harmony("moadian.linux.shim.nodb").Patch(ctor,
                prefix: new HarmonyMethod(typeof(NoDbStub), nameof(CtorPrefix)));
            Console.Error.WriteLine("[shim] C:\\correct\\CNR.udl نیست — سازندهٔ CL_CCNNMANAGER بدون اتصال به دیتابیس اجرا می‌شود (NoDbStub).");
        }

        private static bool CtorPrefix(CL_CCNNMANAGER __instance)
        {
            // مقداردهی فیلدهای نمونه (در IL بخشی از همین سازنده است)
            __instance.TheFunctions = new CL_FUNTIONS();
            CL_CCNNMANAGER.CONNECTION_STR = DeadConnection;
            return false; // بدنهٔ اصلی (خواندن udl و SELECT GETDATE()) اجرا نشود
        }
    }
}
