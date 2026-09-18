using Prg_Moadian.Bulk;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.Generaly;

namespace MoadianLocalTest;

/// <summary>
/// گروه ۱۸ — این کد برای دو محصول مختلف کار می‌کند: MrCorrect و DenaFaraz.
///
/// سوییچ با یک متغیر است:
///     CL_Generaly.MrCorrect = "m"   →  محصول MrCorrect  (پیش‌فرض)
///     CL_Generaly.MrCorrect = "d"   →  محصول DenaFaraz  (کلید لایسنس تشخیص می‌دهد)
///
/// و چون SendInvoiceBulk.CALLER_NAME همیشه "m" است، شرط
///     CALLER_NAME == CL_Generaly.MrCorrect
/// در عمل یعنی «آیا DenaFaraz نیستیم».
///
/// نتیجه‌اش دو مسیر داده کاملا متفاوت است:
///     MrCorrect  →  از VIEW ‏HEAD_BACK_ANBAR می‌خواند (HEAD_LST.TAG - 11)
///     DenaFaraz  →  مستقیم از HEAD_LST می‌خواند
///
/// معنای تگ‌ها:
///     TAG 2  = حواله انبار فروش  (ردیف‌های کالا اینجاست — همین به مؤدیان می‌رود)
///     TAG 13 = سرگروه فاکتور فروش (فقط سرصفحه، بدون ردیف کالا)
///     MrCorrect هر دو را دارد، DenaFaraz فقط 2 را.
/// </summary>
internal static class TwoProducts
{
    private const int SendTag = 77;   // tag که به SendAsync داده می‌شود
    private const int HeadTag = 88;   // HEAD_LST.TAG برای مسیر MrCorrect  (88 - 11 = 77)
    private const long FirstNumber = 1_000_001;

    public static void Run(string mockBaseUrl, Action<string, bool, string> check,
                           Action<string> line)
    {
        var db = new CL_CCNNMANAGER();
        string savedMode = CL_Generaly.MrCorrect;

        try
        {
            // ---------- سوییچ محصول ----------
            check("۱۸-۱ حالت پیش‌فرض MrCorrect است", savedMode == "m", savedMode);

            var numbers = db.DoGetDataSQL<double>(
                $"SELECT NUMBER FROM dbo.HEAD_LST WHERE TAG={HeadTag} AND NUMBER>={FirstNumber} ORDER BY NUMBER")
                .Select(x => (long)x).Take(20).ToList();

            if (numbers.Count == 0)
            {
                check("۱۸-۲ داده تستی موجود است", false, "seed_bulk_test.sql را اجرا کنید");
                return;
            }

            // ---------- محصول ۱ : MrCorrect ----------
            CL_Generaly.MrCorrect = "m";
            db.DoExecuteSQL($"DELETE FROM dbo.TAXDTL WHERE TAG={SendTag} AND NUMBER BETWEEN 1000001 AND 1000120");
            var mr = SendBatch(db, mockBaseUrl, numbers, line);
            check("۱۸-۲ MrCorrect : همه فاکتورها ساخته و ارسال شدند",
                  mr.failures == 0, mr.firstError);
            check("۱۸-۳ MrCorrect : از مسیر HEAD_BACK_ANBAR داده خواند",
                  mr.rows > 0, mr.rows.ToString());
            var mrRows = Snapshot(db);

            // ---------- محصول ۲ : DenaFaraz ----------
            CL_Generaly.MrCorrect = "d";
            db.DoExecuteSQL($"DELETE FROM dbo.TAXDTL WHERE TAG={SendTag} AND NUMBER BETWEEN 1000001 AND 1000120");
            var dn = SendBatch(db, mockBaseUrl, numbers, line);
            check("۱۸-۴ DenaFaraz : همه فاکتورها ساخته و ارسال شدند",
                  dn.failures == 0, dn.firstError);
            check("۱۸-۵ DenaFaraz : از مسیر ساده HEAD_LST داده خواند",
                  dn.rows > 0, dn.rows.ToString());
            var dnRows = Snapshot(db);

            // ---------- دو مسیر باید محتوای یکسان بسازند ----------
            check("۱۸-۶ هر دو محصول تعداد ردیف یکسان تولید کردند",
                  mr.rows == dn.rows, $"MrCorrect={mr.rows} / DenaFaraz={dn.rows}");

            check("۱۸-۷ سریال صورتحساب در هر دو محصول یکسان است",
                  SameSerials(mrRows, dnRows), Diff(mrRows, dnRows, r => r.Inno));

            check("۱۸-۸ مبلغ کل در هر دو محصول یکسان است",
                  SameAmounts(mrRows, dnRows), Diff(mrRows, dnRows, r => r.Tbill.ToString()));

            check("۱۸-۹ شماره مالیاتی در دو اجرا متفاوت است (تصادفی بودن حفظ شده)",
                  !mrRows.Select(r => r.Taxid).Intersect(dnRows.Select(r => r.Taxid)).Any(), "");

            // ---------- محافظ «قبلاً ارسال شده» در هر دو محصول ----------
            CheckAlreadySentGuard(db, check, line);
        }
        finally
        {
            CL_Generaly.MrCorrect = savedMode;
            db.DoExecuteSQL($"DELETE FROM dbo.TAXDTL WHERE TAG={SendTag} AND NUMBER BETWEEN 1000001 AND 1000120");
        }
    }

