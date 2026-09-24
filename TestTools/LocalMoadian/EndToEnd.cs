#if MOADIAN_LINUX_SHIM
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HarmonyLib;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MoadianLocalTest.Shim;
using Prg_Moadian.Bulk;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.FUNCTIONS;
using Prg_Moadian.Generaly;
using Prg_Moadian.Service;
using Prg_Moadian.SQLMODELS;

namespace MoadianLocalTest;

/// <summary>
/// گروه ۲۹ — «سر تا ته مسیرهای واقعی با دیتابیس ساختگی».
///
/// چهار مسیر ارسالِ واقعی روی لینوکس هرگز اجرا نمی‌شدند چون به SQL Server
/// می‌خوانند/می‌نویسند. این‌جا کد تولیدِ دست‌نخورده روی FakeDb (TestTools/SdkShim/FakeDb.cs)
/// و سرور کامل (moadian_mock_full.py) اجرا می‌شود:
///
///   a. گروهی  — SendInvoiceBulk.SendAsync با ~۱۵۰ فاکتور ساختگی
///   b. تکی    — CL_MOADIAN.DoSendInvoice و مقایسهٔ payload با گروهی (دوقلوی بی‌دیتابیسِ گروه ۲۲)
///   c. استعلام — CL_ESTELAM.GETESTELAM_REFCODE_UPDATE و استعلام خودکارِ مسیر تکی
///   d. ارجاعی — برگشتی/ابطالی/اصلاحی از مسیر تکی (HEAD_LST_EXTENDED.ins/irtaxid)
///   e. ارسال مجدد — متدهای RESEND_BTN_Click از سورس WPF با Roslyn پیوند زده می‌شوند
///   f. خطای انتقال — fault_* سرور کامل
///   g. پیشگوی مستقل مبالغ — oracle_amounts.py روی همهٔ payload ها
///
/// ⚠️ حدود صادقانه (CLAUDE.md بخش ۱۰):
///   • FakeDb منطق C# دور دیتابیس را می‌آزماید، نه خود SQL را. رشته‌های SQL فقط
///     روی ماشین توسعه‌دهنده با ‎-Full‎ روی SQL Server واقعی سنجیده می‌شوند.
///   • ردیف‌های DRVD_TBL خروجی ویو هستند که دستی ساخته شده‌اند؛ JOIN ها آزموده نمی‌شوند.
///   • سرور کامل را ما از روی اسناد نوشته‌ایم؛ «پذیرفته شد» یعنی با خوانش ما از سند می‌خواند.
///   • تأخیر ۱۰ ثانیه‌ای Thread.Sleep در DoSendInvoice در زمان اجرا صفر می‌شود (فقط انتظار؛
///     با MOADIAN_E2E_REAL_SLEEP=1 خاموش می‌شود).
///   • از WIN_MODIFYINVOICE هیچ بخشی اجرا نمی‌شود (UI و درج مستقیمش با SqlConnection خام است).
/// </summary>
internal static class EndToEnd
{
    private const string MemoryId = "A11216";
    private const string Seller = "11111111111";
    private const int SendTag = 2;     // حواله انبار فروش — TAXDTL.TAG
    private const int HeadTag = 13;    // سرگروه فاکتور فروش (MrCorrect)

    private static string _base = "";      // آدرس سرور کامل (برای مسیرهای کنترلی)
    private static string _sendUrl = "";   // آدرس ارسال (شامل «sandbox» تا همهٔ مسیرها ApiTypeSent=0 بنویسند)
    private static string _key = "";
    private static HttpClient _http = null!;
    private static Action<string, bool, string> _check = null!;
    private static Action<string> _line = null!;
    private static int _n;
    private static FakeDb _db = null!;
    private static readonly List<string> Warnings = new();
    private static long _dateN, _dateNYesterday;

    // دادهٔ ساختگی
    private static readonly List<Inv> All = new();
    private static readonly Dictionary<long, Inv> ByNumber = new();
    // taxid → ردیف‌های خام (برای پیشگو)؛ taxid های عمداً بد (از پیشگو کنار می‌روند)
    private static readonly Dictionary<string, List<L>> OracleSource = new();
    private static readonly HashSet<string> OracleExclude = new();

