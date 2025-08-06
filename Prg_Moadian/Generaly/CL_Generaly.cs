using Prg_Moadian.FUNCTIONS;
using System.Text;

namespace Prg_Moadian.Generaly
{
    public static class CL_Generaly
    {
        public static string MrCorrect { get; set; } = "m";
        public static class FactorInfoSent
        {
            public static string TaxID { get; set; }
            public static string ReferenceNumber { get; set; }
            public static string NUMBER { get; set; }
        }
        public class ERMODEL
        {
            public byte IDCODE { get; set; }
            public string ErDescription { get; set; }
        }
        public static List<ERMODEL>? ERRORS_LST { get; set; }

        public static string[] ARG_PARAM { get; set; }

        public static int NUMBER { get; set; }
        public static byte TAG { get; set; }
        public static byte IDD_OF_TAXDTL { get; set; }

        public static class TokenLifeTime
        {
            public static DateTime ServerUtcTime { get; set; }
            public static TimeSpan ServerClockSkew { get; set; }
            public static DateTime ExpirationTokenTimeUtc { get; set; }
            public static bool IsExpired
            {
                get
                {
                    // 30 ثانیه ارفاق هم اضافه کنید:
                    var skewedNow = DateTime.UtcNow + ServerClockSkew + TimeSpan.FromSeconds(30);
                    return skewedNow > ExpirationTokenTimeUtc;
                }
            }
        }


        public static void DoGetwriteAppenLog(string text, string NAMFIL = null, string dirPath = null)
        {
            if (string.IsNullOrEmpty(dirPath)) dirPath = "C:\\CORRECT\\ERLG";
            if (!Directory.Exists(dirPath)) Directory.CreateDirectory(dirPath);

            if (NAMFIL is null) NAMFIL = "LOG1.txt";

            if (!File.Exists(dirPath + NAMFIL)) File.Create(dirPath + NAMFIL).Dispose();
            if (File.Exists(dirPath + NAMFIL) && File.ReadAllText(dirPath + NAMFIL) != "") text = Environment.NewLine + text;

            try
            {
                using (StreamWriter sw = File.AppendText(dirPath + NAMFIL))
                {
                    sw.Write(text);
                }
            }
            catch (Exception) { }
        }
        public static void WriteRecordText(string text)
        {
            try
            {
                const string PATH = @"C:\CORRECT\Sent_invoices.txt";
                using (StreamWriter writer = new StreamWriter(PATH, true))
                {
                    writer.WriteLine(text);
                }
            }
            catch { }
        }
        public static void DoWritePRGLOG(string message = null, Exception ex = null)
        {
            const string dirPath = @"C:\CORRECT\ERLG\";

            if (!Directory.Exists(dirPath))
                Directory.CreateDirectory(dirPath);

            // One file per calendar day; everything else is appended.
            string fileName = DateTime.Now.ToString("yyyy-MM-dd") + ".txt";
            string filePath = Path.Combine(dirPath, fileName);

            var sb = new StringBuilder();

            if (ex != null)
            {
                sb.AppendLine($"Message   : {ex.Message}");
                sb.AppendLine($"StackTrace: {ex.StackTrace}");
            }
            else
            {
                sb.AppendLine($"Message   : {message}");
            }

            sb.AppendLine($"Date      : {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine(new string('-', 80));

            // --- Option A: the simplest ---
            File.AppendAllText(filePath, sb.ToString());

            /*  // --- Option B: keep the StreamWriter pattern if you prefer ---
            using (var writer = new StreamWriter(filePath, append: true))
            {
                writer.Write(sb.ToString());
            }
            */
        }
    }
}