    // ---------------------------------------------------------------- کمکی

    private sealed class Row
    {
        public double NUMBER { get; set; }
        public string Inno { get; set; }
        public string Taxid { get; set; }
        public double Tbill { get; set; }
    }

    private static (int failures, int rows, string firstError) SendBatch(
        CL_CCNNMANAGER db, string url, List<long> numbers, Action<string> line)
    {
        var bulk = new SendInvoiceBulk(db, url)
        {
            OnValidationWarning = _ => true
        };
        var result = bulk.SendAsync(numbers, SendTag, inty_value: 1, setm_value: 1)
                         .GetAwaiter().GetResult();

        int rows = (int)db.DoGetDataSQL<long>(
            $"SELECT COUNT(*) FROM dbo.TAXDTL WHERE TAG={SendTag}").First();

        string mode = CL_Generaly.MrCorrect == "m" ? "MrCorrect" : "DenaFaraz";
        line($"      · {mode}: موفق {result.Success}، ناموفق {result.Failures.Count}، " +
             $"{rows} ردیف در TAXDTL");

        string err = result.Failures.Count == 0
            ? ""
            : $"{result.Failures.First().Key}: " +
              result.Failures.First().Value.Replace("\n", " ").Trim();
        return (result.Failures.Count, rows, err);
    }

    private static List<Row> Snapshot(CL_CCNNMANAGER db) =>
        db.DoGetDataSQL<Row>(
            $"SELECT DISTINCT NUMBER, Inno, Taxid, Tbill FROM dbo.TAXDTL WHERE TAG={SendTag}")
          .OrderBy(r => r.NUMBER).ToList();

    private static bool SameSerials(List<Row> a, List<Row> b) =>
        a.Count == b.Count &&
        a.Zip(b).All(p => (p.First.Inno ?? "").Trim() == (p.Second.Inno ?? "").Trim());

    private static bool SameAmounts(List<Row> a, List<Row> b) =>
        a.Count == b.Count && a.Zip(b).All(p => p.First.Tbill == p.Second.Tbill);

    private static string Diff(List<Row> a, List<Row> b, Func<Row, string> pick)
    {
        if (a.Count != b.Count) return $"{a.Count} در برابر {b.Count} ردیف";
        var d = a.Zip(b).FirstOrDefault(p => pick(p.First) != pick(p.Second));
        return d.First == null ? "" : $"{pick(d.First)} در برابر {pick(d.Second)}";
    }

