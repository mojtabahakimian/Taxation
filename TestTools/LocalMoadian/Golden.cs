using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.Bulk;
using Prg_Moadian.Generaly;

namespace MoadianLocalTest;

/// <summary>
/// گروه ۱۹ — تست تثبیت رفتار (Golden / Characterization).
///
/// هدف: «چیزی که کار می‌کرده را خراب نکن».
///
/// روش کار ساده است. برای مجموعه‌ای از فاکتورهای واقعیِ دیتابیس، کد واقعی
/// اجرا می‌شود، payload دقیقی که به مؤدیان می‌رود از سرور ساختگی بیرون کشیده
/// می‌شود، مقادیر بی‌معنیِ متغیر (شماره مالیاتی تصادفی، امضا، کلید، uid …)
/// با placeholder جایگزین می‌شود، و نتیجه با فایل مرجع مقایسه می‌گردد.
///
/// اگر کسی کد را عوض کند و مبلغی، گردکردنی، فیلدی یا nullیی تغییر کند،
/// این تست دقیقا می‌گوید کدام فیلد کدام فاکتور عوض شده است.
///
///     ثبت مرجع جدید :  MoadianLocalTest.exe --bulk --record
///     مقایسه        :  MoadianLocalTest.exe --bulk
///
/// مرجع‌ها در TestTools/LocalMoadian/golden/*.json نگه‌داری می‌شوند و باید
/// کنار کد commit شوند — همان‌ها سند «این رفتار درست بود» هستند.
/// </summary>
internal static class Golden
{
    private const int SendTag = 77;
    private const int HeadTag = 88;

    /// <summary>فیلدهایی که هر اجرا عوض می‌شوند و معنایی برای درستی ندارند.</summary>
    private static readonly (string path, string placeholder, int? expectLen)[] Volatile_ =
    {
        ("header.taxid",    "<TAXID>",     22),   // Random.Shared — عمدی
        ("header.indati2m", "<INDATI2M>",  null), // زمان لحظه ارسال
    };

    public static string Dir =>
        Path.Combine(FindRepoRoot(), "TestTools", "LocalMoadian", "golden");