    public static void Run(string fullUrl, HttpClient http, string privateKey,
                           Action<string, bool, string> check, Action<string> line)
    {
        _base = fullUrl.TrimEnd('/') + "/";
        _sendUrl = _base + "sandbox/";
        _key = privateKey;
        _http = http;
        _check = check;
        _line = line;
        _n = 1;
        All.Clear(); ByNumber.Clear(); OracleSource.Clear(); OracleExclude.Clear(); Warnings.Clear();

        string savedCwd = Directory.GetCurrentDirectory();
        string tmp = Path.Combine(Path.GetTempPath(), "moadian-e2e-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmp);
        // کد تولید در C:\CORRECT\... لاگ می‌نویسد؛ روی لینوکس این یک مسیر نسبی است.
        Directory.SetCurrentDirectory(tmp);

        var savedUrl = CL_MOADIAN.TaxURL;
        var savedWarn = CL_MOADIAN.OnValidationWarning;
        var savedMode = CL_Generaly.MrCorrect;
        try
        {
            PatchSleep();
            _db = FakeDb.Activate();
            DefineSchema();
            BuildDataset();
            Post("__reset", new { });

            Section("a. مسیر گروهی (SendInvoiceBulk)", A_Bulk);
            Section("c. استعلام (CL_ESTELAM)", C_Inquiry);
            Section("b. مسیر تکی (CL_MOADIAN) و مقایسه با گروهی", B_Single);
            Section("d. ارجاعی از مسیر تکی: برگشتی ← ابطالی، اصلاحی", D_Chain);
            Section("e. ارسال مجدد (RESEND_BTN_Click پیوندزده از سورس WPF)", E_Resend);
            Section("g. پیشگوی مستقل مبالغ (oracle_amounts.py)", G_Oracle);
            Section("f. خطای انتقال (fault_*)", F_Faults);
            Report();
        }
        finally
        {
            FakeDb.Deactivate();
            CL_MOADIAN.TaxURL = savedUrl;
            CL_MOADIAN.OnValidationWarning = savedWarn;
            CL_Generaly.MrCorrect = savedMode;
            Directory.SetCurrentDirectory(savedCwd);
            try { Directory.Delete(tmp, recursive: true); } catch { /* فقط پاکسازی */ }
        }
    }

    private static void Section(string title, Action body)
    {
        _line("  — " + title);
        try { body(); }
        catch (Exception ex)
        {
            Check($"اجرای بخش «{title}» بدون استثنای پیش‌بینی‌نشده", false, One(ex));
            if (Environment.GetEnvironmentVariable("MOADIAN_E2E_DEBUG") == "1") _line(ex.ToString());
        }
    }

    // ================================================================ طرح و داده

    private sealed class L
    {
        public string Sstid = "", Mu = "1627", VNames = "عدد", Name = "";
        public decimal Am, Fee, Dis, Vra, Imbaa, MeghMar;
    }

    private sealed class Inv
    {
        public long Number;
        public int Inty = 1, Setm = 1, Tob = 2, Depart = 1;
        public string? Ecode, Mcodem;
        public decimal? Cap;
        public bool SeedHle, NullDepart;
        public string Product = "m";
        public string Kind = "good";    // good | bad-sstid | bad-mu | bad-tinb | bad-fee | bad-cap
        public List<L> Lines = new();
    }

    private sealed class FbkRow { public double? NUMBER { get; set; } public double? NUMBER1 { get; set; } public long? DATE_N { get; set; } }

    private static void DefineSchema()
    {
        _db.DefineTable("SAZMAN", typeof(SAZMAN));
        _db.DefineTable("DEPART", typeof(DEPART));
        _db.DefineTable("TCOD_VAHED_EXTENDED", typeof(TCOD_VAHED_EXTENDED), primaryKey: new[] { "IDD" });
        _db.DefineTable("HEAD_LST", typeof(HEAD_LST), exclude: new[] { "IsSelected" });
        _db.DefineTable("HEAD_LST_EXTENDED", typeof(HEAD_LST_EXTENDED), primaryKey: new[] { "NUMBER", "TGU" });
        _db.DefineTable("HEAD_LST_FBK", typeof(FbkRow));
        // طرح TAXDTL از FULL_TAXDTL؛ ستون‌های محاسبه‌ای/گرید و Bbc (در DDL نیست) کنار می‌روند.
        // پیش‌فرض CRT و NOT NULL ها از DDL در CL_ScriptUpdateDB.cs:18-106 آمده‌اند.
        var tax = _db.DefineTable("TAXDTL", typeof(FULL_TAXDTL), primaryKey: new[] { "IDD" },
                                  exclude: new[] { "ROWNUMBER", "PersianCRT", "NAME_VAHED", "Bbc" });
        tax.Defaults["CRT"] = () => DateTime.Now;
        foreach (var c in new[] { "Indatim", "Indati2m", "Tinb", "Mu", "IDD", "RefrenceNumber" }) tax.NotNull.Add(c);
        _db.DefineTable("DRVD_TBL", typeof(DRV_TBL),
                        overrides: new Dictionary<string, Type> { ["TAG"] = typeof(double?), ["PRODUCT"] = typeof(string) });
    }

    private static long PersianDate(DateTime d)
    {
        var pc = new PersianCalendar();
        return pc.GetYear(d) * 10000L + pc.GetMonth(d) * 100 + pc.GetDayOfMonth(d);
    }

    private static void BuildDataset()
    {
        var iran = DateTime.UtcNow.AddHours(3.5);
        _dateN = PersianDate(iran.AddDays(-2));
        _dateNYesterday = PersianDate(iran.AddDays(-1));
        string pem = "-----BEGIN PRIVATE KEY-----\r\n" + _key.Trim() + "\r\n-----END PRIVATE KEY-----\r\n";

        _db.Seed("SAZMAN", new
        {
            YEA = (short)1405, MEMORYID = MemoryId, MEMORYIDsand = MemoryId, PRIVIATEKEY = pem,
            ECODE = Seller, Dcertificate = "", MOADINA_SCNUM = 1m, NAME = "شرکت آزمایشی",
        });
        _db.Seed("DEPART", new { DEPATMAN = 1, DEPNAME = "مرکزی", BBC = "12", PCODE = "1234567890" });
        _db.Seed("DEPART", new { DEPATMAN = 2, DEPNAME = "شعبه", BBC = (string?)null, PCODE = "1234567891" });
        _db.Seed("TCOD_VAHED_EXTENDED", new { IDD = 1627, NAME_MO = "عدد" });
        _db.Seed("TCOD_VAHED_EXTENDED", new { IDD = 164, NAME_MO = "کیلوگرم" });

        var rnd = new Random(2029);
        int sst = 0;
        L NewLine(bool sentinel)
        {
            decimal[] ams = { 1m, 2m, 3m, 2.5m, 0.3333m, 1.75m, 12.125m, 7m, 0.6667m, 4.2m };
            var l = new L { Sstid = "2820000" + (100000 + (sst++ % 40)).ToString(CultureInfo.InvariantCulture), Name = "کالای آزمایشی " + sst };
            if (sentinel)
            {
                // ۵٫۷۵ × (۴k+۱) → کسر ۰٫۷۵ : برش و گرد (حتی بانکی) فرق دارند
                l.Am = 5.75m; l.Fee = 4 * rnd.Next(2_500, 200_000) + 1;
            }
            else
            {
                l.Am = ams[rnd.Next(ams.Length)]; l.Fee = rnd.Next(1_000, 900_000) | 1;
            }
            decimal prdis = Math.Truncate(l.Am * l.Fee);
            if (rnd.NextDouble() < 0.35) l.Dis = rnd.Next(1, (int)Math.Max(2, prdis / 10));
            double v = rnd.NextDouble();
            l.Vra = v < 0.7 ? 10m : v < 0.9 ? 9m : 0m;
            decimal vam = Math.Truncate((prdis - l.Dis) * l.Vra / 100m);
            // مالیاتِ ذخیره‌شده در دیتابیس گاهی کهنه است؛ کد تولید بازمحاسبه می‌کند
            l.Imbaa = l.Vra == 0 ? 0 : (rnd.NextDouble() < 0.15 ? vam + 1 : vam);
            return l;
        }
        Inv Make(long number, int inty, int setm, int tob, int lines, string kind = "good")
        {
            var inv = new Inv { Number = number, Inty = inty, Setm = setm, Tob = tob, Kind = kind, Depart = 1 + (int)(number % 2) };
            switch (tob)
            {
                case 1: inv.Ecode = "00123456790001"; break;
                case 2: inv.Ecode = "10100302746"; break;
                case 3: inv.Ecode = "10100302746"; break;
                case 4: inv.Ecode = "12345678901201"; break;
            }
            inv.Mcodem = tob == 1 ? "0012345679" : tob == 4 ? "123456789012" : "10100302746";
            for (int i = 0; i < lines; i++) inv.Lines.Add(NewLine(i == 0));
            return inv;
        }

        int k = 0;
        // A: نوع اول / نقدی — ۱۱۰ فاکتور، نیمی بالای یک میلیون (باند هگز سریال)
        for (long n = 7001; n <= 7055; n++) Add(Make(n, 1, 1, 1 + (k++ % 4), 1 + (int)(n % 4)));
        for (long n = 1_000_001; n <= 1_000_055; n++) Add(Make(n, 1, 1, 1 + (k++ % 4), 1 + (int)(n % 3)));
        // خریدار حقیقی بدون شماره اقتصادی: مسیر جایگزین «شماره ملی + کد پستی»
        foreach (var n in new long[] { 7005, 7021, 1_000_009 })
        {
            var inv = ByNumber[n]; inv.Tob = 1; inv.Ecode = null; inv.Mcodem = "0012345679";
        }
        // ذخیره برای IN_PROGRESS / داده خالی / ارسال همراه بدها
        for (long n = 7056; n <= 7058; n++) Add(Make(n, 1, 1, 2, 2));

        // بدها (کنار A)
        var b1 = Make(7901, 1, 1, 2, 2, "bad-sstid"); b1.Lines[1].Sstid = ""; Add(b1);
        var b2 = Make(7902, 1, 1, 2, 2, "bad-mu"); b2.Lines[1].Mu = ""; b2.Lines[1].VNames = ""; Add(b2);
        var b3 = Make(7903, 1, 1, 2, 1, "bad-tinb"); b3.Ecode = "1010030274612"; Add(b3);   // ۱۳ رقم برای حقوقی
        var b4 = Make(7904, 1, 1, 3, 2, "bad-fee"); b4.Lines[1].Fee = 0; b4.Lines[1].Dis = 0; Add(b4);

        // B: نوع اول / نسیه
        for (long n = 7101; n <= 7110; n++) Add(Make(n, 1, 2, 1 + (int)(n % 4), 2));
        // C: نوع اول / نقدی-نسیه — cap از HEAD_LST_EXTENDED
        for (long n = 7201; n <= 7208; n++) { var i = Make(n, 1, 3, 2, 2); i.SeedHle = true; i.Cap = 1000; Add(i); }
        var b5 = Make(7909, 1, 3, 2, 1, "bad-cap"); b5.SeedHle = true; b5.Cap = null; Add(b5);
        // D/E/F: نوع دوم
        for (long n = 7301; n <= 7308; n++) Add(Make(n, 2, 1, 1 + (int)(n % 4), 2));
        for (long n = 7401; n <= 7405; n++) Add(Make(n, 2, 2, 1 + (int)(n % 4), 1));
        for (long n = 7501; n <= 7505; n++) { var i = Make(n, 2, 3, 1, 2); i.SeedHle = true; i.Cap = 1000; Add(i); }
        // G: بدون انتخاب در UI (inty/setm از HEAD_LST_EXTENDED)
        for (long n = 7601; n <= 7605; n++) { var i = Make(n, 1, 1, 2, 2); i.SeedHle = true; Add(i); }
        // دناافراز (کوئری دیگر، بدون VNAMES)
        for (long n = 7701; n <= 7703; n++) { var i = Make(n, 2, 1, 2, 2); i.Product = "d"; Add(i); }
        // b: مقایسهٔ تکی/گروهی
        for (long n = 7801; n <= 7803; n++) { var i = Make(n, 1, 1, 1 + (int)(n % 4), 3); i.SeedHle = true; Add(i); }
        // d: زنجیرهٔ ارجاعی از مسیر تکی
        foreach (var n in new long[] { 7851, 7852 }) { var i = Make(n, 1, 1, 2, 2); i.SeedHle = true; Add(i); }
        // f: خطای انتقال
        for (long n = 7951; n <= 7953; n++) Add(Make(n, 1, 1, 2, 1));
        for (long n = 7961; n <= 7962; n++) Add(Make(n, 1, 1, 2, 1));
        { var i = Make(7971, 1, 1, 2, 1); i.SeedHle = true; Add(i); }
        Add(Make(7981, 1, 1, 2, 1));
        { var i = Make(7982, 1, 1, 2, 1, "bad-tinb"); i.Ecode = "1010030274612"; Add(i); }
        // یافته‌ها: HEAD_LST.DEPATMAN خالی؛ setm انتخاب‌شده بدون inty
        { var i = Make(7991, 1, 1, 2, 1); i.NullDepart = true; Add(i); }
        for (long n = 7992; n <= 7993; n++) Add(Make(n, 1, 1, 2, 1));

        foreach (var inv in All) SeedInvoice(inv);
    }

    private static void Add(Inv inv) { All.Add(inv); ByNumber[inv.Number] = inv; }

    private static void SeedInvoice(Inv inv)
    {
        // HEAD_LST: ردیف حواله (TAG=2) و در MrCorrect ردیف سرگروه (TAG=13)
        _db.Seed("HEAD_LST", new { NUMBER = (double)inv.Number, TAG = (double)SendTag, DEPATMAN = inv.NullDepart ? (int?)null : inv.Depart, DATE_N = _dateN, CUST_NO = "1-" + inv.Number });
        if (inv.Product == "m")
            _db.Seed("HEAD_LST", new { NUMBER = (double)inv.Number, TAG = (double)HeadTag, DEPATMAN = inv.Depart, DATE_N = _dateN, CUST_NO = "1-" + inv.Number });
        if (inv.SeedHle)
            _db.Seed("HEAD_LST_EXTENDED", new { NUMBER = (int)inv.Number, TGU = SendTag, inty = inv.Inty, inp = 1, ins = 1, setm = inv.Setm, cap = inv.Cap, todam = 0m });
        foreach (var l in inv.Lines) SeedLine(inv, l);
    }

    private static void SeedLine(Inv inv, L l)
    {
        decimal mablK = Math.Round(l.Am * l.Fee);   // مقدار ذخیرهٔ ERP (گردشده)؛ کد تولید بازمحاسبه می‌کند
        _db.Seed("DRVD_TBL", new
        {
            NUMBER = (double)inv.Number, TAG = (double)SendTag, DATE_N = _dateN, PRODUCT = inv.Product,
            CODE = "K" + l.Sstid, KALA = l.Name + " ", HESAB = "مشتری " + inv.Number,
            ECODE = inv.Ecode, MCODEM = inv.Mcodem, PCODE = "1234567890", tob = inv.Tob,
            MEGH = l.Am, MEGHk = l.Am, MABL = l.Fee, MABL_K = mablK, N_MOIN = l.Dis, IMBAA = l.Imbaa,
            mabkbt = mablK - l.Dis, mabkn = mablK - l.Dis + l.Imbaa,
            VNAMES = inv.Product == "m" ? l.VNames : null, sstid = l.Sstid, mu = l.Mu, vra = l.Vra,
            MEGH_MAR = l.MeghMar, DEPART = inv.Depart.ToString(CultureInfo.InvariantCulture),
        });
    }

    // ================================================================ a. گروهی

    private static List<int> _packetsA = new();

    private static void A_Bulk()
    {
        var aGood = All.Where(i => i.Setm == 1 && i.Inty == 1 && i.Kind == "good" && !i.SeedHle &&
                                   (i.Number is >= 7001 and <= 7055 or >= 1_000_001 and <= 1_000_055)).ToList();
        var aBad = new[] { 7901L, 7902, 7903, 7904 };

        // --- ۱) اجرای اصلی: بدها با پاسخ «لغو» به هشدار کنار می‌روند
        Warnings.Clear();
        int statsBefore = Stats()?["packets_per_send_request"]?.AsArray().Count ?? 0;
        long mark = _db.Mark();
        var bulk = NewBulk(_ => false);
        var sw = Stopwatch.StartNew();
        var res = bulk.SendAsync(aGood.Select(i => i.Number).Concat(aBad), SendTag, 1, 1).GetAwaiter().GetResult();
        sw.Stop();
        _line($"      · {aGood.Count + aBad.Length} فاکتور در {sw.Elapsed.TotalSeconds:F1} ثانیه");

        var packets = (Stats()?["packets_per_send_request"]?.AsArray() ?? new JsonArray())
                      .Skip(statsBefore).Select(x => x!.GetValue<int>()).ToList();
        _packetsA = packets;
        Check($"بسته‌بندی: هیچ درخواستی بیش از ۹۹ صورتحساب ندارد و جمع بسته‌ها = فاکتورهای ارسالی ({aGood.Count})",
              packets.Count >= 2 && packets.Max() <= 99 && packets.Sum() == aGood.Count,
              "بسته‌ها: " + string.Join("+", packets));
        Check("بسته‌بندی: ۱۱۰ فاکتور دقیقاً دو درخواست ۹۹ + ۱۱ است (MaxPerRequest = 99)",
              packets.SequenceEqual(new[] { 99, aGood.Count - 99 }), string.Join("+", packets));

        Check("گروهی: همهٔ فاکتورهای سالم به صف رفتند (Success = تعداد سالم)",
              res.Success == aGood.Count, $"Success={res.Success} از {aGood.Count}");
        Check("گروهی: دقیقاً چهار فاکتور بد پیش از ارسال با پاسخ «لغو» کنار رفتند",
              res.Failures.Keys.OrderBy(x => x).SequenceEqual(aBad),
              string.Join(",", res.Failures.Keys));
        Check("گروهی: برای هر فاکتور بد، OnValidationWarning پرسیده شد (هشدار، نه سد خاموش)",
              new[] { 7901L, 7902, 7903 }.All(n => Warnings.Count(w => w.Contains("فاکتور " + n.ToString(CultureInfo.InvariantCulture))) == 1) &&
              Warnings.Count(w => w.Contains("مبلغ واحد")) == 1 && Warnings.Count == aBad.Length,
              $"{Warnings.Count} هشدار: " + Short(string.Join(" / ", Warnings)));

        var payloads = Payloads();
        var innoOf = payloads.ToDictionary(kv => kv.Key, kv => kv.Value?["header"]?["inno"]?.GetValue<string>() ?? "");
        var fn = new CL_FUNTIONS();
        var badInnos = aBad.Select(n => fn.GenerateFixedLengthInno("1405", n)).ToHashSet();
        Check("گروهی: هیچ فاکتور بدی (پس از «لغو») به سرور نرسید",
              !innoOf.Values.Any(badInnos.Contains), string.Join(",", innoOf.Values.Where(badInnos.Contains)));

        var local = _db.Rows("TAXDTL").Where(r => Str(r, "TheStatus") == "LOCAL_ERROR").ToList();
        Check("گروهی: برای هر فاکتور لغوشده یک ردیف LOCAL_ERROR با متن خطا در TAXDTL ثبت شد",
              aBad.All(n => local.Any(r => Num(r, "NUMBER") == n && Num(r, "TAG") == SendTag && !string.IsNullOrEmpty(Str(r, "TheError")))),
              string.Join(" | ", local.Select(r => $"{Num(r, "NUMBER")}:{Short(Str(r, "TheError"))}")));

        // --- ژورنال TAXDTL: یک ردیف به ازای هر قلم، با NUMBER/TAG/Taxid/Inno/Ref/UID درست
        var state = MockState();
        var bulkRows = _db.Rows("TAXDTL").Where(r => Str(r, "REMARKS") == "Bulk").ToList();
        var problems = new List<string>();
        foreach (var inv in aGood)
        {
            var rows = bulkRows.Where(r => Num(r, "NUMBER") == inv.Number).ToList();
            if (rows.Count != inv.Lines.Count) { problems.Add($"{inv.Number}: {rows.Count} ردیف / {inv.Lines.Count} قلم"); continue; }
            var taxid = Str(rows[0], "Taxid");
            if (rows.Any(r => Str(r, "Taxid") != taxid)) problems.Add($"{inv.Number}: Taxid ناهمسان");
            if (rows.Any(r => Num(r, "TAG") != SendTag)) problems.Add($"{inv.Number}: TAG={Num(rows[0], "TAG")}");
            if (rows.Any(r => Str(r, "Inno") != fn.GenerateFixedLengthInno("1405", inv.Number))) problems.Add($"{inv.Number}: Inno={Str(rows[0], "Inno")}");
            if (rows.Any(r => Str(r, "TheStatus") != "PENDING")) problems.Add($"{inv.Number}: وضعیت {Str(rows[0], "TheStatus")}");
            if (!state.TryGetValue(taxid, out var s)) { problems.Add($"{inv.Number}: taxid {taxid} روی سرور نیست"); continue; }
            if (rows.Any(r => Str(r, "RefrenceNumber") != s.Reference || Str(r, "UID") != s.Uid))
                problems.Add($"{inv.Number}: جفت‌شدن Ref/UID با taxid غلط است");
            OracleSource[taxid] = inv.Lines;
        }
        Check("TAXDTL: هر قلم یک ردیف؛ NUMBER و TAG=۲ (تگ حواله، نه ۱۳) و Inno درست؛ وضعیت PENDING",
              problems.Count == 0, string.Join(" ؛ ", problems.Take(5)) + (problems.Count > 5 ? $" … ({problems.Count})" : ""));
        Check("TAXDTL: کد رهگیری و UID هر ردیف دقیقاً مال همان taxid روی سرور است (PersistChunk پاسخ‌ها را با اندیس جفت می‌کند)",
              !problems.Any(p => p.Contains("جفت")), "");

        var hex = aGood.Where(i => i.Number >= 1_000_000).Select(i => fn.GenerateFixedLengthInno("1405", i.Number)).ToList();
        Check("سریال فاکتورهای بالای یک میلیون در باند هگز 0xA00000 است و در payload همان می‌رود",
              hex.All(x => x.StartsWith("1405A", StringComparison.Ordinal) && innoOf.Values.Contains(x)),
              string.Join(",", hex.Take(3)));

        int accepted = aGood.Count(i => TaxidOf(i.Number) is { } t && state.TryGetValue(t, out var s) && s.Ok);
        Check($"سرور کامل همهٔ {aGood.Count} فاکتور سالم را پذیرفت (نوع اول/نقدی، tob ۱ تا ۴، چندقلمی، اعشاری، تخفیف)",
              accepted == aGood.Count,
              string.Join(" | ", aGood.Where(i => !(TaxidOf(i.Number) is { } t && state.TryGetValue(t, out var s) && s.Ok))
                                   .Take(3).Select(i => i.Number + ":" + Codes(state, TaxidOf(i.Number)))));

        var hle = _db.Rows("HEAD_LST_EXTENDED");
        Check("گروهی: HEAD_LST_EXTENDED نبود → با INSERT خودکار (TGU=۲) ساخته شد",
              aGood.All(i => hle.Any(r => Num(r, "NUMBER") == i.Number && Num(r, "TGU") == SendTag)),
              $"{hle.Count} ردیف");

        // --- ۲) همان بدها، این بار کاربر «ادامه» می‌زند — هشدار هرگز سد نیست (CLAUDE.md بخش ۱)
        Warnings.Clear();
        var bulk2 = NewBulk(_ => true);
        var res2 = bulk2.SendAsync(aBad.Append(7056L), SendTag, 1, 1).GetAwaiter().GetResult();
        state = MockState();
        Check("هشدار سد نیست: با «ادامه» هر ۵ فاکتور (۴ بد + ۱ سالم) در یک درخواست به سرور رفتند",
              res2.Success == 5 && res2.Failures.Count == 0, $"Success={res2.Success} Failures={res2.Failures.Count}");
        Verdict(7056, true, null, "سالمِ هم‌بستهٔ بدها پذیرفته شد (شکست یکی، بقیه را نمی‌خواباند)");
        Verdict(7902, true, null, "بد (واحد خالی) با «ادامه»: سرور ساختگی mu را کنترل نمی‌کند → پذیرفته");
        Verdict(7901, false, null, "بد (شناسه کالای خالی) با «ادامه»: سرور رد کرد");
        Verdict(7903, false, "0101204", "بد (شماره اقتصادی ۱۳ رقمیِ حقوقی) با «ادامه»: سرور 0101204 داد");
        Verdict(7904, false, "01037", "بد (فی صفر) با «ادامه»: سرور خطای فی (01037xx) داد");
        foreach (var n in new long[] { 7901, 7903, 7904 }) if (TaxidOf(n) is { } t) OracleExclude.Add(t);
        if (TaxidOf(7056) is { } t56) OracleSource[t56] = ByNumber[7056].Lines;

        // --- ۳) بقیهٔ ترکیب‌ها: نوع × تسویه، و مسیر بدون انتخاب UI، و دناافراز
        RunCombo("نوع اول / نسیه", All.Where(i => i.Number is >= 7101 and <= 7110), 1, 2);
        RunCombo("نوع اول / نقدی-نسیه (cap از HEAD_LST_EXTENDED)", All.Where(i => i.Number is >= 7201 and <= 7208), 1, 3);
        RunCombo("نوع دوم / نقدی", All.Where(i => i.Number is >= 7301 and <= 7308), 2, 1);
        RunCombo("نوع دوم / نسیه", All.Where(i => i.Number is >= 7401 and <= 7405), 2, 2);
        RunCombo("نوع دوم / نقدی-نسیه", All.Where(i => i.Number is >= 7501 and <= 7505), 2, 3);
        RunCombo("بدون انتخاب در UI (inty/setm از HEAD_LST_EXTENDED)", All.Where(i => i.Number is >= 7601 and <= 7605), null, null);
        RunCombo("دناافراز (کوئری HEAD_LST/INVO_LST، CALLER_NAME=d)", All.Where(i => i.Number is >= 7701 and <= 7703), 2, 1, caller: "d");

        // setm=3 بدون cap
        Warnings.Clear();
        var r3 = NewBulk(_ => false).SendAsync(new[] { 7909L }, SendTag, 1, 3).GetAwaiter().GetResult();
        Check("نقدی/نسیه بدون مبلغ نقدی: با «لغو» پیش از ارسال کنار رفت و هشدارِ cap پرسیده شد",
              r3.Failures.ContainsKey(7909) && TaxidOf(7909) == null && Warnings.Any(w => w.Contains("نقدی")),
              Short(string.Join(" / ", Warnings)));
        var r3b = NewBulk(_ => true).SendAsync(new[] { 7909L }, SendTag, 1, 3).GetAwaiter().GetResult();
        state = MockState();
        Verdict(7909, false, null, "نقدی/نسیه بدون cap با «ادامه»: رفت و سرور رد کرد (cap اجباری است)");
        if (TaxidOf(7909) is { } t9) OracleExclude.Add(t9);

        // cap+insp در setm=3 = tbill − tvam − todam (کد تولید حساب می‌کند؛ پیشگو هم جدا می‌سنجد)
        var p3 = Payloads();
        var bad3 = new List<string>();
        foreach (var inv in All.Where(i => i.Number is >= 7201 and <= 7208 or >= 7501 and <= 7505))
        {
            var t = TaxidOf(inv.Number);
            var h = t != null && p3.TryGetValue(t, out var node) ? node?["header"] : null;
            if (h == null) { bad3.Add(inv.Number + ": payload نیست"); continue; }
            decimal cap = D(h["cap"]), insp = D(h["insp"]), tbill = D(h["tbill"]), tvam = D(h["tvam"]), todam = D(h["todam"]);
            if (cap != 1000 || cap + insp != tbill - tvam - todam) bad3.Add($"{inv.Number}: cap={cap} insp={insp} tbill={tbill} tvam={tvam}");
        }
        Check("نقدی/نسیه: cap همان مقدار دیتابیس و cap+insp = tbill − tvam − todam (ص۴۶ جدول ۲۵)",
              bad3.Count == 0, string.Join(" ؛ ", bad3.Take(3)));

        var d = _db.ShapeHits.Keys.Any(k => k.StartsWith("DRVD_TBL(DenaFaraz)", StringComparison.Ordinal));
        Check("دناافراز: کوئری مخصوص دناافراز (بدون VNAMES) واقعاً اجرا شد", d, "");

        // --- یافته: UI اجازه می‌دهد «روش تسویه» انتخاب شود و «نوع» خالی بماند (MainWindow.xaml.cs:668
        //     «خواندن از اطلاعات خود فاکتور»)، ولی SendInvoiceBulk.cs:534 روی (int)Inty_Value می‌افتد.
        var ri = NewBulk(_ => true).SendAsync(new[] { 7992L, 7993 }, SendTag, null, 1).GetAwaiter().GetResult();
        Check("یافته: setm انتخاب‌شده + inty خالی → هر فاکتور پیش از ارسال با خطای داخلی می‌افتد (SendInvoiceBulk.cs:534 ‎(int)Inty_Value‎). رفتار فعلی قفل شد",
              ri.Success == 0 && ri.Failures.Count == 2 && TaxidOf(7992) == null && TaxidOf(7993) == null,
              $"Success={ri.Success} Failures={string.Join(" | ", ri.Failures.Select(f => f.Key + ":" + Short(f.Value)))}");

        // --- یافته: HEAD_LST.DEPATMAN خالی → SendInvoiceBulk.cs:329 «WHERE DEPATMAN = » (SQL نامعتبر)
        var rd = NewBulk(_ => true).SendAsync(new[] { 7991L }, SendTag, 1, 1).GetAwaiter().GetResult();
        var t91 = SendSingle(7991, out var e91);
        Check("یافته: HEAD_LST.DEPATMAN خالی → گروهی فاکتور را با SQL نامعتبر «WHERE DEPATMAN = » از دست می‌دهد (SendInvoiceBulk.cs:329)، تکی با زیرپرس‌وجو می‌فرستد (CL_MOADIAN.cs:337). رفتار فعلی قفل شد",
              rd.Success == 0 && rd.Failures.ContainsKey(7991) && t91 != null && e91 == null &&
              _db.Errors.Count(x => x.StartsWith("Incorrect syntax", StringComparison.Ordinal)) == 1,
              $"گروهی: {Short(rd.Failures.GetValueOrDefault(7991))} ؛ تکی: {(t91 != null ? "ارسال شد" : e91)}");
        if (t91 != null) OracleSource[t91] = ByNumber[7991].Lines;
    }

    private static void RunCombo(string label, IEnumerable<Inv> invs, int? inty, int? setm, string caller = "m")
    {
        var list = invs.ToList();
        Warnings.Clear();
        var bulk = NewBulk(_ => false);
        bulk.CALLER_NAME = caller;
        var res = bulk.SendAsync(list.Select(i => i.Number), SendTag, inty, setm).GetAwaiter().GetResult();
        var state = MockState();
        var notOk = list.Where(i => !(TaxidOf(i.Number) is { } t && state.TryGetValue(t, out var s) && s.Ok)).ToList();
        Check($"گروهی {label}: {list.Count} فاکتور بی‌هشدار رفتند و سرور همه را پذیرفت",
              res.Success == list.Count && res.Failures.Count == 0 && notOk.Count == 0,
              $"Success={res.Success} Failures={string.Join(",", res.Failures.Select(f => f.Key + ":" + Short(f.Value)))} " +
              string.Join(" | ", notOk.Take(3).Select(i => i.Number + ":" + Codes(state, TaxidOf(i.Number)))) +
              (Warnings.Count > 0 ? " هشدار: " + Short(Warnings[0]) : ""));
        foreach (var i in list) if (TaxidOf(i.Number) is { } t) OracleSource[t] = i.Lines;
    }

    private static SendInvoiceBulk NewBulk(Func<string, bool> answer)
    {
        var b = new SendInvoiceBulk(new CL_CCNNMANAGER(), _sendUrl);
        b.OnValidationWarning = msg => { lock (Warnings) Warnings.Add(msg); return answer(msg); };
        return b;
    }

    private static void Verdict(long number, bool ok, string? codePrefix, string label)
    {
        var state = MockState();
        var t = TaxidOf(number);
        bool pass = t != null && state.TryGetValue(t, out var s) && s.Ok == ok &&
                    (codePrefix == null || s.Errors.Any(c => c.StartsWith(codePrefix, StringComparison.Ordinal)));
        Check(label, pass, t == null ? "taxid در TAXDTL نیست" : Codes(state, t));
    }

    // ================================================================ c. استعلام

    private static void C_Inquiry()
    {
        var est = new CL_ESTELAM();
        est.GET_INIT_TAX(_sendUrl);

        // همان انتخاب BGWorker_DoWork: TheStatus خالی/PENDING، و فقط آن‌ها که کد رهگیری دارند
        var pending = _db.Rows("TAXDTL")
            .Where(r => Str(r, "TheStatus") is null or "PENDING" or "" or "NULL")
            .Select(r => Str(r, "RefrenceNumber")).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        foreach (var rf in pending) est.GETESTELAM_REFCODE_UPDATE(rf!);

        var state = MockState();
        var rows = _db.Rows("TAXDTL").Where(r => Str(r, "REMARKS") == "Bulk").ToList();
        var wrong = new List<string>();
        foreach (var g in rows.GroupBy(r => Str(r, "Taxid")!))
        {
            if (!state.TryGetValue(g.Key, out var s)) { wrong.Add(g.Key + " روی سرور نیست"); continue; }
            string want = s.Ok ? "SUCCESS" : "FAILED";
            foreach (var r in g)
            {
                if (Str(r, "TheStatus") != want) { wrong.Add($"{Num(r, "NUMBER")}: {Str(r, "TheStatus")} به جای {want}"); break; }
                if (Bool(r, "TheSuccess") != s.Ok) { wrong.Add($"{Num(r, "NUMBER")}: TheSuccess"); break; }
                if (s.Ok && string.IsNullOrEmpty(Str(r, "TheConfirmationReferenceId"))) { wrong.Add($"{Num(r, "NUMBER")}: بدون شناسهٔ تأیید"); break; }
                if (!s.Ok && !s.Errors.All(c => (Str(r, "TheError") ?? "").Contains(c))) { wrong.Add($"{Num(r, "NUMBER")}: TheError کدها را ندارد"); break; }
            }
        }
        Check($"استعلام: هر ردیف TAXDTL به SUCCESS/FAILED همان حکم سرور رسید ({rows.Select(r => Str(r, "Taxid")).Distinct().Count()} صورتحساب)",
              wrong.Count == 0, string.Join(" ؛ ", wrong.Take(4)));

        var t7903 = _db.Rows("TAXDTL").FirstOrDefault(r => Num(r, "NUMBER") == 7903 && Str(r, "TheStatus") == "FAILED");
        Check("استعلام: متن خطای ردشده «کد | پیام» ذخیره شد (7903 → 0101204 | …)",
              t7903 != null && (Str(t7903, "TheError") ?? "").StartsWith("0101204 | ", StringComparison.Ordinal),
              Short(t7903 == null ? "ردیف FAILED نیست" : Str(t7903, "TheError")));

        var warned = rows.Where(r => Str(r, "TheStatus") == "SUCCESS" && !string.IsNullOrEmpty(Str(r, "TheWarning"))).ToList();
        Check("استعلام: هشدارِ سامانه (رقم اول ۱) در TheWarning ثبت شد و وضعیت SUCCESS ماند — هشدار سد نیست",
              warned.Count > 0 && warned.All(r => (Str(r, "TheWarning") ?? "").Split(',').All(w => w.TrimStart().StartsWith("1", StringComparison.Ordinal))),
              $"{warned.Count} ردیف با هشدار؛ نمونه: {Short(warned.FirstOrDefault() is { } w0 ? Str(w0, "TheWarning") : "")}");

        var hle = _db.Rows("HEAD_LST_EXTENDED");
        var okInvs = rows.Where(r => Str(r, "TheStatus") == "SUCCESS").GroupBy(r => Num(r, "NUMBER")).ToList();
        var noIr = okInvs.Where(g => !hle.Any(h => Num(h, "NUMBER") == g.Key && Num(h, "TGU") == SendTag &&
                                                  Str(h, "irtaxid") == Str(g.First(), "Taxid"))).Select(g => g.Key).ToList();
        Check("استعلام موفق: HEAD_LST_EXTENDED.irtaxid = شماره مالیاتی همان صورتحساب (مرجعِ ارجاعی بعدی)",
              noIr.Count == 0, string.Join(",", noIr.Take(5)));

        // --- IN_PROGRESS: سرور یک بار «در حال پردازش» می‌گوید
        Post("__config", new { in_progress_polls = 1 });
        NewBulk(_ => false).SendAsync(new[] { 7057L }, SendTag, 1, 1).GetAwaiter().GetResult();
        var rf57 = Str(_db.Rows("TAXDTL").First(r => Num(r, "NUMBER") == 7057), "RefrenceNumber")!;
        est.GETESTELAM_REFCODE_UPDATE(rf57);
        var s1 = _db.Rows("TAXDTL").Where(r => Num(r, "NUMBER") == 7057).Select(r => Str(r, "TheStatus")).Distinct().ToList();
        est.GETESTELAM_REFCODE_UPDATE(rf57);
        var s2 = _db.Rows("TAXDTL").Where(r => Num(r, "NUMBER") == 7057).Select(r => Str(r, "TheStatus")).Distinct().ToList();
        Check("استعلام IN_PROGRESS: اول «IN_PROGRESS» ثبت شد، استعلام بعدی SUCCESS",
              s1.SequenceEqual(new[] { "IN_PROGRESS" }) && s2.SequenceEqual(new[] { "SUCCESS" }),
              string.Join(",", s1) + " → " + string.Join(",", s2));
        if (TaxidOf(7057) is { } t57) OracleSource[t57] = ByNumber[7057].Lines;

        // --- یافته: IN_PROGRESS با data خالی (EC_V02 ص۱۶) → NullReferenceException، ردیف PENDING می‌ماند
        Post("__config", new { in_progress_polls = 1, empty_data_when_in_progress = true });
        NewBulk(_ => false).SendAsync(new[] { 7058L }, SendTag, 1, 1).GetAwaiter().GetResult();
        var rf58 = Str(_db.Rows("TAXDTL").First(r => Num(r, "NUMBER") == 7058), "RefrenceNumber")!;
        string? thrown = null;
        try { est.GETESTELAM_REFCODE_UPDATE(rf58); } catch (Exception ex) { thrown = ex.GetType().Name; }
        var s58 = _db.Rows("TAXDTL").Where(r => Num(r, "NUMBER") == 7058).Select(r => Str(r, "TheStatus")).Distinct().ToList();
        Check("یافته: IN_PROGRESS با data خالی → GETESTELAM_REFCODE_UPDATE استثنا می‌دهد (CL_ESTELAM.cs:131) و ردیف PENDING می‌ماند (رفتار فعلی قفل شد)",
              thrown == nameof(NullReferenceException) && s58.SequenceEqual(new[] { "PENDING" }),
              $"استثنا={thrown ?? "هیچ"} وضعیت={string.Join(",", s58)}");
        Post("__config", new { in_progress_polls = 0, empty_data_when_in_progress = false });
        est.GETESTELAM_REFCODE_UPDATE(rf58);
        if (TaxidOf(7058) is { } t58) OracleSource[t58] = ByNumber[7058].Lines;
    }

    // ================================================================ b. تکی

    private static readonly Dictionary<long, string> SingleTaxid = new();

    private static string? SendSingle(long number, out string? error)
    {
        error = null;
        CL_MOADIAN.TaxURL = _sendUrl;
        CL_MOADIAN.OnValidationWarning = _ => true;
        var before = _db.Rows("TAXDTL").Select(r => Str(r, "Taxid")).ToHashSet();
        try { CL_MOADIAN.DoSendInvoice(new[] { $"{number}_{SendTag}_m" }); }
        catch (Exception ex) { error = One(ex); }
        return _db.Rows("TAXDTL").Where(r => Num(r, "NUMBER") == number && !before.Contains(Str(r, "Taxid")))
                  .Select(r => Str(r, "Taxid")).FirstOrDefault();
    }

    private static void B_Single()
    {
        var nums = new long[] { 7801, 7802, 7803 };
        var sw = Stopwatch.StartNew();
        foreach (var n in nums)
        {
            var t = SendSingle(n, out var err);
            Check($"تکی {n}: DoSendInvoice بی‌استثنا تمام شد و ردیف TAXDTL ساخت", t != null && err == null, err ?? "");
            if (t != null) { SingleTaxid[n] = t; OracleSource[t] = ByNumber[n].Lines; }
        }
        _line($"      · سه ارسال تکی در {sw.Elapsed.TotalSeconds:F1} ثانیه (Sleep ده‌ثانیه‌ای صفر شده)");

        var state = MockState();
        var rows = _db.Rows("TAXDTL");
        Check("تکی: سرور هر سه را پذیرفت و استعلام خودکارِ خودِ مسیر تکی ردیف‌ها را SUCCESS کرد",
              nums.All(n => SingleTaxid.TryGetValue(n, out var t) && state.TryGetValue(t, out var s) && s.Ok &&
                            rows.Where(r => Str(r, "Taxid") == t).All(r => Str(r, "TheStatus") == "SUCCESS")),
              string.Join(" | ", nums.Select(n => n + ":" + (SingleTaxid.TryGetValue(n, out var t)
                  ? string.Join(",", rows.Where(r => Str(r, "Taxid") == t).Select(r => Str(r, "TheStatus")).Distinct()) : "—"))));
        Check("تکی: ردیف‌ها NUMBER و TAG=۲ دارند، برچسب Bulk نمی‌خورند، Tinb هرگز NULL نیست",
              nums.All(n => SingleTaxid.TryGetValue(n, out var t) &&
                            rows.Where(r => Str(r, "Taxid") == t).All(r => Num(r, "NUMBER") == n && Num(r, "TAG") == SendTag &&
                                                                         Str(r, "REMARKS") != "Bulk" && Str(r, "Tinb") != null) &&
                            rows.Count(r => Str(r, "Taxid") == t) == ByNumber[n].Lines.Count), "");

        // همان فاکتورها از مسیر گروهی بدون انتخاب UI (مثل گروه ۲۲)
        var res = NewBulk(_ => true).SendAsync(nums, SendTag, null, null).GetAwaiter().GetResult();
        var payloads = Payloads();
        var diffsAll = new List<string>();
        var moneyDiff = new List<string>();
        var identDiff = new List<string>();
        foreach (var n in nums)
        {
            var bt = _db.Rows("TAXDTL").Where(r => Num(r, "NUMBER") == n && Str(r, "REMARKS") == "Bulk").Select(r => Str(r, "Taxid")).FirstOrDefault();
            if (bt == null || !SingleTaxid.TryGetValue(n, out var st)) { identDiff.Add(n + ": ارسال ناقص"); continue; }
            OracleSource[bt] = ByNumber[n].Lines;
            var sp = payloads[st]!; var bp = payloads[bt]!;
            foreach (var d in DiffJson(sp, bp)) diffsAll.Add(d);
        }
        string[] money = { "tprdis", "tdis", "tadis", "tvam", "todam", "tbill", "am", "fee", "prdis", "dis", "adis", "vra", "vam", "tsstam" };
        string[] ident = { "inno", "indatim", "inty", "inp", "ins", "tins", "tob", "tinb", "bid", "bpc", "bbc", "sbc", "setm", "irtaxid", "insr", "sstid", "mu", "sstt" };
        foreach (var d in diffsAll)
        {
            var field = d.Split(':')[0].Split('.').Last().Split('[')[0];
            if (money.Contains(field)) moneyDiff.Add(d);
            else if (ident.Contains(field)) identDiff.Add(d);
        }
        Check("تکی ≡ گروهی: همهٔ مبالغ سرصفحه و اقلام یکسان (دوقلوی بی‌دیتابیسِ گروه ۲۲)",
              moneyDiff.Count == 0 && res.Success == nums.Length, string.Join(" ؛ ", moneyDiff.Take(4)));
        Check("تکی ≡ گروهی: سریال، تاریخ صدور، نوع/الگو/موضوع، خریدار، کد شعبه، setm، کالا/واحد یکسان",
              identDiff.Count == 0, string.Join(" ؛ ", identDiff.Take(4)));

        // واگرایی‌های شناخته‌شده روی سیم — فهرست دقیق قفل می‌شود تا تغییرش دیده شود
        var other = diffsAll.Except(moneyDiff).Except(identDiff)
                            .Select(d => d.Split(':')[0]).Select(Generic).Distinct().OrderBy(x => x).ToList();
        var expected = new[] { "body[].consfee", "body[].bros", "body[].cop", "body[].odam", "body[].odr", "body[].odt",
                               "body[].olam", "body[].olr", "body[].olt", "body[].spro", "body[].tcpbs", "body[].vop",
                               "header.cap", "header.indati2m", "header.insp", "header.tax17", "header.tvop", "header.ft" }
                       .OrderBy(x => x).ToList();
        _line("      · واگرایی روی سیم (تکی / گروهی): " + string.Join("، ", other));
        foreach (var d in diffsAll.Except(moneyDiff).Except(identDiff).Where(x => x.StartsWith("header.", StringComparison.Ordinal)).Take(6))
            _line("        " + d);
        Check("تکی ≡ گروهی: تنها تفاوت‌ها همین فهرست ثبت‌شده‌اند (indati2m، cap/insp، tvop/tax17/ft، و صفرهای صریحِ odt/olt/… در تکی)",
              other.All(expected.Contains), "غیرمنتظره: " + string.Join("، ", other.Where(x => !expected.Contains(x))));
    }

    private static string Generic(string path) => System.Text.RegularExpressions.Regex.Replace(path, @"\[\d+\]", "[]");

    private static IEnumerable<string> DiffJson(JsonNode single, JsonNode bulk)
    {
        var sh = single["header"]!.AsObject(); var bh = bulk["header"]!.AsObject();
        foreach (var k in sh.Select(x => x.Key).Union(bh.Select(x => x.Key)).Distinct())
        {
            if (k == "taxid") continue;
            string a = sh[k]?.ToJsonString() ?? "null", b = bh[k]?.ToJsonString() ?? "null";
            if (a != b) yield return $"header.{k}: تکی {a} / گروهی {b}";
        }
        var sb = single["body"]!.AsArray(); var bb = bulk["body"]!.AsArray();
        if (sb.Count != bb.Count) { yield return $"body.count: تکی {sb.Count} / گروهی {bb.Count}"; yield break; }
        for (int i = 0; i < sb.Count; i++)
        {
            var so = sb[i]!.AsObject(); var bo = bb[i]!.AsObject();
            foreach (var k in so.Select(x => x.Key).Union(bo.Select(x => x.Key)).Distinct())
            {
                string a = so[k]?.ToJsonString() ?? "null", b = bo[k]?.ToJsonString() ?? "null";
                if (a != b) yield return $"body[{i}].{k}: تکی {a} / گروهی {b}";
            }
        }
    }

    // ================================================================ d. زنجیرهٔ ارجاعی

    private static void SetHle(long number, int ins)
    {
        var db = new CL_CCNNMANAGER();
        // کاربر در برنامهٔ حسابداری موضوع صورتحساب را عوض می‌کند (خارج از این مخزن) — این‌جا مستقیم.
        db.DoExecuteSQL($"UPDATE dbo.HEAD_LST_EXTENDED SET ins = {ins} WHERE NUMBER = {number} AND TGU = {SendTag}");
    }

    private static void D_Chain()
    {
        var fn = new CL_FUNTIONS();
        // --- ۱) اصلی
        var o = SendSingle(7851, out var e0);
        var state = MockState();
        Check("زنجیره: اصلیِ ۷۸۵۱ از مسیر تکی پذیرفته شد و irtaxid سربرگ = شماره مالیاتی خودش شد",
              o != null && state.TryGetValue(o, out var so) && so.Ok && HleIrtaxid(7851) == o, e0 ?? Codes(state, o));
        if (o == null) return;
        OracleSource[o] = ByNumber[7851].Lines;

        // --- ۲) برگشت از فروش: یک واحد از قلم اول برگشت خورد
        var inv = ByNumber[7851];
        _db.Seed("HEAD_LST_FBK", new FbkRow { NUMBER = 1, NUMBER1 = 7851, DATE_N = _dateNYesterday });
        foreach (var r in _db.GetTable("DRVD_TBL").Rows.Where(r => Convert.ToDouble(r["NUMBER"]) == 7851).Take(1))
            r["MEGH_MAR"] = 1m;
        SetHle(7851, 4);
        var ret = SendSingle(7851, out var e1);
        state = MockState();
        var pr = ret != null ? Payloads()[ret] : null;
        bool amDown = pr != null && D(pr["body"]![0]!["am"]) == inv.Lines[0].Am - 1;
        Check("زنجیره: برگشتی (ins=4) با irtaxid = اصلی و تعداد کم‌شده پذیرفته شد",
              ret != null && state.TryGetValue(ret, out var sr) && sr.Ok && pr?["header"]?["ins"]?.GetValue<int>() == 4 &&
              pr?["header"]?["irtaxid"]?.GetValue<string>() == o && amDown,
              e1 ?? Codes(state, ret) + $" am={pr?["body"]?[0]?["am"]}");
        Check("زنجیره: برگشتی تاریخِ HEAD_LST_FBK را گرفت و پس از استعلام، سربرگ به برگشتی اشاره می‌کند",
              ret != null && HleIrtaxid(7851) == ret &&
              pr?["header"]?["indatim"]?.GetValue<long>() == TaxService.ConvertDateToLong(fn.GetGregorianDateTime(_dateNYesterday.ToString(CultureInfo.InvariantCulture))),
              $"irtaxid={HleIrtaxid(7851)}");

        // --- ۳) ابطالِ همان برگشتی
        SetHle(7851, 3);
        var cancel = SendSingle(7851, out var e2);
        state = MockState();
        var pc = cancel != null ? Payloads()[cancel] : null;
        Check("زنجیره: ابطالی (ins=3) روی برگشتیِ «در انتظار واکنش» پذیرفته شد و مرجع باطل شد (FAQ 4-9)",
              cancel != null && state.TryGetValue(cancel, out var sc) && sc.Ok && pc?["header"]?["irtaxid"]?.GetValue<string>() == ret &&
              Dash(ret!) == "CANCELED", e2 ?? Codes(state, cancel) + " " + (ret != null ? Dash(ret) : ""));
        Check("یافته: مسیر تکی برای ابطالی هم اقلام را می‌فرستد (فرم اصلاحی نمی‌فرستد؛ سند ص۱۷ لازم نمی‌داند) — رفتار فعلی قفل شد",
              (pc?["body"]?.AsArray().Count ?? 0) == inv.Lines.Count, $"{pc?["body"]?.AsArray().Count} قلم");

        // --- ۴) اصلاحی روی یک اصلی دیگر: مبلغ واحد قلم دوم تغییر کرد
        var o2 = SendSingle(7852, out var e3);
        if (o2 == null) { Check("زنجیره: اصلیِ ۷۸۵۲ ارسال شد", false, e3 ?? ""); return; }
        OracleSource[o2] = ByNumber[7852].Lines;
        foreach (var r in _db.GetTable("DRVD_TBL").Rows.Where(r => Convert.ToDouble(r["NUMBER"]) == 7852).Skip(1).Take(1))
            r["MABL"] = (decimal)r["MABL"]! + 1000m;
        SetHle(7852, 2);
        var corr = SendSingle(7852, out var e4);
        state = MockState();
        var pk = corr != null ? Payloads()[corr] : null;
        Check("زنجیره: اصلاحی (ins=2) با irtaxid = اصلی و فیِ تازه پذیرفته شد",
              corr != null && state.TryGetValue(corr, out var sk) && sk.Ok && pk?["header"]?["irtaxid"]?.GetValue<string>() == o2 &&
              D(pk?["body"]?[1]?["fee"]) == ByNumber[7852].Lines[1].Fee + 1000,
              e4 ?? Codes(state, corr));

        // --- ۵) ریاضیات فرم اصلاحی (CorrectionMath) روی ردیف‌های TAXDTL همین دیتابیس
        var baseRows = new CL_CCNNMANAGER().DoGetDataSQL<TAXDTL>(
            "SELECT * FROM dbo.TAXDTL WHERE Taxid = @t AND ApiTypeSent = 0 ORDER BY IDD", new { t = TaxidOf(7002) }).ToList();
        var h0 = baseRows.FirstOrDefault();
        if (h0 == null) { Check("CorrectionMath: ردیف‌های مرجع از TAXDTL خوانده شد", false, "7002"); return; }
        foreach (var r in baseRows) { r.Am = r.Am + 0.25m; CorrectionMath.RecalculateRow(r); }
        var tax = new TaxService(MemoryId, _key, _sendUrl); tax.RequestToken();
        long now = ServerNow() - 30_000;
        var hh = new TaxModel.InvoiceModel.Header
        {
            Taxid = tax.RequestTaxId(MemoryId, DateTimeOffset.FromUnixTimeMilliseconds(now).ToOffset(TimeSpan.FromHours(3.5)).DateTime),
            Indatim = now, Indati2m = now, Inty = h0.Inty ?? 1, Inno = h0.Inno, Inp = h0.Inp ?? 1, Ins = 2,
            Irtaxid = h0.Taxid, Tins = h0.Tins, Tob = h0.Tob ?? 2, Tinb = string.IsNullOrEmpty(h0.Tinb) ? null : h0.Tinb,
            Bid = h0.Bid, Bpc = h0.Bpc, Setm = (int)(h0.Setm ?? 1),
            Tprdis = baseRows.Sum(r => r.Prdis ?? 0), Tdis = baseRows.Sum(r => r.Dis ?? 0), Tadis = baseRows.Sum(r => r.Adis ?? 0),
            Tvam = baseRows.Sum(r => r.Vam ?? 0), Todam = 0, Tbill = baseRows.Sum(r => r.Tsstam ?? 0),
        };
        var bodies = baseRows.Select(r => new TaxModel.InvoiceModel.Body
        {
            Sstid = r.Sstid, Sstt = r.Sstt, Mu = r.Mu, Am = r.Am ?? 0, Fee = r.Fee ?? 0, Prdis = r.Prdis ?? 0, Dis = r.Dis ?? 0,
            Adis = r.Adis ?? 0, Vra = r.Vra ?? 0, Vam = r.Vam ?? 0, Tsstam = r.Tsstam ?? 0,
        }).ToList();
        var resp = tax.SendInvoices(hh, bodies, new List<TaxModel.InvoiceModel.Payment>());
        state = MockState();
        Check("اصلاحیِ ساخته‌شده با CorrectionMath روی ردیف‌های TAXDTL ِ ۷۰۰۲ (تعداد +۰٫۲۵) پذیرفته شد",
              state.TryGetValue(hh.Taxid!, out var sm) && sm.Ok, Codes(state, hh.Taxid));
    }

    private static string? HleIrtaxid(long number) =>
        _db.Rows("HEAD_LST_EXTENDED").Where(r => Num(r, "NUMBER") == number && Num(r, "TGU") == SendTag)
           .Select(r => Str(r, "irtaxid")).FirstOrDefault();

    // ================================================================ e. ارسال مجدد

    private static void E_Resend()
    {
        var (host, err) = ResendTransplant.Build();
        Check("ارسال مجدد: حلقهٔ RESEND_BTN_Click و InsertNewTaxDtlRecord/CreateHeader/CreateBody از سورس WPF کامپایل شدند (Roslyn)",
              host != null, err ?? "");
        if (host == null) return;

        long number = 7003;
        var taxid = TaxidOf(number)!;
        var before = _db.Rows("TAXDTL").Where(r => Str(r, "Taxid") == taxid).ToList();
        var origPayload = Payloads()[taxid]!.DeepClone();
        int reqBefore = Stats()?["packets_per_send_request"]?.AsArray().Count ?? 0;
        var tax = new TaxService(MemoryId, _key, _sendUrl); tax.RequestToken();

        var (ok, fail, ex) = ResendTransplant.Run(host, new List<string> { taxid }, tax, isMainApi: false, windowsUser: "e2e");
        Check("ارسال مجدد: یک فاکتور، بدون استثنا، شمارندهٔ موفق = ۱", ok == 1 && fail == 0 && ex == null,
              $"موفق={ok} ناموفق={fail} {ex}");

        var after = _db.Rows("TAXDTL").Where(r => Str(r, "Taxid") == taxid).ToList();
        var fresh = after.Where(r => (Str(r, "REMARKS") ?? "").StartsWith("ResendDuplicate", StringComparison.Ordinal)).ToList();
        Check("ارسال مجدد: شماره مالیاتی تازه ساخته نشد — ردیف‌های تازه همان taxid را دارند (CLAUDE.md بخش ۴)",
              fresh.Count == before.Count && fresh.All(r => Str(r, "Taxid") == taxid), $"{fresh.Count} ردیف تازه / {before.Count}");
        int reqAfter = Stats()?["packets_per_send_request"]?.AsArray().Count ?? 0;
        var sent = Payloads()[taxid]!;
        Check("ارسال مجدد: payload عیناً همان taxid، inno و indatim/indati2m ِ ارسال اول است",
              reqAfter == reqBefore + 1 &&
              new[] { "taxid", "inno", "indatim", "indati2m", "tbill", "tvam", "tinb", "tob" }
                  .All(k => sent["header"]![k]?.ToJsonString() == origPayload["header"]![k]?.ToJsonString()),
              string.Join(" ", new[] { "inno", "indatim", "indati2m" }.Select(k => $"{k}:{origPayload["header"]![k]}→{sent["header"]![k]}")));

        // سرور: همان taxid دوباره → رد با 0300101 (تکراری)
        var newRef = Str(fresh.FirstOrDefault() ?? new(), "RefrenceNumber");
        var rec = MockByReference(newRef);
        Check("ارسال مجدد: سرور کامل همان شماره مالیاتی را با 0300101 (تکراری) رد کرد و اصلی دست‌نخورده ماند",
              rec != null && !rec.Value.Ok && rec.Value.Errors.Contains("0300101") && MockState()[taxid].Ok,
              rec == null ? "مرجع تازه روی سرور نیست" : string.Join(",", rec.Value.Errors));

        // همهٔ ستون‌ها، از جمله NUMBER و TAG
        var reset = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "IDD", "UID", "RefrenceNumber", "CRT", "TheStatus", "REMARKS", "ApiTypeSent", "TheError", "TheConfirmationReferenceId", "TheSuccess", "TheWarning" };
        var lost = new List<string>();
        var o = before.OrderBy(r => Num(r, "IDD")).ToList();
        var f = fresh.OrderBy(r => Num(r, "IDD")).ToList();
        for (int i = 0; i < Math.Min(o.Count, f.Count); i++)
            foreach (var col in o[i].Keys.Where(c => !reset.Contains(c)))
            {
                var a = Convert.ToString(o[i][col], CultureInfo.InvariantCulture);
                var b = Convert.ToString(f[i][col], CultureInfo.InvariantCulture);
                if (a != b) lost.Add($"{col}: {a ?? "null"}→{b ?? "null"}");
            }
        Check("ارسال مجدد: ردیف تازه همهٔ ستون‌ها را عیناً دارد، از جمله NUMBER و TAG (گرید پیگیری با NUMBER وصل می‌شود)",
              lost.Count == 0 && f.All(r => Num(r, "NUMBER") == number && Num(r, "TAG") == SendTag),
              string.Join(" ؛ ", lost.Distinct().Take(5)));
        Check("ارسال مجدد: ردیف تازه PENDING است، کد رهگیری تازه دارد و REMARKS با ResendDuplicate|کاربر نشانه‌دار است",
              f.All(r => Str(r, "TheStatus") == "PENDING" && Str(r, "RefrenceNumber") == newRef && newRef != Str(o[0], "RefrenceNumber") &&
                         (Str(r, "REMARKS") ?? "").StartsWith("ResendDuplicate|e2e|", StringComparison.Ordinal)), "");

        var est = new CL_ESTELAM(); est.GET_INIT_TAX(_sendUrl);
        est.GETESTELAM_REFCODE_UPDATE(newRef!);
        var rows = _db.Rows("TAXDTL").Where(r => Str(r, "Taxid") == taxid).ToList();
        Check("ارسال مجدد: استعلام ردیف‌های تازه را FAILED (0300101) و ردیف‌های اصلی را SUCCESS نگه داشت",
              rows.Where(r => Str(r, "RefrenceNumber") == newRef).All(r => Str(r, "TheStatus") == "FAILED" && (Str(r, "TheError") ?? "").Contains("0300101")) &&
              rows.Where(r => Str(r, "RefrenceNumber") != newRef).All(r => Str(r, "TheStatus") == "SUCCESS"),
              string.Join(",", rows.Select(r => Str(r, "TheStatus"))));
    }

