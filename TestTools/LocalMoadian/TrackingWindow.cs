using System.Text.Json.Nodes;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.FUNCTIONS;
using Prg_Moadian.Generaly;
using Prg_Moadian.Headless;
using Prg_Moadian.SQLMODELS;

namespace MoadianLocalTest;

/// <summary>
/// گروه ۳۴ — پنجرهٔ پیگیری و استعلام (بررسی ۱۴۰۵/۰۸/۱۳).
///
/// سه باگی که با دادهٔ واقعی ثابت شد:
///   ۳۴-۱..۲  گرید ردیف‌های بی‌شمارهٔ مالیاتی (LOCAL_ERROR) را یکی می‌کرد — آبادیس: ۳۰ فاکتور در یک ردیف.
///   ۳۴-۳..۴  EXPIRED هرگز ثبت نمی‌شد («WHERE IDD = » با IDD ِ null)؛ و حالا که ثبت می‌شود،
///            باید مثل PENDING «ارسال‌شده» بماند وگرنه فاکتور به لیست ارسال‌نشده‌ها برمی‌گردد.
///   ۳۴-۵..۸، ۱۰ ارسال تکیِ بی‌پاسخ (UNKNOWN) هیچ کلیدی برای استعلام نداشت؛ حالا uid ِ خودمان را دارد.
///   ۳۴-۹     استعلام پس‌زمینه SUCCESS را پایین نمی‌آورد.
///   ۳۴-۱۱..۱۶ یافته‌های بازبینی Codex: SUCCESS پیش از پیام، زنجیرهٔ ارجاع، نویسندهٔ مشترک، کادر OneWay.
///
/// دادهٔ تستی در بازهٔ ۱۰۰۰۰۰۱..۱۰۰۰۱۲۰ با TAG=77 است تا cleanup_bulk_test.sql هم پاکش کند.
/// </summary>
internal static class TrackingWindow
{
    private const int SendTag = 77;
    private const int HeadTag = 88;
    private const long Lo = 1_000_111, Hi = 1_000_119;