    public static void Run(string mockBaseUrl, HttpClient http, bool record,
                           Action<string, bool, string> check, Action<string> line)
    {
        var db = new CL_CCNNMANAGER();
        string savedMode = CL_Generaly.MrCorrect;
        Directory.CreateDirectory(Dir);

        try
        {
            CL_Generaly.MrCorrect = "m";                 // حالت ثابت و قابل تکرار

            var cases = PickCases(db);
            if (cases.Count == 0)
            {
                check("۱۹-۱ فاکتورهای مرجع پیدا شدند", false,
                      "seed_bulk_test.sql را اجرا کنید");
                return;
            }
            line($"      · {cases.Count} فاکتور مرجع: " +
                 string.Join("، ", cases.Select(c => c.label)));

            http.PostAsync(mockBaseUrl.TrimEnd('/').Replace("/sandbox", "") + "/__reset",
                           new StringContent("{}", Encoding.UTF8, "application/json"))
                .GetAwaiter().GetResult();

            db.DoExecuteSQL($"DELETE FROM dbo.TAXDTL WHERE TAG={SendTag} AND NUMBER BETWEEN 1000001 AND 1000120");

            var bulk = new SendInvoiceBulk(db, mockBaseUrl) { OnValidationWarning = _ => true };
            var result = bulk.SendAsync(cases.Select(c => c.number).ToList(),
                                        SendTag, inty_value: null, setm_value: null)
                             .GetAwaiter().GetResult();

            check("۱۹-۱ همه فاکتورهای مرجع ساخته و ارسال شدند",
                  result.Failures.Count == 0,
                  string.Join(" | ", result.Failures.Take(2).Select(f => f.Key + ":" + One(f.Value))));

            // payloadها را از سرور ساختگی بگیر و به شماره فاکتور نگاشت کن
            var payloads = Fetch(http, mockBaseUrl);
            var taxidByNumber = db.DoGetDataSQL<NumTax>(
                $"SELECT DISTINCT NUMBER, Taxid FROM dbo.TAXDTL WHERE TAG={SendTag}")
                .ToDictionary(r => (long)r.NUMBER, r => (r.Taxid ?? "").Trim());

            int compared = 0, changed = 0, recorded = 0, missing = 0;
            var report = new List<string>();

            foreach (var c in cases)
            {
                if (!taxidByNumber.TryGetValue(c.number, out var taxid) ||
                    !payloads.TryGetValue(taxid, out var node))
                {
                    check($"۱۹-x فاکتور {c.number} به سرور رسید", false, "payload پیدا نشد");
                    continue;
                }

                var normalized = Normalize(node, c);
                string text = normalized.ToJsonString(new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });

                string file = Path.Combine(Dir, $"{c.number}_{c.tag}.json");

                if (record)
                {
                    File.WriteAllText(file, text, new UTF8Encoding(false));
                    recorded++;
                    continue;
                }

                if (!File.Exists(file))
                {
                    // مرجع مفقود = شکست، نه فرصتی برای ساختن جواب از روی خروجی
                    // همین اجرا. اگر بی‌صدا ثبت شود، حذف تصادفی یک مرجع کنترل
                    // رگرسیون را برای همیشه بی‌اثر می‌کند.
                    missing++;
                    report.Add($"فاکتور {c.number}: فایل مرجع وجود ندارد — " +
                               "اگر عمدی است با --record ثبتش کنید");
                    continue;
                }

                string expected = File.ReadAllText(file);
                compared++;
                var diffs = Diff(expected, text);
                if (diffs.Count > 0)
                {
                    changed++;
                    report.Add($"فاکتور {c.number} ({c.label}): " +
                               string.Join("؛ ", diffs.Take(4)) +
                               (diffs.Count > 4 ? $" و {diffs.Count - 4} مورد دیگر" : ""));
                }
            }

            if (recorded > 0)
            {
                line($"      · {recorded} فایل مرجع نوشته شد در golden/");
                check("۱۹-۲ فایل‌های مرجع ثبت شدند", true, "");
            }

            check("۱۹-۲-الف هیچ فایل مرجعی مفقود نیست",
                  missing == 0, missing + " مرجع مفقود");

            if (compared > 0)
            {
                check($"۱۹-۲ payload هیچ‌کدام از {compared} فاکتور مرجع عوض نشده است",
                      changed == 0, string.Join("  ||  ", report));
                foreach (var r in report) line("      ✗ " + r);
            }

            // --- ساختارهایی که باید همیشه برقرار باشند، مستقل از مرجع ---
            StructuralInvariants(payloads.Values, check);
        }
        finally
        {
            CL_Generaly.MrCorrect = savedMode;
            db.DoExecuteSQL($"DELETE FROM dbo.TAXDTL WHERE TAG={SendTag} AND NUMBER BETWEEN 1000001 AND 1000120");
        }
    }

    // ---------------------------------------------------------------- انتخاب فاکتورها

    private sealed class NumTax { public double NUMBER { get; set; } public string Taxid { get; set; } }

    private sealed class Case
    {
        public long number; public int tag; public string label;
    }

    /// <summary>
    /// فاکتورهای مرجع را قطعی و تکرارپذیر انتخاب می‌کند: از داده تستیِ
    /// seed_bulk_test.sql، مرتب‌شده بر اساس شماره. چون داده تستی از یک فاکتور
    /// واقعی کلون شده، محتوایش واقعی است ولی به داده اصلی دست نمی‌زند.
    /// </summary>
    private static List<Case> PickCases(CL_CCNNMANAGER db)
    {
        var nums = db.DoGetDataSQL<double>(
            $@"SELECT TOP 6 NUMBER FROM dbo.HEAD_LST
               WHERE TAG={HeadTag} AND NUMBER>=1000001 ORDER BY NUMBER")
            .Select(x => (long)x).ToList();

        var labels = new[] { "پایه", "دوم", "سوم", "چهارم", "پنجم", "ششم" };
        return nums.Select((n, i) => new Case
        {
            number = n,
            tag = SendTag,
            label = i < labels.Length ? labels[i] : "#" + i
        }).ToList();
    }

    // ---------------------------------------------------------------- نرمال‌سازی

    private static JsonObject Normalize(JsonNode src, Case c)
    {
        var root = JsonNode.Parse(src.ToJsonString())!.AsObject();

        foreach (var (path, placeholder, expectLen) in Volatile_)
        {
            var parts = path.Split('.');
            var obj = root[parts[0]]?.AsObject();
            if (obj == null) continue;
            var cur = obj[parts[1]];
            string shape = "";
            if (expectLen.HasValue)
            {
                var v = cur?.GetValue<string>() ?? "";
                shape = v.Length == expectLen.Value ? $"(len={expectLen})" : $"(len={v.Length}!)";
            }
            obj[parts[1]] = placeholder + shape;
        }

        // شماره فاکتور را کنار payload نگه می‌داریم تا فایل مرجع خودتوضیح باشد
        root["__source"] = new JsonObject
        {
            ["number"] = c.number,
            ["tag"] = c.tag,
            ["label"] = c.label,
        };
        return root;
    }

    // ---------------------------------------------------------------- مقایسه

    /// <summary>تفاوت‌ها را به صورت «مسیر: قدیم → جدید» برمی‌گرداند.</summary>
    private static List<string> Diff(string expectedJson, string actualJson)
    {
        var outp = new List<string>();
        JsonNode a, b;
        try { a = JsonNode.Parse(expectedJson)!; b = JsonNode.Parse(actualJson)!; }
        catch { return new List<string> { "فایل مرجع قابل خواندن نیست" }; }
        Walk("", a, b, outp);
        return outp;
    }

    private static void Walk(string path, JsonNode? a, JsonNode? b, List<string> outp)
    {
        if (outp.Count > 40) return;

        if (a is JsonObject oa && b is JsonObject ob)
        {
            foreach (var k in oa.Select(x => x.Key).Union(ob.Select(x => x.Key)))
            {
                bool inA = oa.ContainsKey(k), inB = ob.ContainsKey(k);
                if (!inB) { outp.Add($"{path}{k} حذف شده"); continue; }
                if (!inA) { outp.Add($"{path}{k} اضافه شده"); continue; }
                Walk(path + k + ".", oa[k], ob[k], outp);
            }
            return;
        }

        if (a is JsonArray aa && b is JsonArray ab)
        {
            if (aa.Count != ab.Count)
            {
                outp.Add($"{path.TrimEnd('.')} تعداد {aa.Count} → {ab.Count}");
                return;
            }
            for (int i = 0; i < aa.Count; i++) Walk($"{path}[{i}].", aa[i], ab[i], outp);
            return;
        }

        string sa = a?.ToJsonString() ?? "null";
        string sb = b?.ToJsonString() ?? "null";
        if (sa != sb) outp.Add($"{path.TrimEnd('.')}: {Trim(sa)} → {Trim(sb)}");
    }

    private static string Trim(string s) => s.Length > 40 ? s.Substring(0, 40) + "…" : s;
    private static string One(string s) => (s ?? "").Replace("\n", " ").Trim() is { Length: > 80 } t
        ? t.Substring(0, 80) + "…" : (s ?? "").Replace("\n", " ").Trim();

    // ---------------------------------------------------------------- ثابت‌های ساختاری

    /// <summary>
    /// قواعدی که فارغ از فایل مرجع باید همیشه برقرار باشند. اگر کسی فایل‌های
    /// مرجع را بی‌فکر دوباره ثبت کند، این‌ها همچنان جلوی خرابی را می‌گیرند.
    /// </summary>
    private static void StructuralInvariants(IEnumerable<JsonNode> payloads,
                                             Action<string, bool, string> check)
    {
        var list = payloads.Select(p => p.AsObject()).ToList();
        if (list.Count == 0) return;

        bool AllHeaders(Func<JsonObject, bool> f) =>
            list.All(p => p["header"] is JsonObject h && f(h));

        check("۱۹-۳ شماره مالیاتی همه فاکتورها ۲۲ کاراکتر است",
              AllHeaders(h => (h["taxid"]?.GetValue<string>() ?? "").Length == 22), "");

        check("۱۹-۴ سریال همه فاکتورها ۱۰ کاراکتر است",
              AllHeaders(h => (h["inno"]?.GetValue<string>() ?? "").Length == 10), "");

        check("۱۹-۵ تاریخ صدور همیشه عدد میلی‌ثانیه است",
              AllHeaders(h => h["indatim"]?.GetValue<long>() > 1_000_000_000_000L), "");

        check("۱۹-۶ تاریخ ثبت هیچ‌وقت قبل از تاریخ صدور نیست",
              AllHeaders(h => h["indati2m"]!.GetValue<long>() >= h["indatim"]!.GetValue<long>()), "");

        check("۱۹-۷ مبلغ کل هر فاکتور با مجموع اقلامش می‌خواند",
              list.All(p =>
              {
                  var h = p["header"]!.AsObject();
                  double tbill = h["tbill"]?.GetValue<double>() ?? 0;
                  double sum = p["body"]!.AsArray()
                      .Sum(b => b!["tsstam"]?.GetValue<double>() ?? 0);
                  return Math.Abs(tbill - sum) <= 1;
              }), "");

        check("۱۹-۸ هیچ مبلغی اعشار ندارد (همه Truncate شده‌اند)",
              list.All(p => p["body"]!.AsArray().All(b =>
                     IsWhole(b!["tsstam"]) && IsWhole(b["vam"]) && IsWhole(b["adis"]))), "");

        check("۱۹-۹ هیچ قلمی بدون شناسه کالا یا واحد اندازه‌گیری نیست",
              list.All(p => p["body"]!.AsArray().All(b =>
                     !string.IsNullOrWhiteSpace(b!["sstid"]?.GetValue<string>()) &&
                     !string.IsNullOrWhiteSpace(b["mu"]?.GetValue<string>()))), "");

        check("۱۹-۱۰ قاعده ارسال (insr) همچنان خالی می‌رود",
              AllHeaders(h => h["insr"] == null), "");

        check("۱۹-۱۱ صورتحساب اصلی هیچ مرجعی ندارد",
              AllHeaders(h => h["ins"]!.GetValue<int>() != 1 || h["irtaxid"] == null), "");

        check("۱۹-۱۲ رابطه تسویه برقرار است (نقدی→insp صفر، نسیه→cap صفر)",
              AllHeaders(h =>
              {
                  int setm = h["setm"]?.GetValue<int>() ?? 0;
                  double cap = h["cap"]?.GetValue<double>() ?? 0;
                  double insp = h["insp"]?.GetValue<double>() ?? 0;
                  return setm switch
                  {
                      1 => insp == 0,
                      2 => cap == 0,
                      3 => cap > 0 && insp > 0,
                      _ => false,
                  };
              }), "");
    }

    private static bool IsWhole(JsonNode? n)
    {
        if (n is not JsonValue v || !v.TryGetValue<double>(out var d)) return true;
        return Math.Abs(d - Math.Truncate(d)) < 1e-9;
    }

    // ---------------------------------------------------------------- کمکی

    private static Dictionary<string, JsonNode> Fetch(HttpClient http, string baseUrl)
    {
        string url = baseUrl.TrimEnd('/').Replace("/sandbox", "") + "/__payloads";
        var json = http.GetStringAsync(url).GetAwaiter().GetResult();
        var obj = JsonNode.Parse(json)!.AsObject();
        return obj.ToDictionary(kv => kv.Key, kv => kv.Value!);
    }

    private static string FindRepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, ".git")))
            d = d.Parent;
        return d?.FullName ?? Directory.GetCurrentDirectory();
    }
}
