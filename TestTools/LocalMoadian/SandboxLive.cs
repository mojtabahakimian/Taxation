using Prg_Moadian.Bulk;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.FUNCTIONS;

namespace MoadianLocalTest;

/// <summary>
/// ارسال گروهی واقعی به **سندباکس** سامانه مؤدیان و بررسی اینکه نتیجهٔ هر فاکتور مال خودش
/// است (باگ جفت‌شدن با ترتیب، ۱۴۰۵/۰۷/۲۲). فقط با اجازهٔ صریح کاربر اجرا شود.
///
///   dotnet run -- --sandbox-live=8366,8388,129
///
/// قفل‌ها: آدرس ثابتِ سندباکس است، دیتابیس باید YAZDSEPAR1405 باشد، و همهٔ ردیف‌های ثبت‌شده
/// باید ApiTypeSent=0 باشند.
/// </summary>
internal static class SandboxLive
{
    private const string SandboxUrl = "https://sandboxrc.tax.gov.ir/req/api/";
    private const string ExpectedDb = "YAZDSEPAR1405";

    private sealed class Row
    {
        public double NUMBER { get; set; }
        public string? Taxid { get; set; }
        public string? UID { get; set; }
        public string? RefrenceNumber { get; set; }
        public string? TheStatus { get; set; }
        public bool? ApiTypeSent { get; set; }
        public int Items { get; set; }
        public double? Tob { get; set; }
        public string? Tinb { get; set; }
        public string? Vras { get; set; }
        public string? TheError { get; set; }
    }

    public static int Run(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var arg = args.First(a => a.StartsWith("--sandbox-live="));
        var nums = arg.Split('=')[1].Split(',').Select(long.Parse).ToList();

        if (!SandboxUrl.Contains("sandboxrc.tax.gov.ir")) { Console.WriteLine("آدرس سندباکس نیست — متوقف شد"); return 2; }
        var db = new CL_CCNNMANAGER();
        string dbName = db.DoGetDataSQL<string>("SELECT DB_NAME()").First();
        if (dbName != ExpectedDb) { Console.WriteLine($"دیتابیس {dbName} است، نه {ExpectedDb} — متوقف شد"); return 2; }

        DateTime since = db.DoGetDataSQL<DateTime>("SELECT GETDATE()").First();
        Console.WriteLine($"دیتابیس {dbName} | مقصد {SandboxUrl} | فاکتورها {string.Join(",", nums)}");

        var bulk = new SendInvoiceBulk(db, SandboxUrl)
        {
            OnValidationWarning = m => { Console.WriteLine("  [هشدار قبلاً ارسال‌شده] " + m.Replace("\n", " ")); return true; },
            OnValidationWarningsBatch = list =>
            {
                Console.WriteLine($"  [فهرست هشدارها] {list.Count} هشدار در {list.Select(w => w.Number).Distinct().Count()} فاکتور:");
                foreach (var w in list) Console.WriteLine($"     {w.Number} | {w.Kind} | {w.Message}");
                return new HashSet<long>();   // همه بروند، مثل «ادامه»
            },
        };

        var r = bulk.SendAsync(nums, 2, inty_value: 1, setm_value: 1).GetAwaiter().GetResult();
        Console.WriteLine($"ارسال: {r.Success} به صف رفت، {r.Failures.Count} خطای پیش از ارسال");
        foreach (var f in r.Failures) Console.WriteLine($"  ✗ {f.Key}: {f.Value}");

        string rowsSql = @"
            SELECT NUMBER, Taxid, UID, RefrenceNumber, TheStatus, ApiTypeSent, COUNT(*) Items,
                   MIN(Tob) Tob, MIN(Tinb) Tinb,
                   STRING_AGG(CAST(CAST(Vra AS int) AS varchar(3)), ',') WITHIN GROUP (ORDER BY CRT) Vras,
                   MIN(CAST(TheError AS nvarchar(2000))) TheError
            FROM dbo.TAXDTL WHERE CRT >= @S AND NUMBER IN @N AND Taxid IS NOT NULL
            GROUP BY NUMBER, Taxid, UID, RefrenceNumber, TheStatus, ApiTypeSent
            OPTION (MAXDOP 1)";
        var rows = db.DoGetDataSQL<Row>(rowsSql, new { S = since, N = nums }).ToList();

        if (rows.Any(x => x.ApiTypeSent != false))
        {
            Console.WriteLine("!!! ردیفی با ApiTypeSent غیر از سندباکس پیدا شد");
            return 3;
        }
        Console.WriteLine($"ثبت‌شده: {rows.Count} فاکتور، همه ApiTypeSent=0 (سندباکس)، UIDهای یکتا: {rows.Select(x => x.UID).Distinct().Count()}");

        // استعلام با همان مسیر برنامهٔ پیگیری (CL_ESTELAM)، چند بار تا نتیجه بیاید.
        var est = new CL_ESTELAM();
        est.GET_INIT_TAX(SandboxUrl);
        for (int attempt = 1; attempt <= 8; attempt++)
        {
            Thread.Sleep(TimeSpan.FromSeconds(10));
            var pending = rows.Where(x => !string.IsNullOrEmpty(x.RefrenceNumber)).Select(x => x.RefrenceNumber!).Distinct().ToList();
            foreach (var reference in pending)
            {
                try { est.GETESTELAM_REFCODE_UPDATE(reference); }
                catch (Exception e) { Console.WriteLine($"  استعلام {reference}: {e.Message}"); }
            }
            rows = db.DoGetDataSQL<Row>(rowsSql, new { S = since, N = nums }).ToList();
            int open = rows.Count(x => x.TheStatus is "PENDING" or "IN_PROGRESS" or null);
            Console.WriteLine($"  استعلام {attempt}: {open} هنوز در صف");
            if (open == 0) break;
        }

        Console.WriteLine();
        Console.WriteLine("نتیجه برای هر فاکتور (داده‌های خود فاکتور ← حکم سامانه):");
        foreach (var x in rows.OrderBy(x => x.NUMBER))
        {
            string err = string.IsNullOrEmpty(x.TheError) ? "" : " | " + x.TheError.Replace("\n", " ");
            Console.WriteLine($"  {x.NUMBER} | اقلام {x.Items} | نرخ‌ها [{x.Vras}] | tob {x.Tob} | tinb {x.Tinb} ({(x.Tinb ?? "").Length} رقم) | " +
                              $"{x.TheStatus} | taxid {x.Taxid} | uid {x.UID}{err}");
        }
        return 0;
    }
}