    // ================================================================ f. خطای انتقال

    private static void F_Faults()
    {
        // ۱) گروهی: پاسخ گم شد (ثبت شده ولی result: [])
        Post("__config", new { fault_drop_result_next = 1 });
        var r1 = NewBulk(_ => false).SendAsync(new[] { 7951L, 7952, 7953 }, SendTag, 1, 1).GetAwaiter().GetResult();
        var reg = (Stats()?["registered_despite_fault"]?.AsArray() ?? new JsonArray())
                  .Where(x => x?["fault"]?.GetValue<string>() == "drop_result").Select(x => x!["taxid"]!.GetValue<string>()).ToList();
        var rows = _db.Rows("TAXDTL");
        Check("خطای انتقال (گروهی، پاسخ گم‌شده): هر taxid ثبت‌شده روی سرور در TAXDTL با وضعیت UNKNOWN هست (SendInvoiceBulk.cs:172، :931)",
              reg.Count == 3 && reg.All(t => rows.Where(r => Str(r, "Taxid") == t).ToList() is { Count: > 0 } g &&
                                             g.All(r => Str(r, "TheStatus") == "UNKNOWN" && Str(r, "RefrenceNumber") == null)),
              $"ثبت‌شده روی سرور: {reg.Count}؛ وضعیت‌ها: " + string.Join(",", reg.SelectMany(t => rows.Where(r => Str(r, "Taxid") == t).Select(r => Str(r, "TheStatus"))).Distinct()));
        Check("خطای انتقال (گروهی، پاسخ گم‌شده): به کاربر «ناموفق» گزارش شد (سرنوشت نامعلوم) — رفتار فعلی",
              r1.Success == 0 && r1.Failures.Count == 3, $"Success={r1.Success} Failures={r1.Failures.Count}");
        foreach (var t in reg) OracleExclude.Add(t);   // در payload هست ولی منبع ردیفی‌اش همین است؛ کنار گذاشتن ساده‌تر

        // ۲) گروهی: HTTP 500 (سرور ثبت نکرد)
        Post("__config", new { fault_http_500_next = 1 });
        var r2 = NewBulk(_ => false).SendAsync(new[] { 7961L, 7962 }, SendTag, 1, 1).GetAwaiter().GetResult();
        rows = _db.Rows("TAXDTL");
        var st2 = rows.Where(r => Num(r, "NUMBER") is 7961 or 7962).Select(r => Str(r, "TheStatus")).Distinct().ToList();
        Check("خطای انتقال (گروهی، HTTP 500): ردیف‌ها FAILED ثبت شدند — رفتار فعلی (نه UNKNOWN، چون بدنهٔ خطا رسید)",
              st2.SequenceEqual(new[] { "FAILED" }) && r2.Failures.Count == 2 && TaxidOf(7961) is { } t61 && !MockState().ContainsKey(t61),
              string.Join(",", st2));

        // ۳) تکی: پاسخ گم شد
        Post("__config", new { fault_drop_result_next = 1 });
        var t71 = SendSingle(7971, out var e71);
        var reg2 = (Stats()?["registered_despite_fault"]?.AsArray() ?? new JsonArray())
                   .Where(x => x?["fault"]?.GetValue<string>() == "drop_result").Select(x => x!["taxid"]!.GetValue<string>())
                   .Except(reg).ToList();
        Check("یافته: تکی با پاسخ گم‌شده → استثنا (TaxService.cs:227-241) و هیچ ردیفی در TAXDTL نیست، ولی سرور ثبتش کرده؛ ارسال دوباره taxid تازه می‌کشد (CL_MOADIAN.cs:366-381) → خطر ثبت دوباره. رفتار فعلی قفل شد",
              e71 != null && t71 == null && reg2.Count == 1 && MockState().TryGetValue(reg2[0], out var s71) && s71.Ok &&
              !_db.Rows("TAXDTL").Any(r => Num(r, "NUMBER") == 7971),
              $"استثنا={(e71 == null ? "نه" : Short(e71))} ثبت‌شده روی سرور={reg2.Count}");
        foreach (var t in reg2) OracleExclude.Add(t);

        // ۴) کد رهگیری مشترک + CL_ESTELAM
        Post("__config", new { fault_duplicate_reference = 1 });
        NewBulk(_ => true).SendAsync(new[] { 7981L, 7982 }, SendTag, 1, 1).GetAwaiter().GetResult();
        var state = MockState();
        rows = _db.Rows("TAXDTL");
        var ta = TaxidOf(7981); var tb = TaxidOf(7982);
        var refs = rows.Where(r => Num(r, "NUMBER") is 7981 or 7982).Select(r => Str(r, "RefrenceNumber")).Distinct().ToList();
        bool setup = ta != null && tb != null && refs.Count == 1 && state[ta].Ok && !state[tb].Ok;
        if (setup)
        {
            var est = new CL_ESTELAM(); est.GET_INIT_TAX(_sendUrl);
            est.GETESTELAM_REFCODE_UPDATE(refs[0]!);
        }
        rows = _db.Rows("TAXDTL");
        var sa = rows.Where(r => Num(r, "NUMBER") == 7981).Select(r => Str(r, "TheStatus")).Distinct().ToList();
        var sb = rows.Where(r => Num(r, "NUMBER") == 7982).Select(r => Str(r, "TheStatus")).Distinct().ToList();
        bool same = sa.Count == 1 && sb.Count == 1 && sa[0] == sb[0];
        bool oneWrong = same && ((sa[0] == "SUCCESS") != (sb[0] == "SUCCESS") || sa[0] != (state[ta!].Ok ? "SUCCESS" : "FAILED") || sb[0] != (state[tb!].Ok ? "SUCCESS" : "FAILED"));
        Check("یافته: کد رهگیری مشترک (یکی پذیرفته، یکی رد) → CL_ESTELAM هر دو را با یک حکم به‌روز می‌کند و یکی غلط ثبت می‌شود (TaxService.cs:267-269، CL_ESTELAM.cs:153-163). رفتار فعلی قفل شد",
              setup && same && oneWrong,
              $"سرور: 7981={(ta != null && state.TryGetValue(ta, out var x1) ? x1.Ok : (bool?)null)} 7982={(tb != null && state.TryGetValue(tb, out var x2) ? x2.Ok : (bool?)null)} ؛ TAXDTL: 7981={string.Join(",", sa)} 7982={string.Join(",", sb)} ؛ refs={refs.Count}");
        if (tb != null) OracleExclude.Add(tb);
        if (ta != null) OracleSource[ta] = ByNumber[7981].Lines;
    }