    public static void Run(string mockBaseUrl, HttpClient http,
                           Action<string, bool, string> check, Action<string> line)
    {
        var db = new CL_CCNNMANAGER();
        var fn = new CL_FUNTIONS();
        string savedMode = CL_Generaly.MrCorrect;
        string savedUrl = CL_MOADIAN.TaxURL;
        var savedWarn = CL_MOADIAN.OnValidationWarning;
        string Range(string alias = "") => $"{alias}TAG={SendTag} AND {alias}NUMBER BETWEEN {Lo} AND {Hi}";

        try
        {
            db.DoExecuteSQL($"DELETE FROM dbo.TAXDTL WHERE {Range()}");

            // ---------------- ۳۴-۱ و ۳۴-۲ : گرید ----------------
            var localIdds = new List<int>();
            for (long n = Lo; n < Lo + 3; n++)
            {
                int idd = fn.GetNewIDD();
                localIdds.Add(idd);
                db.DoExecuteSQL(@"INSERT INTO dbo.TAXDTL (Inno, NUMBER, TAG, TheStatus, TheError, IDD, CRT, ApiTypeSent, Tinb)
                                  VALUES (N'TEST', @n, @t, 'LOCAL_ERROR', N'خطای آزمایشی', @idd, GETDATE(), 1, N'')",
                                new { n = (double)n, t = (double)SendTag, idd });
            }
            var grid = db.DoGetDataSQL<TRACK_TAXDTL>(TrackingQueries.GridSql(1, false, Range("T."))).ToList();
            var numbers = grid.Select(g => (long)(g.NUMBER ?? 0)).OrderBy(x => x).ToList();
            check("۳۴-۱ گرید: سه LOCAL_ERROR ِ بی‌شمارهٔ مالیاتی از سه فاکتور، سه ردیف جدا (نه یکی)",
                  grid.Count == 3 && numbers.SequenceEqual(new[] { Lo, Lo + 1, Lo + 2 }),
                  $"ردیف‌ها={grid.Count} شماره‌ها={string.Join(",", numbers)}");

            // یک ارسال عادی با دو قلم: باید یک ردیف بماند، با IDD ِ واقعی.
            // IDD کلید اصلی TAXDTL است، پس هر قلم IDD ِ خودش را دارد؛ گرید بزرگ‌ترین را نشان می‌دهد.
            int sendIdd = 0;
            for (int i = 0; i < 2; i++)
            {
                sendIdd = fn.GetNewIDD();
                db.DoExecuteSQL(@"INSERT INTO dbo.TAXDTL (Taxid, Inno, NUMBER, TAG, TheStatus, IDD, CRT, ApiTypeSent, Tinb, RefrenceNumber, UID, Ins)
                                  VALUES (N'TESTTAXID000000000034A', N'TEST', @n, @t, 'PENDING', @idd, GETDATE(), 1, N'', N'TESTREF34A', N'test-uid-34a', 1)",
                                new { n = (double)(Lo + 3), t = (double)SendTag, idd = sendIdd });
            }
            grid = db.DoGetDataSQL<TRACK_TAXDTL>(TrackingQueries.GridSql(1, false, Range("T."))).ToList();
            var normal = grid.Where(g => g.Taxid == "TESTTAXID000000000034A").ToList();
            check("۳۴-۲ گرید: اقلام یک ارسال یک ردیف می‌مانند و IDD ِ واقعی را دارند (قبلاً null بود)",
                  normal.Count == 1 && normal[0].LineCount == 2 && normal[0].IDD == sendIdd &&
                  grid.Where(g => g.TheStatus == "LOCAL_ERROR").All(g => g.IDD != null && localIdds.Contains(g.IDD.Value)),
                  $"ردیف‌ها={normal.Count} اقلام={normal.FirstOrDefault()?.LineCount} IDD={normal.FirstOrDefault()?.IDD}/{sendIdd}");

            // ---------------- ۳۴-۳ و ۳۴-۴ : EXPIRED ----------------
            db.DoExecuteSQL(@"INSERT INTO dbo.TAXDTL (Taxid, Inno, NUMBER, TAG, TheStatus, IDD, CRT, ApiTypeSent, Tinb, RefrenceNumber, UID, Ins)
                              VALUES (N'TESTTAXID000000000034B', N'TEST', @n, @t, 'PENDING', @idd, GETDATE(), 1, N'', N'TESTREF34B', N'test-uid-34b', 1)",
                            new { n = (double)(Lo + 4), t = (double)SendTag, idd = fn.GetNewIDD() });
            db.DoExecuteSQL(@"INSERT INTO dbo.TAXDTL (Taxid, Inno, NUMBER, TAG, TheStatus, IDD, CRT, ApiTypeSent, Tinb, RefrenceNumber, UID, Ins)
                              VALUES (N'TESTTAXID000000000034C', N'TEST', @n, @t, 'SUCCESS', @idd, GETDATE(), 1, N'', N'TESTREF34C', N'test-uid-34c', 1)",
                            new { n = (double)(Lo + 5), t = (double)SendTag, idd = fn.GetNewIDD() });
            db.DoExecuteSQL(TrackingQueries.ExpireSql, new { RefrenceNumber = "TESTREF34A" });
            db.DoExecuteSQL(TrackingQueries.ExpireSql, new { RefrenceNumber = "TESTREF34C" });
            string St(string r) => string.Join(",", db.DoGetDataSQL<string>(
                $"SELECT DISTINCT TheStatus FROM dbo.TAXDTL WHERE RefrenceNumber=N'{r}' AND {Range()}"));
            check("۳۴-۳ EXPIRED: همهٔ اقلام همان کد رهگیری منقضی شدند؛ کد دیگر و ردیف SUCCESS دست نخوردند",
                  St("TESTREF34A") == "EXPIRED" && St("TESTREF34B") == "PENDING" && St("TESTREF34C") == "SUCCESS",
                  $"A={St("TESTREF34A")} B={St("TESTREF34B")} C={St("TESTREF34C")}");

            check("۳۴-۴ EXPIRED «ارسال‌شده» است: زنده برای هشدار تکراری و دیگر بی‌تکلیف نیست",
                  MoadianRules.IsLiveStatus("EXPIRED") && !TrackingQueries.IsUndecided("EXPIRED") &&
                  !InquiryRunner.Undecided("EXPIRED") && TrackingQueries.IsUndecided("PENDING"),
                  "");

            db.DoExecuteSQL($"DELETE FROM dbo.TAXDTL WHERE {Range()}");

            CodexFindings(db, fn, check);

            db.DoExecuteSQL($"DELETE FROM dbo.TAXDTL WHERE {Range()}");

            // ---------------- ۳۴-۵ تا ۳۴-۸ : UNKNOWN با uid ----------------
            CL_Generaly.MrCorrect = "m";
            CL_MOADIAN.TaxURL = mockBaseUrl;
            CL_MOADIAN.OnValidationWarning = _ => true;

            long target = db.DoGetDataSQL<double>(
                    $"SELECT TOP 1 NUMBER FROM dbo.HEAD_LST WHERE TAG={HeadTag} AND NUMBER BETWEEN {Lo} AND {Hi} ORDER BY NUMBER")
                .Select(x => (long)x).FirstOrDefault();
            if (target == 0)
            {
                check("۳۴-۵ دادهٔ تستی موجود است", false, "seed_bulk_test.sql را اجرا کنید");
                return;
            }

            Post(http, mockBaseUrl, "__reset", "{}");
            Post(http, mockBaseUrl, "__drop_next_send", "{\"mode\":\"empty\"}");
            try { CL_MOADIAN.DoSendInvoice(new[] { $"{target}_{SendTag}_m" }); } catch (Exception) { }

            var rows = db.DoGetDataSQL<(string Taxid, string UID, string RefrenceNumber, string TheStatus)>(
                $"SELECT Taxid, UID, RefrenceNumber, TheStatus FROM dbo.TAXDTL WHERE TAG={SendTag} AND NUMBER={target}").ToList();
            var uids = rows.Select(r => r.UID).Distinct().ToList();
            check("۳۴-۵ جواب نرسید: ردیف‌های UNKNOWN uid ِ ارسال را دارند (قبلاً خالی بود و راهی برای استعلام نبود)",
                  rows.Count > 0 && rows.All(r => r.TheStatus == "UNKNOWN") && uids.Count == 1 &&
                  Guid.TryParse(uids[0], out _) && rows.All(r => string.IsNullOrEmpty(r.RefrenceNumber)),
                  $"ردیف‌ها={rows.Count} وضعیت‌ها={string.Join(",", rows.Select(r => r.TheStatus).Distinct())} uid={string.Join(",", uids)}");
            if (rows.Count == 0) return;

            var estelam = new CL_ESTELAM();
            estelam.GET_INIT_TAX(mockBaseUrl);
            string taxid = rows[0].Taxid, uid = rows[0].UID;

            // uid ای که سامانه نمی‌شناسد: همان UNKNOWN بماند
            var none = estelam.ResolveUnknownByUid(taxid, Guid.NewGuid().ToString());
            var stillUnknown = db.DoGetDataSQL<string>(
                $"SELECT DISTINCT TheStatus FROM dbo.TAXDTL WHERE TAG={SendTag} AND NUMBER={target}").ToList();
            check("۳۴-۶ uid ِ ناشناخته (یا NOT_FOUND): وضعیت همان UNKNOWN می‌ماند، FAILED نمی‌شود",
                  (none is null || none == "NOT_FOUND") && stillUnknown.Count == 1 && stillUnknown[0] == "UNKNOWN",
                  $"پاسخ={none ?? "(هیچ)"} وضعیت={string.Join(",", stillUnknown)}");

            // سرور ساختگی صورتحسابِ پذیرفته را اول IN_PROGRESS می‌گوید (در حال پردازش)
            var resolved = estelam.ResolveUnknownByUid(taxid, uid);
            var after = db.DoGetDataSQL<(string TheStatus, string RefrenceNumber)>(
                $"SELECT TheStatus, RefrenceNumber FROM dbo.TAXDTL WHERE TAG={SendTag} AND NUMBER={target}").ToList();
            string reference = after.Select(a => a.RefrenceNumber).FirstOrDefault() ?? "";
            check("۳۴-۷ استعلام با uid: سامانه گرفته و هنوز در پردازش است → PENDING با کد رهگیری، روی همهٔ اقلام",
                  resolved == "PENDING" && after.Count == rows.Count && after.All(a => a.TheStatus == "PENDING") &&
                  after.Select(a => a.RefrenceNumber).Distinct().Count() == 1 && reference.Length > 0,
                  $"پاسخ={resolved} وضعیت‌ها={string.Join(",", after.Select(a => a.TheStatus).Distinct())} کد={reference}");

            // از اینجا همان استعلام عادیِ با کد رهگیری کار را تمام می‌کند
            Post(http, mockBaseUrl, "__set_status", $"{{\"taxid\":\"{taxid}\",\"status\":\"SUCCESS\"}}");
            try { estelam.GETESTELAM_REFCODE_UPDATE(reference); } catch (Exception) { }
            var st8 = Statuses(db, $"TAG={SendTag} AND NUMBER={target}");
            check("۳۴-۸ بعد از آن، استعلام عادی با همان کد رهگیری به SUCCESS رسید",
                  st8 == "SUCCESS", st8);

            // ---------------- ۳۴-۹ : SUCCESS پایین نمی‌آید ----------------
            Post(http, mockBaseUrl, "__set_status", $"{{\"taxid\":\"{taxid}\",\"status\":\"FAILED\"}}");
            try { estelam.GETESTELAM_REFCODE_UPDATE(reference); } catch (Exception) { }
            var st9 = Statuses(db, $"TAG={SendTag} AND NUMBER={target}");
            check("۳۴-۹ استعلام پس‌زمینه SUCCESS را با پاسخ گذرای FAILED پایین نمی‌آورد",
                  st9 == "SUCCESS", st9);

            // ---------------- ۳۴-۱۰ : پاسخ قطعی SUCCESS مستقیم با uid ----------------
            Post(http, mockBaseUrl, "__drop_next_send", "{\"mode\":\"close\"}");
            try { CL_MOADIAN.DoSendInvoice(new[] { $"{target}_{SendTag}_m" }); } catch (Exception) { }
            var second = db.DoGetDataSQL<(string Taxid, string UID)>(
                $"SELECT DISTINCT Taxid, UID FROM dbo.TAXDTL WHERE TAG={SendTag} AND NUMBER={target} AND TheStatus='UNKNOWN'").ToList();
            string r10 = null, st10 = "";
            if (second.Count == 1 && !string.IsNullOrEmpty(second[0].UID))
            {
                Post(http, mockBaseUrl, "__set_status", $"{{\"taxid\":\"{second[0].Taxid}\",\"status\":\"SUCCESS\"}}");
                r10 = estelam.ResolveUnknownByUid(second[0].Taxid, second[0].UID);
                st10 = Statuses(db, $"Taxid=N'{second[0].Taxid}'");
            }
            var success10 = second.Count == 1 && db.DoGetDataSQL<int>(
                $"SELECT COUNT(*) FROM dbo.TAXDTL WHERE Taxid=N'{second[0].Taxid}' AND (TheSuccess IS NULL OR TheSuccess=0 OR ISNULL(RefrenceNumber,'')='')").First() == 0;
            check("۳۴-۱۰ استعلام با uid: پاسخ قطعی SUCCESS مستقیم ثبت شد (TheSuccess=1 و کد رهگیری)، ارسال قبلی دست نخورد",
                  r10 == "SUCCESS" && st10 == "SUCCESS" && success10 && Statuses(db, $"Taxid=N'{taxid}'") == "SUCCESS",
                  $"ارسال‌های UNKNOWN={second.Count} پاسخ={r10 ?? "(هیچ)"} وضعیت={st10}");
        }
        finally
        {
            try { db.DoExecuteSQL($"DELETE FROM dbo.TAXDTL WHERE {Range()}"); } catch { }
            CL_Generaly.MrCorrect = savedMode;
            CL_MOADIAN.TaxURL = savedUrl;
            CL_MOADIAN.OnValidationWarning = savedWarn;
        }
    }

