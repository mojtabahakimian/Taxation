using System.Diagnostics;
using Prg_Moadian.Bulk;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.FUNCTIONS;

namespace MoadianLocalTest;

/// <summary>
/// تست ارسال گروهی سنگین روی مسیر واقعی SendInvoiceBulk:
///   • جدول TAXDTL خالی
///   • شماره فاکتور بالای یک میلیون (سریال هگز)
///   • تعداد زیاد در یک اجرا (بسته‌بندی ۹۹تایی)
///
/// داده تستی: seed_bulk_test.sql  (TAG=77، NUMBER از 1000001)
/// پاک‌سازی:  cleanup_bulk_test.sql
/// </summary>
internal static class BulkStress
{
    private const int TestTag = 77;      // HTAG — مطابق HEAD_BACK_ANBAR
    private const int HeadTag = 88;      // HEAD_LST.TAG = HTAG + 11
    private const long FirstNumber = 1_000_001;

    public static void Run(string mockBaseUrl, Action<string, bool, string> check,
                           Action<string> line)
    {
        var db = new CL_CCNNMANAGER();

        long seeded = db.DoGetDataSQL<long>(
            $"SELECT COUNT(*) FROM dbo.HEAD_LST WHERE TAG={HeadTag} AND NUMBER>={FirstNumber}").First();
        check("۱۶-۱ داده تستی موجود است (seed_bulk_test.sql اجرا شده)", seeded > 0, $"{seeded} فاکتور");
        if (seeded == 0) return;

        // --- وضعیت جدول قبل از تست: دست‌نخورده می‌ماند و باید دست‌نخورده بماند ---
        long otherRowsBefore = db.DoGetDataSQL<long>(
            $"SELECT COUNT(*) FROM dbo.TAXDTL WHERE TAG<>{TestTag}").First();
        var existingInno = db.DoGetDataSQL<string>(
            $"SELECT DISTINCT Inno FROM dbo.TAXDTL WHERE TAG<>{TestTag} AND Inno IS NOT NULL")
            .Select(x => (x ?? "").Trim())
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingTaxid = db.DoGetDataSQL<string>(
            $"SELECT DISTINCT Taxid FROM dbo.TAXDTL WHERE TAG<>{TestTag} AND Taxid IS NOT NULL")
            .Select(x => (x ?? "").Trim())
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string checksumBefore = Checksum(db);
        line($"      · جدول قبل از تست: {otherRowsBefore:N0} ردیف، " +
             $"{existingInno.Count:N0} سریال و {existingTaxid.Count:N0} شماره مالیاتی موجود");

        // --- TAXDTL را برای این تگ خالی کن تا شرط «جدول پاک شده» برقرار باشد ---
        db.DoExecuteSQL($"DELETE FROM dbo.TAXDTL WHERE TAG={TestTag} AND NUMBER BETWEEN 1000001 AND 1000120");
        db.DoExecuteSQL($"DELETE FROM dbo.HEAD_LST_EXTENDED WHERE TGU={TestTag} AND NUMBER BETWEEN 1000001 AND 1000120");
        long before = db.DoGetDataSQL<long>(
            $"SELECT COUNT(*) FROM dbo.TAXDTL WHERE TAG={TestTag}").First();
        check("۱۶-۲ جدول TAXDTL برای این تگ خالی است", before == 0, before.ToString());

        var numbers = db.DoGetDataSQL<double>(
            $"SELECT NUMBER FROM dbo.HEAD_LST WHERE TAG={HeadTag} AND NUMBER>={FirstNumber} ORDER BY NUMBER")
            .Select(x => (long)x).ToList();

        line($"      · {numbers.Count} فاکتور، از {numbers.First():N0} تا {numbers.Last():N0}");

        // --- سریال‌های مورد انتظار: همه باید هگز باشند ---
        var fn = new CL_FUNTIONS();
        var expected = numbers.ToDictionary(n => n, n => fn.GenerateFixedLengthInno("1405", n));
        check("۱۶-۳ همه شماره‌ها بالای یک میلیون‌اند",
              numbers.All(n => n > 1_000_000), "");
        check("۱۶-۴ همه سریال‌ها هگز شدند (کاراکتر پنجم بین A و F)",
              expected.Values.All(v => v.Length == 10 && v[4] >= 'A' && v[4] <= 'F'),
              expected.Values.First());
        check("۱۶-۵ سریال‌های تولیدشده یکتا هستند",
              expected.Values.Distinct().Count() == expected.Count, "");

        // --- ارسال گروهی واقعی ---
        var bulk = new SendInvoiceBulk(db, mockBaseUrl)
        {
            OnValidationWarning = msg => { line("      ! هشدار: " + Flatten(msg)); return true; }
        };

        var sw = Stopwatch.StartNew();
        var result = bulk.SendAsync(numbers, TestTag, inty_value: 1, setm_value: 1)
                         .GetAwaiter().GetResult();
        sw.Stop();

        line($"      · زمان: {sw.Elapsed.TotalSeconds:F1} ثانیه — موفق {result.Success}، ناموفق {result.Failures.Count}");
        foreach (var f in result.Failures.Take(3))
            line($"      ! {f.Key}: {Flatten(f.Value)}");

        check("۱۶-۶ هیچ فاکتوری در مرحله ساخت رد نشد",
              result.Failures.Count == 0,
              string.Join(" | ", result.Failures.Take(2).Select(f => $"{f.Key}:{Flatten(f.Value)}")));

        // --- آنچه در دیتابیس ثبت شد ---
        long rows = db.DoGetDataSQL<long>(
            $"SELECT COUNT(*) FROM dbo.TAXDTL WHERE TAG={TestTag}").First();
        long distinctInvoices = db.DoGetDataSQL<long>(
            $"SELECT COUNT(DISTINCT NUMBER) FROM dbo.TAXDTL WHERE TAG={TestTag}").First();
        long distinctTaxid = db.DoGetDataSQL<long>(
            $"SELECT COUNT(DISTINCT Taxid) FROM dbo.TAXDTL WHERE TAG={TestTag}").First();
        long distinctInno = db.DoGetDataSQL<long>(
            $"SELECT COUNT(DISTINCT Inno) FROM dbo.TAXDTL WHERE TAG={TestTag}").First();

        line($"      · TAXDTL: {rows} ردیف، {distinctInvoices} فاکتور، " +
             $"{distinctTaxid} شماره مالیاتی، {distinctInno} سریال");

        check("۱۶-۷ همه فاکتورها در TAXDTL ثبت شدند",
              distinctInvoices == numbers.Count, $"{distinctInvoices}/{numbers.Count}");
        check("۱۶-۸ هیچ دو فاکتوری شماره مالیاتی یکسان نگرفتند",
              distinctTaxid == numbers.Count, $"{distinctTaxid}/{numbers.Count}");
        check("۱۶-۹ هیچ دو فاکتوری سریال یکسان نگرفتند (تداخل Inno)",
              distinctInno == numbers.Count, $"{distinctInno}/{numbers.Count}");

        // --- سریال ثبت‌شده باید دقیقا همان چیزی باشد که تابع تولید می‌کند ---
        var stored = db.DoGetDataSQL<InnoRow>(
            $"SELECT DISTINCT NUMBER, Inno FROM dbo.TAXDTL WHERE TAG={TestTag}").ToList();
        int mismatched = stored.Count(r =>
            !expected.TryGetValue((long)r.NUMBER, out var e) || (r.Inno ?? "").Trim() != e);
        check("۱۶-۱۰ سریال ثبت‌شده با سریال مورد انتظار یکی است", mismatched == 0,
              mismatched + " مورد اختلاف");

        check("۱۶-۱۱ شماره فاکتور در TAXDTL درست ثبت شد (نه null و نه ساختگی)",
              stored.All(r => r.NUMBER >= FirstNumber), "");

        // --- وضعیت و کد رهگیری ---
        long pending = db.DoGetDataSQL<long>(
            $"SELECT COUNT(*) FROM dbo.TAXDTL WHERE TAG={TestTag} AND TheStatus='PENDING'").First();
        long noRef = db.DoGetDataSQL<long>(
            $"SELECT COUNT(*) FROM dbo.TAXDTL WHERE TAG={TestTag} AND (RefrenceNumber IS NULL OR RefrenceNumber='')").First();
        check("۱۶-۱۲ همه ردیف‌ها وضعیت PENDING گرفتند", pending == rows, $"{pending}/{rows}");

        // رسید گرفتن یعنی «بسته رسید»، نه «صورتحساب پذیرفته شد». سامانه ساختگی
        // هم مثل واقعی برای ردشده‌ها کد رهگیری می‌دهد. پس نتیجه اعتبارسنجی
        // نهایی را مستقیم از خود سرور می‌پرسیم.
        var batchTaxids = db.DoGetDataSQL<string>(
            $"SELECT DISTINCT Taxid FROM dbo.TAXDTL WHERE TAG={TestTag}")
            .Select(x => (x ?? "").Trim()).Where(x => x.Length > 0).ToHashSet();
        var verdict = MockVerdict(batchTaxids, mockBaseUrl);
        line($"      · نتیجه اعتبارسنجی سرور: {verdict.accepted} پذیرفته، {verdict.rejected} رد" +
             (verdict.codes.Length > 0 ? $" ({verdict.codes})" : ""));
        check("۱۶-۱۲-ب سرور همه صورتحساب‌ها را واقعا پذیرفت (نه فقط رسید داد)",
              verdict.rejected == 0 && verdict.accepted == numbers.Count,
              $"{verdict.accepted}/{numbers.Count} پذیرفته، {verdict.rejected} رد — {verdict.codes}");
        check("۱۶-۱۳ همه ردیف‌ها کد رهگیری دارند", noRef == 0, noRef + " بدون کد رهگیری");

        long sandboxRows = db.DoGetDataSQL<long>(
            $"SELECT COUNT(*) FROM dbo.TAXDTL WHERE TAG={TestTag} AND ApiTypeSent=0").First();
        check("۱۶-۱۴ همه ردیف‌ها به عنوان سندباکس ثبت شدند (نه سامانه اصلی)",
              sandboxRows == rows, $"{sandboxRows}/{rows}");

        // --- بسته‌بندی: بیش از ۹۹ تا یعنی حداقل دو بسته ---
        long distinctUid = db.DoGetDataSQL<long>(
            $"SELECT COUNT(DISTINCT UID) FROM dbo.TAXDTL WHERE TAG={TestTag}").First();
        check("۱۶-۱۵ هر فاکتور UID مستقل خودش را دارد",
              distinctUid == numbers.Count, $"{distinctUid}/{numbers.Count}");

        // --- ارسال دوباره همان فاکتورها: شماره مالیاتی نباید تکرار شود ---
        var taxidsRun1 = db.DoGetDataSQL<string>(
            $"SELECT DISTINCT Taxid FROM dbo.TAXDTL WHERE TAG={TestTag}").ToList();

        var subset = numbers.Take(10).ToList();
        bulk.SendAsync(subset, TestTag, inty_value: 1, setm_value: 1).GetAwaiter().GetResult();

        var taxidsRun2 = db.DoGetDataSQL<string>(
            $"SELECT DISTINCT Taxid FROM dbo.TAXDTL WHERE TAG={TestTag}").ToList();
        check("۱۶-۱۶ ارسال دوباره، شماره مالیاتی تازه می‌سازد (تداخل با ارسال قبلی ندارد)",
              taxidsRun2.Count == taxidsRun1.Count + subset.Count,
              $"{taxidsRun1.Count} → {taxidsRun2.Count}");

        long innoRepeat = db.DoGetDataSQL<long>($@"
            SELECT COUNT(*) FROM (
                SELECT Inno FROM dbo.TAXDTL WHERE TAG={TestTag}
                GROUP BY Inno HAVING COUNT(DISTINCT NUMBER) > 1
            ) q").First();
        check("۱۶-۱۷ هیچ سریالی بین دو فاکتور مختلف مشترک نیست", innoRepeat == 0,
              innoRepeat + " سریال مشترک");

        // --- برخورد با داده‌ای که از قبل در جدول بود ---
        var newInno = db.DoGetDataSQL<string>(
            $"SELECT DISTINCT Inno FROM dbo.TAXDTL WHERE TAG={TestTag}")
            .Select(x => (x ?? "").Trim()).ToList();
        var newTaxid = db.DoGetDataSQL<string>(
            $"SELECT DISTINCT Taxid FROM dbo.TAXDTL WHERE TAG={TestTag}")
            .Select(x => (x ?? "").Trim()).ToList();

        var innoClash = newInno.Where(existingInno.Contains).ToList();
        check("۱۶-۱۸ هیچ سریال جدیدی با سریال‌های قبلیِ جدول تداخل ندارد",
              innoClash.Count == 0,
              innoClash.Count + " مورد: " + string.Join(",", innoClash.Take(3)));

        var taxidClash = newTaxid.Where(existingTaxid.Contains).ToList();
        check("۱۶-۱۹ هیچ شماره مالیاتی جدیدی با شماره‌های قبلی تداخل ندارد",
              taxidClash.Count == 0,
              taxidClash.Count + " مورد: " + string.Join(",", taxidClash.Take(3)));

        // --- دروازه «فقط اگر مالیات ذخیره‌شده > صفر» ---
        // مسیر تکی و گروهی مالیات را فقط وقتی بازمحاسبه می‌کنند که مقدار
        // ذخیره‌شده بزرگتر از صفر باشد؛ وگرنه صفر می‌ماند حتی اگر نرخ غیرصفر
        // باشد. این عمدی است و هزاران صورتحساب با همین رفتار رفته‌اند.
        //
        // این سناریو جدا از دسته اصلی اجرا می‌شود چون نتیجه‌اش یک صورتحساب
        // عمدا ناسازگار است و نباید تست «همه پذیرفته شدند» را قرمز کند.
        CheckVatGate(db, mockBaseUrl, check, line);

        // در UI می‌شود روش تسویه را انتخاب کرد و نوع صورتحساب را روی
        // «خواندن از اطلاعات خود فاکتور» گذاشت. قبلاً این ترکیب با
        // (int)Inty_Value روی null کرش می‌کرد و فاکتور ارسال نمی‌شد.
        CheckSetmWithoutInty(db, mockBaseUrl, check, line);

        // فاکتوری که ارسال زنده دارد، بی‌پرسش دوباره با شماره مالیاتی تازه نمی‌رود.
        CheckDuplicateGuard(db, mockBaseUrl, check, line);

        // --- داده قبلی نباید دست خورده باشد ---
        long otherRowsAfter = db.DoGetDataSQL<long>(
            $"SELECT COUNT(*) FROM dbo.TAXDTL WHERE TAG<>{TestTag}").First();
        check("۱۶-۲۰ ردیف‌های قبلی جدول دست نخوردند",
              otherRowsAfter == otherRowsBefore, $"{otherRowsBefore:N0} → {otherRowsAfter:N0}");

        // شمردن ردیف کافی نیست — محتوا هم باید دست‌نخورده باشد.
        string checksumAfter = Checksum(db);
        check("۱۶-۲۱ محتوای ردیف‌های قبلی هم دست نخورده (نه فقط تعدادشان)",
              checksumAfter == checksumBefore,
              checksumBefore == checksumAfter ? "" : $"{checksumBefore} → {checksumAfter}");
    }

    /// <summary>نتیجه اعتبارسنجی واقعی سرور ساختگی، نه صرفا رسید دریافت.</summary>
    private static (int accepted, int rejected, string codes) MockVerdict(HashSet<string> taxids, string baseUrl)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var json = http.GetStringAsync(baseUrl.TrimEnd('/').Replace("/sandbox", "") + "/__state")
                           .GetAwaiter().GetResult();
            using var doc = System.Text.Json.JsonDocument.Parse(json);

            int ok = doc.RootElement.GetProperty("invoices").EnumerateObject()
                        .Count(p => taxids.Contains(p.Name));

            var bad = new List<string>();
            int rejected = 0;
            if (doc.RootElement.TryGetProperty("rejected", out var rej))
                foreach (var r in rej.EnumerateObject())
                {
                    var t = r.Value.GetProperty("taxid").GetString() ?? "";
                    if (!taxids.Contains(t)) continue;      // مال دسته‌های دیگر
                    rejected++;
                    foreach (var e in r.Value.GetProperty("errors").EnumerateArray())
                        bad.Add(e.GetProperty("code").GetString()!);
                }
            return (ok, rejected, string.Join(",", bad.Distinct().Take(4)));
        }
        catch (Exception e) { return (-1, -1, e.Message); }
    }

    /// <summary>
    /// اثر انگشت محتوای ردیف‌های غیرتستی. اگر حتی یک مبلغ یا وضعیت عوض شود،
    /// این عدد فرق می‌کند — برخلاف شمردن ردیف که فقط حذف و درج را می‌بیند.
    /// </summary>
    private static string Checksum(CL_CCNNMANAGER db) =>
        db.DoGetDataSQL<long>($@"
            SELECT ISNULL(SUM(CAST(CHECKSUM(Taxid, Inno, Irtaxid, Ins, Tinb,
                                            Tbill, Tvam, Tadis, TheStatus,
                                            RefrenceNumber, NUMBER, TAG) AS BIGINT)), 0)
            FROM dbo.TAXDTL WHERE TAG <> {TestTag}").First().ToString();

    /// <summary>
    /// یک صورتحساب با نرخ مالیات غیرصفر ولی مالیات ذخیره‌شده صفر می‌فرستد و
    /// تایید می‌کند که برنامه مالیات را بازمحاسبه *نمی‌کند* — رفتار فعلی.
    /// </summary>
    private static void CheckVatGate(CL_CCNNMANAGER db, string url,
                                     Action<string, bool, string> check, Action<string> line)
    {
        const long gateNumber = 1_000_120;           // آخرین شماره دسته
        try
        {
            db.DoExecuteSQL($@"UPDATE dbo.INVO_LST SET IMBAA = 0
                               WHERE TAG={TestTag} AND NUMBER={gateNumber}");
            db.DoExecuteSQL($@"DELETE FROM dbo.TAXDTL
                               WHERE TAG={TestTag} AND NUMBER={gateNumber}");

            var bulk = new SendInvoiceBulk(db, url) { OnValidationWarning = _ => true };
            bulk.SendAsync(new[] { gateNumber }, TestTag, inty_value: 1, setm_value: 1)
                .GetAwaiter().GetResult();

            long taxedLinesWithZeroVat = db.DoGetDataSQL<long>($@"
                SELECT COUNT(*) FROM dbo.TAXDTL
                WHERE TAG={TestTag} AND NUMBER={gateNumber}
                  AND ISNULL(Vra,0) > 0 AND ISNULL(Vam,0) = 0").First();

            line($"      · دروازه مالیات: {taxedLinesWithZeroVat} قلم با نرخ غیرصفر و مالیات صفر رفت");
            check("۱۶-۱۹-ب مالیات ذخیره‌شده صفر بازمحاسبه نمی‌شود (رفتار عمدی)",
                  taxedLinesWithZeroVat > 0,
                  taxedLinesWithZeroVat == 0
                      ? "مالیات بازمحاسبه شد — رفتار مسیر گروهی عوض شده است"
                      : "");
        }
        catch (Exception e)
        {
            check("۱۶-۱۹-ب دروازه مالیات", false, Flatten(e.Message));
        }
    }

    /// <summary>
    /// روش تسویه از UI، نوع صورتحساب از خود فاکتور (inty_value = null).
    /// </summary>
    private static void CheckSetmWithoutInty(CL_CCNNMANAGER db, string url,
                                             Action<string, bool, string> check, Action<string> line)
    {
        const long number = 1_000_119;
        try
        {
            db.DoExecuteSQL($@"DELETE FROM dbo.TAXDTL
                               WHERE TAG={TestTag} AND NUMBER={number}");
            // نوع ذخیره‌شده ۲ است، نه ۱ که مقدار پیش‌فرض هم هست؛ وگرنه تست نمی‌تواند
            // «خواندن از خود فاکتور» را از «همیشه ۱» تشخیص دهد.
            db.DoExecuteSQL($@"UPDATE dbo.HEAD_LST_EXTENDED SET inty = 2
                               WHERE TGU={TestTag} AND NUMBER={number}");

            var bulk = new SendInvoiceBulk(db, url) { OnValidationWarning = _ => true };
            var result = bulk.SendAsync(new[] { number }, TestTag, inty_value: null, setm_value: 1)
                             .GetAwaiter().GetResult();

            long pending = db.DoGetDataSQL<long>($@"
                SELECT COUNT(*) FROM dbo.TAXDTL
                WHERE TAG={TestTag} AND NUMBER={number}
                  AND TheStatus='PENDING' AND Inty=2 AND Setm=1").First();

            line($"      · تسویه بدون نوع: موفق {result.Success}، ناموفق {result.Failures.Count}، {pending} ردیف PENDING");
            check("۱۶-۲۲ روش تسویه بدون نوع صورتحساب ارسال می‌شود (نوع از خود فاکتور)",
                  result.Success == 1 && result.Failures.Count == 0 && pending > 0,
                  string.Join(" | ", result.Failures.Select(f => $"{f.Key}:{Flatten(f.Value)}")));
        }
        catch (Exception e)
        {
            check("۱۶-۲۲ روش تسویه بدون نوع صورتحساب", false, Flatten(e.Message));
        }
        finally
        {
            db.DoExecuteSQL($@"UPDATE dbo.HEAD_LST_EXTENDED SET inty = 1
                               WHERE TGU={TestTag} AND NUMBER={number}");
        }
    }

    /// <summary>
    /// در یزدسپار ۸۸ فاکتور دو بار با شماره مالیاتی متفاوت «موفق» شدند، چون هیچ
    /// مسیری نمی‌پرسید «این فاکتور قبلاً رفته». این تست روی 1000119 که
    /// CheckSetmWithoutInty همین الان فرستاده (وضعیت PENDING) اجرا می‌شود.
    /// </summary>
    private static void CheckDuplicateGuard(CL_CCNNMANAGER db, string url,
                                            Action<string, bool, string> check, Action<string> line)
    {
        const long number = 1_000_119;
        string where = $"TAG={TestTag} AND NUMBER={number}";
        long Taxids() => db.DoGetDataSQL<long>(
            $"SELECT COUNT(DISTINCT Taxid) FROM dbo.TAXDTL WHERE {where}").First();

        try
        {
            var prior = MoadianRules.FindLiveOriginals(db, new[] { number }, TestTag, isMainApi: false);
            check("۱۶-۲۳ ارسال قبلیِ در صف، زنده شناخته می‌شود",
                  prior.Count == 1 && prior[0].TheStatus == "PENDING",
                  string.Join(",", prior.Select(p => p.TheStatus)));

            check("۱۶-۲۴ ارسال روی سامانه اصلی با سندباکس قاطی نمی‌شود",
                  MoadianRules.FindLiveOriginals(db, new[] { number }, TestTag, isMainApi: true).Count == 0, "");

            // --- کاربر «لغو ارسال» می‌زند ---
            long before = Taxids();
            int asked = 0;
            var bulk = new SendInvoiceBulk(db, url) { OnValidationWarning = msg => { if (msg.Contains("قبلاً")) asked++; return false; } };
            var r = bulk.SendAsync(new[] { number }, TestTag, inty_value: 1, setm_value: 1).GetAwaiter().GetResult();
            check("۱۶-۲۵ با «لغو ارسال» فاکتورِ ارسال‌شده دوباره نمی‌رود",
                  asked == 1 && r.Success == 0 && r.Failures.ContainsKey(number) && Taxids() == before,
                  $"پرسش {asked}، موفق {r.Success}، شماره‌ها {before}→{Taxids()}");

            // --- کاربر «ادامه و ارسال» می‌زند: رفتار قبلی، شماره مالیاتی تازه ---
            bulk = new SendInvoiceBulk(db, url) { OnValidationWarning = _ => true };
            r = bulk.SendAsync(new[] { number }, TestTag, inty_value: 1, setm_value: 1).GetAwaiter().GetResult();
            check("۱۶-۲۶ با «ادامه و ارسال» مثل قبل ارسال می‌شود",
                  r.Success == 1 && Taxids() == before + 1, $"شماره‌ها {before}→{Taxids()}");

            // --- ابطال‌شده دیگر زنده نیست ---
            var live = db.DoGetDataSQL<string>($"SELECT DISTINCT Taxid FROM dbo.TAXDTL WHERE {where}").ToList();
            foreach (var t in live)
                db.DoExecuteSQL($@"INSERT INTO dbo.TAXDTL (Ins, Irtaxid, Tinb, TheStatus, NUMBER, TAG, IDD, CRT, ApiTypeSent)
                                   VALUES (3, @T, N'', 'SUCCESS', {number}, {TestTag}, @IDD, GETDATE(), 0)",
                                new { T = t, IDD = new CL_FUNTIONS().GetNewIDD() });
            check("۱۶-۲۷ فاکتورِ ابطال‌شده دوباره قابل ارسال است (هشدار نمی‌دهد)",
                  MoadianRules.FindLiveOriginals(db, new[] { number }, TestTag, false).Count == 0, "");
            db.DoExecuteSQL($"DELETE FROM dbo.TAXDTL WHERE {where} AND Ins=3");

            // --- ردشده هم زنده نیست ---
            db.DoExecuteSQL($"UPDATE dbo.TAXDTL SET TheStatus='FAILED' WHERE {where}");
            check("۱۶-۲۸ فاکتورِ ردشده دوباره قابل ارسال است (هشدار نمی‌دهد)",
                  MoadianRules.FindLiveOriginals(db, new[] { number }, TestTag, false).Count == 0, "");
        }
        catch (Exception e)
        {
            check("۱۶-۲۳ نگهبان ارسال تکراری", false, Flatten(e.Message));
        }
    }

    private sealed class InnoRow
    {
        public double NUMBER { get; set; }
        public string Inno { get; set; }
    }

    private static string Flatten(string s) =>
        (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim() is { Length: > 110 } t
            ? t.Substring(0, 110) + "…"
            : (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
}