    // ================================================================ g. پیشگو

    private static void G_Oracle()
    {
        var all = Payloads();
        var chosen = new JsonObject();
        foreach (var kv in all)
            if (!OracleExclude.Contains(kv.Key)) chosen[kv.Key] = kv.Value?.DeepClone();
        var src = new JsonObject();
        foreach (var kv in OracleSource)
        {
            if (!chosen.ContainsKey(kv.Key)) continue;
            var arr = new JsonArray();
            foreach (var l in kv.Value)
                arr.Add(new JsonObject { ["am"] = l.Am, ["fee"] = l.Fee, ["dis"] = l.Dis, ["vra"] = l.Vra, ["odam"] = 0, ["olam"] = 0 });
            src[kv.Key] = arr;
        }
        string dir = Directory.GetCurrentDirectory();
        string pf = Path.Combine(dir, "e2e_payloads.json"), sf = Path.Combine(dir, "e2e_source.json");
        File.WriteAllText(pf, chosen.ToJsonString(), new UTF8Encoding(false));
        File.WriteAllText(sf, src.ToJsonString(), new UTF8Encoding(false));

        var oracle = Path.Combine(RepoRoot(), "TestTools", "LocalMoadian", "oracle_amounts.py");
        if (!File.Exists(oracle)) { Check("پیشگو: oracle_amounts.py موجود است", false, oracle); return; }
        var (code, stdout, stderr) = RunPython(oracle, "check", pf, "--source", sf, "--json");
        JsonNode? rep = null;
        try { rep = JsonNode.Parse(stdout); } catch { }
        var mism = rep?["mismatches"]?.AsArray() ?? new JsonArray();
        var div = rep?["known_divergences"]?.AsArray() ?? new JsonArray();
        int n = rep?["invoices"]?.GetValue<int>() ?? 0, skipped = rep?["skipped"]?.GetValue<int>() ?? 0;
        _line($"      · پیشگو: {n} صورتحساب ({skipped} ابطالیِ بی‌بدنه)، {src.Count} با ردیف خام دیتابیس، {mism.Count} ناهمخوانی، {div.Count} واگرایی شناخته‌شده");
        Check($"پیشگوی مستقل: صفر ناهمخوانیِ مبلغ روی {n} صورتحسابِ ارسالی (برش، جمع‌ها، cap+insp، و am/fee/dis/vra برابر ردیف خام دیتابیس)",
              code == 0 && rep != null && mism.Count == 0 && n >= 150,
              rep == null ? $"exit={code} {Short(stderr + stdout)}" :
              string.Join(" ؛ ", mism.Take(4).Select(m => $"{Short(m?["taxid"]?.ToString())} {m?["path"]} sent={m?["sent"]} exp={m?["expected"]} [{m?["rule"]}]")));
        Check("پیشگو: همهٔ فاکتورهای گروهی سالم با ردیف خام دیتابیس سنجیده شدند (نه فقط سازگاری درونی payload)",
              src.Count >= 140, $"{src.Count}");
    }