    // متن واقعی هشدارهای اصلاحیِ حوالهٔ ۱۰۵۴ آبادیس (چهار کدی که روی اصلاحی می‌آیند)
    private const string IdentityWarnings =
        "14004 | فیلد «نوع صورتحساب» در الگوی صورتحساب ارسالی خارج از الگو است.," +
        "14007 | فیلد «الگوی صورتحساب» در الگوی صورتحساب ارسالی خارج از الگو است.," +
        "14012 | فیلد «شماره اقتصادی خریدار» در الگوی صورتحساب ارسالی خارج از الگو است.," +
        "14010 | فیلد «نوع شخص خریدار» در الگوی صورتحساب ارسالی خارج از الگو است.";

    /// <summary>۳۴-۱۱ تا ۳۴-۱۶: یافته‌های بازبینی Codex (۱۴۰۵/۰۸/۱۳).</summary>
    private static void CodexFindings(CL_CCNNMANAGER db, CL_FUNTIONS fn, Action<string, bool, string> check)
    {
        const long n = 1_000_117;
        void Row(string taxid, int ins, int inty, string tinb, string? irtaxid, string status, string reference, string? warning, string? uid = null)
            => db.DoExecuteSQL(@"INSERT INTO dbo.TAXDTL (Taxid, Inno, NUMBER, TAG, Ins, Inty, Inp, Tob, Tinb, Irtaxid, TheStatus, TheSuccess,
                                     RefrenceNumber, UID, TheWarning, IDD, CRT, ApiTypeSent)
                                 VALUES (@taxid, N'TEST', @n, 77, @ins, @inty, 1, 2, @tinb, @irtaxid, @status, @suc,
                                     @reference, @uid, @warning, @idd, GETDATE(), 1)",
                new { taxid, n = (double)n, ins, inty, tinb, irtaxid, status, suc = status == "SUCCESS" ? 1 : 0, reference, uid, warning, idd = fn.GetNewIDD() });