    /// <summary>
    /// محافظ «قبلاً ارسال شده» در Prg_Grpsend روی TAXDTL.TAG فیلتر می‌زند.
    /// TAXDTL همیشه تگ حواله (2) را ذخیره می‌کند، ولی گرید MrCorrect تگ
    /// سرگروه (13) را می‌داد. در DenaFaraz این دو یکی‌اند، در MrCorrect نه.
    /// </summary>
    private static void CheckAlreadySentGuard(CL_CCNNMANAGER db,
                                              Action<string, bool, string> check,
                                              Action<string> line)
    {
        // داده واقعی این دیتابیس: MrCorrect، سرصفحه TAG=13، TAXDTL.TAG=2
        long headRows = db.DoGetDataSQL<long>(
            "SELECT COUNT(*) FROM dbo.HEAD_LST WHERE TAG=13").First();
        if (headRows == 0) return;

        // آنچه کد *قبل* از اصلاح می‌کرد: مقایسه با تگ سرصفحه
        long oldWay = db.DoGetDataSQL<long>(@"
            SELECT COUNT(*) FROM dbo.HEAD_LST H
            WHERE H.TAG = 13 AND EXISTS (
                SELECT 1 FROM dbo.TAXDTL T
                WHERE T.NUMBER = H.NUMBER AND T.TAG = H.TAG
                  AND T.Ins = 1 AND T.TheStatus IN ('SUCCESS','PENDING','UNKNOWN'))").First();

        // آنچه کد *بعد* از اصلاح می‌کند: مقایسه با تگ ذخیره‌شده در TAXDTL
        long newWay = db.DoGetDataSQL<long>(@"
            SELECT COUNT(*) FROM dbo.HEAD_LST H
            WHERE H.TAG = 13 AND EXISTS (
                SELECT 1 FROM dbo.TAXDTL T
                WHERE T.NUMBER = H.NUMBER AND T.TAG = 2
                  AND T.Ins = 1 AND T.TheStatus IN ('SUCCESS','PENDING','UNKNOWN'))").First();

        line($"      · MrCorrect: از {headRows:N0} فاکتور سرصفحه — " +
             $"فیلتر فعلی {oldWay:N0} تا را «ارسال‌شده» می‌شناسد؛ با تگ حواله {newWay:N0} تا می‌شد");

        // این تست چیزی را «درست» نمی‌خواند — فقط عدد را ثبت می‌کند.
        // رفتار فعلی آگاهانه نگه داشته شده (لیست همه فاکتورها را نشان می‌دهد).
        // اگر روزی تصمیم گرفتید فیلتر را فعال کنید، عدد دوم می‌گوید چند فاکتور
        // از لیست کنار می‌روند.
        check("۱۸-۱۰ رفتار فیلتر MrCorrect همان چیزی است که بوده",
              oldWay == 0 && newWay > 0,
              $"فیلتر فعلی {oldWay} / با تگ حواله {newWay}");

        check("۱۸-۱۱ فاکتورهای ناموفق در هر دو حالت در لیست می‌مانند",
              db.DoGetDataSQL<long>(@"
                  SELECT COUNT(*) FROM dbo.HEAD_LST H
                  WHERE H.TAG = 13 AND EXISTS (
                      SELECT 1 FROM dbo.TAXDTL T
                      WHERE T.NUMBER = H.NUMBER AND T.TAG = 2
                        AND T.TheStatus = 'FAILED'
                        AND NOT EXISTS (SELECT 1 FROM dbo.TAXDTL S
                                        WHERE S.NUMBER = T.NUMBER AND S.TAG = T.TAG
                                          AND S.TheStatus IN ('SUCCESS','PENDING','UNKNOWN')))").First() > 0,
              "");

        // DenaFaraz: سرصفحه و TAXDTL هر دو TAG=2 → اصلاح چیزی را خراب نمی‌کند
        long dnHead = db.DoGetDataSQL<long>(
            "SELECT COUNT(*) FROM dbo.HEAD_LST WHERE TAG=2").First();
        long dnMatched = db.DoGetDataSQL<long>(@"
            SELECT COUNT(*) FROM dbo.HEAD_LST H
            WHERE H.TAG = 2 AND EXISTS (
                SELECT 1 FROM dbo.TAXDTL T
                WHERE T.NUMBER = H.NUMBER AND T.TAG = 2
                  AND T.Ins = 1 AND T.TheStatus IN ('SUCCESS','PENDING','UNKNOWN'))").First();
        line($"      · DenaFaraz: از {dnHead:N0} فاکتور، {dnMatched:N0} تا «ارسال‌شده» شناخته می‌شوند");

        check("۱۸-۱۲ در DenaFaraz اصلاح چیزی را خراب نمی‌کند (هر دو تگ ۲ بودند)",
              dnMatched > 0, dnMatched.ToString());
    }
}