    private static (int code, string stdout, string stderr) RunPython(params string[] args)
    {
        var cands = new[] { Environment.GetEnvironmentVariable("MOADIAN_PYTHON"), "python3.12", "python3", "python" }
                    .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct();
        Exception? last = null;
        foreach (var py in cands)
        {
            try
            {
                var psi = new ProcessStartInfo(py!) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                foreach (var a in args) psi.ArgumentList.Add(a);
                psi.Environment["PYTHONIOENCODING"] = "utf-8";
                using var p = Process.Start(psi)!;
                var o = p.StandardOutput.ReadToEndAsync();
                var e = p.StandardError.ReadToEndAsync();
                p.WaitForExit(120_000);
                return (p.ExitCode, o.Result, e.Result);
            }
            catch (Exception ex) { last = ex; }
        }
        return (-1, "", "پایتون پیدا نشد: " + last?.Message);
    }

    // ================================================================ گزارش

    private static void Report()
    {
        _line("");
        _line($"  شکل‌های SQL که FakeDb در این اجرا دید ({_db.ShapeHits.Count}):");
        foreach (var kv in _db.ShapeHits) _line($"    {kv.Value,5} × {kv.Key}");
        var ddl = _db.DdlViolations.Select(v => v.Length > 160 ? v[..160] + " …" : v).Distinct().ToList();
        if (ddl.Count > 0)
        {
            _line("");
            _line("  ⚠ نقض NOT NULL طبق DDL ِ CL_ScriptUpdateDB.cs (اعمال نشد؛ طرح زندهٔ دیتابیس‌ها ممکن است فرق کند — با ‎-Full‎ بسنجید):");
            foreach (var v in ddl) _line("    ! " + v);
        }
        // کد تولید چند جا استثنای دیتابیس را می‌بلعد؛ هر خطای FakeDb باید این‌جا دیده شود.
        var unexpected = _db.Errors.Where(x => !x.StartsWith("Incorrect syntax", StringComparison.Ordinal)).ToList();
        Check("FakeDb: هیچ SQL ناشناخته/ستون ناموجود/تبدیل ناممکن رخ نداد — حتی در catch های بلعنده (تنها خطا همان «DEPATMAN = » ِ یافته است)",
              unexpected.Count == 0 && _db.Errors.Count == 1, string.Join(" ؛ ", unexpected.Take(3).Select(Short)));
    }