        // زنجیره: اصلی نوع ۲ بی‌خریدار ← اصلاحی ۱ (نوع ۱ با خریدار، بی‌اثر) ← اصلاحی ۲ (همان سرصفحه)
        Row("TESTCODEX0000000000O01", 1, 2, "", null, "SUCCESS", "TESTREF34O", null);
        Row("TESTCODEX0000000000C01", 2, 1, "61385341333", "TESTCODEX0000000000O01", "FAILED", "TESTREF34C1", IdentityWarnings);

        check("۳۴-۱۱ اصلاحیِ ردشده (FAILED) با همان هشدارها پیام «سامانه موفق ثبت کرد» نمی‌گیرد",
              MoadianRules.CheckIgnoredReferenceChanges(db, "TESTREF34C1") == null, "");

        db.DoExecuteSQL("UPDATE dbo.TAXDTL SET TheStatus=N'SUCCESS', TheSuccess=1 WHERE Taxid=N'TESTCODEX0000000000C01'");
        bool firstWarns = MoadianRules.CheckIgnoredReferenceChanges(db, "TESTREF34C1") != null;

        Row("TESTCODEX0000000000C02", 2, 1, "61385341333", "TESTCODEX0000000000C01", "SUCCESS", "TESTREF34C2", IdentityWarnings);
        var eff = MoadianRules.ResolveEffectiveIdentity(db, "TESTCODEX0000000000C01");
        var msg2 = MoadianRules.CheckIgnoredReferenceChanges(db, "TESTREF34C2");
        check("۳۴-۱۲ زنجیرهٔ ارجاع: مرجعِ اصلاحیِ بی‌اثر با مقادیر مؤثر (اصلی: نوع ۲، بی‌خریدار) سنجیده می‌شود، نه ردیف خامش",
              firstWarns && eff != null && eff.Taxid == "TESTCODEX0000000000O01" && eff.Inty == 2 && eff.Hops == 1 &&
              string.IsNullOrEmpty(eff.Tinb) && msg2 != null,
              $"اولی={firstWarns} مؤثر={eff?.Taxid}/نوع {eff?.Inty}/حلقه {eff?.Hops} پیام دومی={msg2 != null}");