    // ================================================================ کمکی: سرور

    private readonly record struct MockRec(bool Ok, string Reference, string Uid, List<string> Errors, List<string> Warnings);

    private static Dictionary<string, MockRec> MockState()
    {
        var d = new Dictionary<string, MockRec>();
        var root = JsonNode.Parse(Get("__state"))!;
        foreach (var bucket in new[] { "rejected", "invoices" })
            foreach (var kv in root[bucket]?.AsObject() ?? new JsonObject())
            {
                var v = kv.Value!;
                var taxid = v["taxid"]?.GetValue<string>() ?? "";
                var rec = new MockRec(v["success"]!.GetValue<bool>(), v["reference"]?.GetValue<string>() ?? "",
                                      v["uid"]?.GetValue<string>() ?? "",
                                      v["errors"]!.AsArray().Select(e => e!["code"]!.GetValue<string>()).ToList(),
                                      v["warnings"]!.AsArray().Select(e => e!["code"]!.GetValue<string>()).ToList());
                // taxid تکراری (ارسال مجدد) در rejected هم هست؛ رکورد پذیرفته‌شده اولویت دارد
                if (!d.ContainsKey(taxid) || rec.Ok) d[taxid] = rec;
            }
        return d;
    }

    private static MockRec? MockByReference(string? reference)
    {
        if (reference == null) return null;
        var root = JsonNode.Parse(Get("__state"))!;
        foreach (var bucket in new[] { "rejected", "invoices" })
            foreach (var kv in root[bucket]?.AsObject() ?? new JsonObject())
                if (kv.Value?["reference"]?.GetValue<string>() == reference)
                    return new MockRec(kv.Value!["success"]!.GetValue<bool>(), reference, kv.Value["uid"]?.GetValue<string>() ?? "",
                                       kv.Value["errors"]!.AsArray().Select(e => e!["code"]!.GetValue<string>()).ToList(),
                                       kv.Value["warnings"]!.AsArray().Select(e => e!["code"]!.GetValue<string>()).ToList());
        return null;
    }

    private static string Codes(Dictionary<string, MockRec> state, string? taxid) =>
        taxid != null && state.TryGetValue(taxid, out var s)
            ? (s.Ok ? "پذیرفته" : "رد") + " " + string.Join(",", s.Errors.Concat(s.Warnings))
            : "(روی سرور نیست)";

    private static string Dash(string taxid)
    {
        var arr = JsonNode.Parse(Get("__invoice_status?taxIds=" + Uri.EscapeDataString(taxid)))?.AsArray();
        return arr?.FirstOrDefault()?["invoiceStatus"]?.GetValue<string>() ?? "?";
    }

    private static Dictionary<string, JsonNode?> Payloads() =>
        JsonNode.Parse(Get("__payloads"))!.AsObject().ToDictionary(kv => kv.Key, kv => kv.Value);

    private static JsonNode? Stats()
    {
        try { return JsonNode.Parse(Get("__stats")); } catch { return null; }
    }

    private static long ServerNow() => JsonNode.Parse(Get("__clock"))!["serverTime"]!.GetValue<long>();

    private static string Get(string path) => _http.GetStringAsync(_base + path).GetAwaiter().GetResult();

    private static JsonNode? Post(string path, object body)
    {
        var resp = _http.PostAsJsonAsync(_base + path, body).GetAwaiter().GetResult();
        var text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        try { return JsonNode.Parse(text); } catch { return null; }
    }