        // حلقه در داده: نباید گیر کند
        Row("TESTCODEX0000000000L01", 2, 1, "", "TESTCODEX0000000000L02", "SUCCESS", "TESTREF34L1", null);
        Row("TESTCODEX0000000000L02", 2, 1, "", "TESTCODEX0000000000L01", "SUCCESS", "TESTREF34L2", null);
        var loop = MoadianRules.ResolveEffectiveIdentity(db, "TESTCODEX0000000000L01");
        check("۳۴-۱۳ زنجیرهٔ حلقوی گیر نمی‌کند و جواب می‌دهد",
              loop != null && loop.Hops <= 20, $"حلقه={loop?.Hops}");

        // نویسندهٔ مشترک با کلید Taxid + uid (شاخهٔ uid ِ استعلام دستی)
        Row("TESTCODEX0000000000U01", 1, 1, "", null, "PENDING", "TESTREF34U", null, "uid-34-u");
        Row("TESTCODEX0000000000U01", 1, 1, "", null, "PENDING", "TESTREF34U", null, "uid-34-u");
        const string data = "{\"confirmationReferenceId\":\"CONF34\",\"error\":[],\"warning\":[{\"code\":\"14800\",\"message\":\"test\"}],\"success\":true}";
        var wrongUid = CL_ESTELAM.SaveInquiryResult(db, new TaxCollectData.Library.Dto.Content.InquiryResultModel(
                           "OTHERREF", "uid-of-another-attempt", "SUCCESS", data, "INVOICE.V01", ""), null, "TESTCODEX0000000000U01", "uid-34-u");
        var stWrong = Statuses(db, "Taxid=N'TESTCODEX0000000000U01'");
        var saved = CL_ESTELAM.SaveInquiryResult(db, new TaxCollectData.Library.Dto.Content.InquiryResultModel(
                           "OTHERREF", "uid-34-u", "SUCCESS", data, "INVOICE.V01", ""), null, "TESTCODEX0000000000U01", "uid-34-u");
        var u = db.DoGetDataSQL<(string TheStatus, string RefrenceNumber, string TheWarning, string TheConfirmationReferenceId)>(
            "SELECT TheStatus, RefrenceNumber, TheWarning, TheConfirmationReferenceId FROM dbo.TAXDTL WHERE Taxid=N'TESTCODEX0000000000U01'").ToList();
        check("۳۴-۱۴ شاخهٔ uid: نتیجهٔ قطعی ذخیره می‌شود (وضعیت و هشدار)؛ پاسخِ uid ِ دیگر چیزی نمی‌نویسد؛ کد رهگیری موجود عوض نمی‌شود",
              wrongUid == null && stWrong == "PENDING" && saved == "SUCCESS" && u.Count == 2 &&
              u.All(x => x.TheStatus == "SUCCESS" && x.RefrenceNumber == "TESTREF34U" && (x.TheWarning ?? "").Contains("14800") && x.TheConfirmationReferenceId == "CONF34"),
              $"uid دیگر={wrongUid ?? "-"}/{stWrong} ذخیره={saved} ردیف‌ها={string.Join(";", u.Select(x => x.TheStatus + "/" + x.RefrenceNumber))}");