    // ================================================================ کمکی: دیتابیس ساختگی

    /// <summary>taxid آخرین ارسالِ این شماره فاکتور (از TAXDTL، نه از حافظهٔ تست).</summary>
    private static string? TaxidOf(long number) =>
        _db.Rows("TAXDTL").Where(r => Num(r, "NUMBER") == number && !string.IsNullOrEmpty(Str(r, "Taxid")))
           .OrderByDescending(r => Num(r, "IDD")).Select(r => Str(r, "Taxid")).FirstOrDefault();

    private static string? Str(Dictionary<string, object?> r, string c) =>
        r.TryGetValue(c, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;

    private static decimal? Num(Dictionary<string, object?> r, string c) =>
        r.TryGetValue(c, out var v) && v != null ? Convert.ToDecimal(v, CultureInfo.InvariantCulture) : null;

    private static bool? Bool(Dictionary<string, object?> r, string c) =>
        r.TryGetValue(c, out var v) && v != null ? Convert.ToBoolean(v, CultureInfo.InvariantCulture) : null;

    private static decimal D(JsonNode? n) =>
        n == null ? 0 : decimal.Parse(n.ToJsonString().Trim('"'), NumberStyles.Float, CultureInfo.InvariantCulture);

    // ================================================================ Thread.Sleep مسیر تکی

    private static bool _sleepPatched;

    private static void PatchSleep()
    {
        if (_sleepPatched || Environment.GetEnvironmentVariable("MOADIAN_E2E_REAL_SLEEP") == "1") return;
        var h = new Harmony("moadian.linux.shim.e2e.nosleep");
        h.Patch(AccessTools.Method(typeof(CL_MOADIAN), nameof(CL_MOADIAN.DoSendInvoice)),
                transpiler: new HarmonyMethod(typeof(EndToEnd), nameof(SleepTranspiler)));
        _sleepPatched = true;
    }

    private static IEnumerable<CodeInstruction> SleepTranspiler(IEnumerable<CodeInstruction> ins)
    {
        var sleep = AccessTools.Method(typeof(Thread), nameof(Thread.Sleep), new[] { typeof(int) });
        var noSleep = AccessTools.Method(typeof(EndToEnd), nameof(NoSleep));
        foreach (var i in ins)
            yield return i.Calls(sleep) ? new CodeInstruction(OpCodes.Call, noSleep).MoveLabelsFrom(i).MoveBlocksFrom(i) : i;
    }

    public static void NoSleep(int ms) { /* مسیر تکی ۱۰ ثانیه صبر می‌کند تا سامانه پردازش کند؛ سرور ساختگی فوری است */ }

    // ================================================================ کمکی: عمومی

    private static void Check(string name, bool ok, string detail) =>
        _check($"۲۹-{Fa(_n++)} سر تا ته: {name}", ok, detail);

    private static string Fa(int n) => string.Concat(n.ToString(CultureInfo.InvariantCulture).Select(c => (char)('۰' + (c - '0'))));

    private static string One(Exception ex)
    {
        var e = ex is AggregateException ae && ae.InnerException != null ? ae.InnerException : ex;
        if (e is TargetInvocationException tie && tie.InnerException != null) e = tie.InnerException;
        return e.GetType().Name + ": " + Short(e.Message);
    }

    private static string Short(string? s)
    {
        s = (s ?? "").Replace("\n", " ").Replace("\r", " ");
        return s.Length > 160 ? s[..160] + " …" : s;
    }

    internal static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, ".git"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("ریشهٔ مخزن (.git) پیدا نشد");
    }
}

/// <summary>
/// پیوند زدن منطق دکمهٔ «ارسال مجدد» از Prg_TrackSentInvoice/MainWindow.xaml.cs.
///
/// آن فایل WPF است و روی لینوکس کامپایل نمی‌شود. این‌جا با Roslyn فقط نحو (syntax)
/// آن خوانده می‌شود و این قطعه‌ها *بی هیچ تغییری در متن* در یک کلاس میزبان کامپایل
/// می‌شوند:
///   • حلقهٔ «foreach (var taxid in uniqueTaxids)» داخل RESEND_BTN_Click
///   • InsertNewTaxDtlRecord، CreateHeaderFromFullTaxDtl، CreateBodyListFromFullTaxDtl، TruncateString
/// میزبان فقط فیلدهایی را که آن متن لازم دارد فراهم می‌کند: dbms، Functions،
/// STATUS_LABEL (یک stub با Content)، successCount/failCount، و ورودی‌های taxService و
/// isMainApi و windowsUser. بخش‌های UIِ پیش و پس از حلقه (دیالوگ تأیید، لاگ AMALIAT با
/// SqlConnection خام، RefGetData) اجرا نمی‌شوند.
/// </summary>
internal static class ResendTransplant
{
    private static readonly string[] DropUsingPrefixes = { "NPOI", "Prg_Graphicy", "System.Windows", "System.Data.SqlClient", "Hardcodet" };

    public static (object? host, string? error) Build()
    {
        try
        {
            var file = Path.Combine(EndToEnd.RepoRoot(), "Prg_TrackSentInvoice", "MainWindow.xaml.cs");
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file));
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
            MethodDeclarationSyntax M(string name) =>
                methods.SingleOrDefault(m => m.Identifier.Text == name)
                ?? throw new InvalidOperationException("متد در سورس پیدا نشد: " + name);

            var loop = M("RESEND_BTN_Click").DescendantNodes().OfType<ForEachStatementSyntax>()
                        .SingleOrDefault(f => f.Expression.ToString() == "uniqueTaxids")
                        ?? throw new InvalidOperationException("حلقهٔ foreach (var taxid in uniqueTaxids) پیدا نشد");

            var usings = string.Join("\n", root.Usings.Where(u => !DropUsingPrefixes.Any(p =>
                (u.Name?.ToString() ?? "").StartsWith(p, StringComparison.Ordinal))).Select(u => u.ToString()));

            var src = new StringBuilder();
            src.AppendLine(usings);
            src.AppendLine("namespace TransplantedTrackSentInvoice {");
            src.AppendLine("public sealed class LabelStub { public object Content { get; set; } }");
            src.AppendLine("public sealed class ResendHost {");
            src.AppendLine("  CL_CCNNMANAGER dbms = new CL_CCNNMANAGER();");
            src.AppendLine("  CL_FUNTIONS Functions = new CL_FUNTIONS();");
            src.AppendLine("  LabelStub STATUS_LABEL = new LabelStub();");
            src.AppendLine("  public int successCount; public int failCount;");
            src.AppendLine("  public async System.Threading.Tasks.Task RunAsync(List<string> uniqueTaxids, TaxService taxService, bool isMainApi, string windowsUser) {");
            src.AppendLine(loop.ToString());
            src.AppendLine("  }");
            foreach (var name in new[] { "TruncateString", "InsertNewTaxDtlRecord", "CreateHeaderFromFullTaxDtl", "CreateBodyListFromFullTaxDtl" })
                src.AppendLine(M(name).ToString());
            src.AppendLine("}}");

            var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
            var refs = tpa.Select(p => MetadataReference.CreateFromFile(p)).ToList();
            var self = typeof(EndToEnd).Assembly.Location;
            if (!tpa.Contains(self)) refs.Add(MetadataReference.CreateFromFile(self));
            var comp = CSharpCompilation.Create("TransplantedResend_" + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(src.ToString(), new CSharpParseOptions(LanguageVersion.Latest)) },
                refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
            using var ms = new MemoryStream();
            var emit = comp.Emit(ms);
            if (!emit.Success)
                return (null, string.Join(" | ", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(5)));
            var asm = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(ms.ToArray()));
            var type = asm.GetType("TransplantedTrackSentInvoice.ResendHost")!;
            return (Activator.CreateInstance(type), null);
        }
        catch (Exception ex)
        {
            return (null, ex.GetType().Name + ": " + ex.Message);
        }
    }

    public static (int ok, int fail, string? error) Run(object host, List<string> taxids, TaxService tax, bool isMainApi, string windowsUser)
    {
        var t = host.GetType();
        try
        {
            var task = (Task)t.GetMethod("RunAsync")!.Invoke(host, new object[] { taxids, tax, isMainApi, windowsUser })!;
            task.GetAwaiter().GetResult();
        }
        catch (Exception ex) { return (-1, -1, ex.GetType().Name + ": " + ex.Message); }
        return ((int)t.GetField("successCount")!.GetValue(host)!, (int)t.GetField("failCount")!.GetValue(host)!, null);
    }
}
#else
namespace MoadianLocalTest;

/// <summary>گروه ۲۹ فقط در ساخت لینوکسی (TestTools/ShimBuild) کامل است؛ این‌جا فقط اعلام می‌کند.</summary>
internal static class EndToEnd
{
    public static void Run(string fullUrl, HttpClient http, string privateKey,
                           Action<string, bool, string> check, Action<string> line) =>
        line("  (گروه ۲۹ به FakeDb و Harmony نیاز دارد و فقط در ساخت لینوکسی TestTools/ShimBuild اجرا می‌شود)");
}
#endif