        CL_ESTELAM.SaveInquiryResult(db, new TaxCollectData.Library.Dto.Content.InquiryResultModel(
            "TESTREF34U", "uid-34-u", "FAILED", "{\"error\":[{\"code\":\"0\",\"message\":\"x\"}],\"warning\":[],\"success\":false}", "INVOICE.V01", ""), "TESTREF34U");
        var nf = CL_ESTELAM.SaveInquiryResult(db, new TaxCollectData.Library.Dto.Content.InquiryResultModel(
            "TESTREF34U", "uid-34-u", "NOT_FOUND", null, "INVOICE.V01", ""), "TESTREF34U");
        var st15 = Statuses(db, "Taxid=N'TESTCODEX0000000000U01'");
        check("۳۴-۱۵ نویسندهٔ مشترک: FAILED ِ گذرا SUCCESS را پایین نمی‌آورد و NOT_FOUND چیزی نمی‌نویسد",
              st15 == "SUCCESS" && nf == null, $"وضعیت={st15} NOT_FOUND={nf ?? "-"}");

        // کادر کد رهگیری نباید به ردیف انتخاب‌شده بازنویسی کند
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "Prg_TrackSentInvoice", "MainWindow.xaml"));
        var tb = System.Text.RegularExpressions.Regex.Match(xaml, "<TextBox x:Name=\"UID_TXB\"[^>]*>").Value;
        check("۳۴-۱۶ کادر کد رهگیری OneWay است: تایپ در آن RefrenceNumber ِ ردیف را عوض نمی‌کند",
              tb.Contains("Path=SelectedItem.RefrenceNumber") && tb.Contains("Mode=OneWay") && !tb.Contains("UpdateSourceTrigger"), tb);
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "TaxationApp.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("TaxationApp.sln not found");
    }

    private static string Statuses(CL_CCNNMANAGER db, string where) =>
        string.Join(",", db.DoGetDataSQL<string>($"SELECT DISTINCT ISNULL(TheStatus,'(null)') FROM dbo.TAXDTL WHERE {where}").OrderBy(x => x));

    private static void Post(HttpClient http, string baseUrl, string route, string json) =>
        http.PostAsync(baseUrl.TrimEnd('/').Replace("/sandbox", "") + "/" + route,
                       new StringContent(json, System.Text.Encoding.UTF8, "application/json"))
            .GetAwaiter().GetResult();
}
