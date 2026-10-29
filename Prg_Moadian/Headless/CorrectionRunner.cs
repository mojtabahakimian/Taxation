using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.FUNCTIONS;
using Prg_Moadian.Service;
using Prg_Moadian.SQLMODELS;

namespace Prg_Moadian.Headless
{
    /// <summary>
    /// فرمِ «صورتحسابِ اصلاحی / برگشتی / ابطالی» بدونِ پنجره — WIN_MODIFYINVOICE ِ Prg_TrackSentInvoice.
    ///
    /// سه قدم، مثلِ پنجره:
    /// • <see cref="Load"/> (ReGetData): ردیف‌های ارسالِ مرجع از TAXDTL؛ Irtaxid = Taxid ِ مرجع و Taxid خالی.
    /// • <see cref="Recalculate"/> و <see cref="Validate"/> (ReCalculateTotals، TotalsAreConsistent، WarnOnReferenceChain و
    ///   شرط‌های BTN_SEND_Click): خطاها مانع‌اند، هشدارها فقط پرسیده می‌شوند — قواعدِ سامانه ناپایدارند و تصمیم با کاربر است.
    /// • <see cref="Send"/> (بقیه‌ی BTN_SEND_Click): شماره‌ی مالیاتیِ تازه، payload، ارسال، درجِ ردیف‌ها در یک تراکنش،
    ///   و فایلِ بازیابی اگر ارسال شد ولی درج نشد.
    ///
    /// ریاضیات در CorrectionMath است (گروه ۲۳)؛ این کلاس فقط ترتیب و شرط‌های پنجره را بی‌پنجره می‌کند (گروه ۳۳).
    /// تفاوتِ عمدی: کنترلِ جمع‌ها CorrectionMath.TotalsAreConsistent است (همان که گروه ۲۳ قفل کرده)، نه نسخه‌ی
    /// داخلِ پنجره؛ هر دو یک قاعده‌اند، فقط متنِ پیام فرق دارد.
    /// </summary>
    public static class CorrectionRunner
    {
        public sealed class Draft
        {
            /// <summary>شماره‌ی مالیاتیِ صورتحسابِ مرجع.</summary>
            public string ReferenceTaxId { get; set; } = "";
            /// <summary>تاریخ و زمانِ صدورِ مرجع (اپکِ میلی‌ثانیه) — پیش از بازنویسیِ ردیف‌ها نگه داشته می‌شود.</summary>
            public long ReferenceIndatim { get; set; }
            public List<TAXDTL> Rows { get; set; } = new List<TAXDTL>();
        }

        public sealed class Check
        {
            public List<string> Errors { get; } = new List<string>();
            public List<string> Warnings { get; } = new List<string>();
        }

        public sealed class Result
        {
            public bool Sent { get; set; }
            public string? TaxId { get; set; }
            public string? ReferenceNumber { get; set; }
            public string? Error { get; set; }
        }

        public static string InsName(int? ins) => ins switch { 1 => "اصلی", 2 => "اصلاحی", 3 => "ابطالی", 4 => "برگشت فروش", _ => "شناسه نامعتبر" };

        /// <summary>
        /// ReGetData: ردیف‌های موفقِ ارسالِ مرجع (حالتِ ویژه = ارسالِ ناموفق، IsSpecialF). null اگر پیدا نشد.
        /// </summary>
        public static Draft? Load(string connectionString, string taxId, bool mainSystem, bool special = false)
        {
            using var cn = new SqlConnection(connectionString);
            var rows = cn.Query<TAXDTL>("SELECT Taxid, Inty, Inno, Irtaxid, Inp, Ins, Tins, Tob, Bid, Tinb, Sbc, Bpc, Ft, Bpn, Scln, Scc, Crn, Billid, Tprdis, Tdis, Tadis, Tvam, Todam, Tbill, Setm, Cap, Insp, Tvop, Tax17, Sstid, Sstt, Mu, Am, Fee, Cfee, Cut, Exr, Prdis, Dis, Adis, Vra, Vam, Odt, Odr, Odam, Olt, Olr, Olam, Consfee, Spro, Bros, Tcpbs, Cop, Vop, Bsrn, Tsstam, Iinn, Acn, Trmn, Trn, Pcn, Pid, Pdt, Cdcn, Cdcd, Tonw, Torv, Tocv, Nw, Ssrv, Sscv, Pmt, PV, IDD, RefrenceNumber, TheConfirmationReferenceId, TheError, TheStatus, TheSuccess, CRT, UID, SentTaxMemory, ApiTypeSent, Indatim_Sec, Indati2m_Sec, NUMBER, TAG, DATE_N, REMARKS FROM dbo.TAXDTL " +
                "WHERE TheSuccess = @success AND ApiTypeSent = @api AND Taxid = @taxId",
                new { success = special ? 0 : 1, api = mainSystem ? 1 : 0, taxId }).ToList();
            if (rows.Count == 0) return null;

            var draft = new Draft { ReferenceTaxId = rows[0].Taxid ?? taxId, ReferenceIndatim = rows[0].Indatim_Sec ?? 0 };
            foreach (var item in rows)
            {
                if (item?.Mu != null)
                    item.Mu = decimal.Truncate(decimal.Parse(item.Mu, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
                item!.Irtaxid = item.Taxid;
                item.Taxid = null; // شماره‌ی مالیاتیِ تازه موقعِ ارسال گرفته می‌شود
                draft.Rows.Add(item);
            }
            HeaderSums(draft.Rows);
            return draft;
        }

        /// <summary>ReCalculateTotals: CorrectionMath برای هر ردیف، Cleaning_RestoreValiding، و جمع‌های سرصفحه روی همه.</summary>
        public static void Recalculate(List<TAXDTL> rows)
        {
            foreach (var item in rows)
            {
                CorrectionMath.RecalculateRow(item);

                if (string.IsNullOrEmpty(item?.Crn) || item?.Crn == "0")
                {
                    if (item?.Crn != null) item.Crn = null; //شناسه یکتای ثبت قرار داد فروشنده
                }
                if (item?.Cut != null && string.IsNullOrWhiteSpace(item.Cut)) item.Cut = null; //نوع ارز
                if (item!.Cfee == null) item.Cfee = 0;
                if (string.IsNullOrWhiteSpace(item.Odt)) item.Odt = "0";
                if (item.Odr == null) item.Odr = 0;
                if (item.Odam == null) item.Odam = 0;
                if (string.IsNullOrWhiteSpace(item.Olt)) item.Olt = "0";
                if (item.Olr == null) item.Olr = 0;
                if (item.Olam == null) item.Olam = 0;
                if (item.Consfee == null) item.Consfee = 0;
                if (item.Spro == null) item.Spro = 0;
                if (item.Bros == null) item.Bros = 0;
                if (item.Tcpbs == null) item.Tcpbs = 0;
                if (item.Cop == null) item.Cop = 0;
                if (item.Vop == null) item.Vop = 0;
                if (string.IsNullOrWhiteSpace(item.Bsrn)) item.Bsrn = null;
                if (item.Nw == null || item.Nw == 0) item.Nw = 0;
                if (item.Ssrv == null || item.Ssrv == 0) item.Ssrv = 0;
                if (item.Sscv == null || item.Sscv == 0) item.Sscv = 0;
            }
            HeaderSums(rows);
        }

        /// <summary>GetHeaderSum: جمع‌ها روی همه‌ی ردیف‌ها (هدرِ ارسالی از ردیفِ اول خوانده می‌شود).</summary>
        private static void HeaderSums(List<TAXDTL> rows)
        {
            var s = CorrectionMath.HeaderSums(rows);
            foreach (var row in rows)
            {
                row.Tprdis = s.Tprdis;
                row.Tdis = s.Tdis;
                row.Tadis = s.Tadis;
                row.Tvam = s.Tvam;
                row.Tbill = s.Tbill;
            }
        }

        /// <summary>
        /// شرط‌های BTN_SEND_Click پیش از ارسال. ins روی همه‌ی ردیف‌ها نوشته می‌شود (پنجره هم یکدستشان می‌کرد).
        /// customIssueDate: تاریخِ سفارشی، یا null = لحظه‌ی ارسال.
        /// </summary>
        public static Check Validate(string connectionString, Draft draft, int ins, DateTime? customIssueDate)
        {
            var c = new Check();
            var rows = draft.Rows;
            if (ins <= 0 || ins > 4) { c.Errors.Add("لطفاً نوع عملیات را انتخاب کنید."); return c; }
            bool isEbtali = ins == 3;
            foreach (var r in rows) r.Ins = ins;

            if (rows.Count == 0 && !isEbtali) c.Errors.Add("برای صورتحساب اصلاحی یا برگشتی باید حداقل یک قلم کالا وجود داشته باشد.");
            if (rows.Count == 0) { if (c.Errors.Count == 0) c.Errors.Add("ردیفی برای ارسال نیست."); return c; }
            if (rows.Any(x => string.IsNullOrWhiteSpace(x.Inno))) c.Errors.Add("سریال جدید (Inno) را وارد کنید.");
            if (rows.Any(x => string.IsNullOrWhiteSpace(x.Irtaxid))) c.Errors.Add("شماره صورت حساب مرجع (Irtaxid) را وارد کنید.");
            if (c.Errors.Count > 0) return c;

            var badRef = rows.FirstOrDefault(x => (x.Irtaxid ?? string.Empty).Trim().Length != 22);
            if (badRef != null)
                c.Warnings.Add("شماره صورت حساب مرجع (Irtaxid) معمولا باید دقیقا ۲۲ کاراکتر باشد. مقدار فعلی «" +
                               badRef.Irtaxid + "» طول " + (badRef.Irtaxid ?? string.Empty).Trim().Length + " دارد.");

            var typedRef = (rows[0].Irtaxid ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(draft.ReferenceTaxId) && !string.Equals(typedRef, draft.ReferenceTaxId.Trim(), StringComparison.OrdinalIgnoreCase))
                c.Warnings.Add($"شماره مرجع وارد شده ({typedRef}) با صورتحسابی که این فرم از روی آن باز شده ({draft.ReferenceTaxId}) یکسان نیست؛ " +
                               "اقلام از سند اول خوانده شده‌اند.");

            c.Warnings.AddRange(ReferenceChainWarnings(connectionString, typedRef, isEbtali));

            // ص۲۵ ردیف ۳: تاریخ صورتحساب ارجاعی نباید از مرجع زودتر باشد
            if (customIssueDate is { } cd && draft.ReferenceIndatim > 0 && TaxService.ConvertDateToLong(cd) < draft.ReferenceIndatim)
            {
                var refDate = DateTimeOffset.FromUnixTimeMilliseconds(draft.ReferenceIndatim).ToOffset(new TimeSpan(3, 30, 0));
                c.Warnings.Add("تاریخ سفارشی از تاریخ صدور صورتحساب مرجع (" + refDate.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture) +
                               ") کوچکتر است؛ طبق ص۲۵ دستورالعمل، تاریخ صورتحساب ارجاعی نباید از مرجع زودتر باشد.");
            }

            Recalculate(rows);
            if (!isEbtali && !CorrectionMath.TotalsAreConsistent(rows, out var why))
                c.Warnings.Add("قواعد جمع/رُندینگ رعایت نشده: " + why);
            return c;
        }

        /// <summary>WarnOnReferenceChain: هشدار (نه سد) درباره‌ی زنجیره‌ی مرجع، با همان متن‌ها.</summary>
        public static List<string> ReferenceChainWarnings(string connectionString, string irtaxid, bool isEbtali)
        {
            var w = new List<string>();
            try
            {
                using var cn = new SqlConnection(connectionString);
                var refInfo = cn.Query<TAXDTL>("SELECT TOP 1 Ins, TheStatus FROM dbo.TAXDTL WHERE Taxid = @tx", new { tx = irtaxid }).FirstOrDefault();
                if (!isEbtali)
                {
                    var liveSibling = cn.Query<TAXDTL>(
                        "SELECT TOP 1 Taxid, Ins, TheStatus FROM dbo.TAXDTL WHERE Irtaxid = @ref AND Ins IN (2, 4) AND TheStatus IN (N'SUCCESS', N'PENDING')",
                        new { @ref = irtaxid }).FirstOrDefault();
                    if (liveSibling != null)
                        w.Add("روی این صورتحساب مرجع قبلا یک " + (liveSibling.Ins == 2 ? "اصلاحی" : "برگشت از فروش") +
                              " با وضعیت «" + liveSibling.TheStatus + "» ثبت شده است (" + liveSibling.Taxid + "). این لزوما مانع نیست، ولی سامانه" +
                              " ممکن است آن را با خطای 0300601 رد کند؛ اگر رد شد، اصلاحی را روی خودِ آن صورتحساب (" + liveSibling.Taxid + ") بزنید.");

                    if (refInfo != null && (refInfo.Ins == 2 || refInfo.Ins == 4))
                        w.Add("صورتحساب مرجع خودش یک " + (refInfo.Ins == 2 ? "اصلاحی" : "برگشت از فروش") +
                              " است (زنجیره پلکانی). طبق ص۱۶ بند ۳ این کار مجاز است به شرط اینکه مرجع در وضعیت تایید شده/تایید سیستمی/عدم نیاز به واکنش باشد.");
                    return w;
                }

                var previous = cn.Query<TAXDTL>(
                    "SELECT TOP 1 Taxid, Ins, TheStatus FROM dbo.TAXDTL WHERE Irtaxid = @ref AND Ins = 3 AND TheStatus IN (N'SUCCESS', N'PENDING')",
                    new { @ref = irtaxid }).FirstOrDefault();
                if (previous != null)
                    w.Add("روی این صورتحساب مرجع قبلا یک ابطالی با وضعیت «" + previous.TheStatus + "» ثبت شده است (شماره مالیاتی " + previous.Taxid +
                          "). طبق ص۱۵ دستورالعمل، هر صورتحساب فقط می‌تواند مرجع یک ابطالی باشد.");
                if (refInfo != null && refInfo.Ins == 3)
                    w.Add("صورتحساب مرجع انتخاب‌شده خودش یک ابطالی است و طبق ص۱۷ بند ۲ معمولا نمی‌تواند مرجع قرار گیرد.");
            }
            catch (Exception) { /* مثلِ پنجره: هشدار نباید کار را بخواباند */ }
            return w;
        }

        /// <summary>
        /// بقیه‌ی BTN_SEND_Click، بعد از Validate و تأییدِ کاربر. ردیف‌ها همان Draft.Rows ِ محاسبه‌شده‌اند.
        /// </summary>
        public static Result Send(string connectionString, bool mainSystem, Draft draft, DateTime? customIssueDate, string? taxUrlOverride = null)
        {
            var result = new Result();
            var TAXDTL_DATA = draft.Rows;
            if (TAXDTL_DATA.Count == 0) { result.Error = "ردیفی برای ارسال نیست."; return result; }
            bool IsEbtali = TAXDTL_DATA[0].Ins == 3;
            var TaxURL = taxUrlOverride ?? (mainSystem ? SingleSendRunner.MainUrl : SingleSendRunner.SandboxUrl);

            using (HeadlessGate.Enter(connectionString))
            {
                string? taxidNew = null;
                TaxModel.SendInvoicesModel? sendInvoicesModel = null;
                string? memoryId = null;
                try
                {
                    Recalculate(TAXDTL_DATA);

                    var dbms = new CL_CCNNMANAGER();
                    var saz = dbms.DoGetDataSQL<SAZMAN>("SELECT TOP 1 MOADINA_SCNUM , YEA , MEMORYID, MEMORYIDsand, PRIVIATEKEY, Dcertificate, ECODE FROM dbo.SAZMAN").FirstOrDefault()
                              ?? throw new Exception("SAZMAN not configured.");
                    var privateKey = saz.PRIVIATEKEY?.Replace("-----BEGIN PRIVATE KEY-----\r\n", "").Replace("\r\n-----END PRIVATE KEY-----\r\n", "").Trim();
                    memoryId = mainSystem ? saz.MEMORYID?.Trim() : saz.MEMORYIDsand?.Trim();

                    var taxService = new TaxService(memoryId!, privateKey!, TaxURL);
                    var _ = taxService.RequestToken();

                    // تاریخ و TaxId جدید — یک snapshot مشترک تا taxidNew و indatim دقیقاً یک لحظه باشند
                    var iranTZ = IranZone();
                    var nowUtcOffset = DateTimeOffset.UtcNow.Add(TimeSync.TimeOffset);
                    var now = TimeZoneInfo.ConvertTimeFromUtc(nowUtcOffset.UtcDateTime, iranTZ);
                    DateTime issueDateForTaxId = customIssueDate ?? now;
                    long indatim = customIssueDate is { } cd ? TaxService.ConvertDateToLong(cd) : nowUtcOffset.ToUnixTimeMilliseconds();
                    long indatim2 = indatim;

                    taxidNew = taxService.RequestTaxId(memoryId!, issueDateForTaxId);
                    foreach (var taxrow in TAXDTL_DATA)
                    {
                        taxrow.Taxid = taxidNew;
                        taxrow.Indatim_Sec = indatim;
                        taxrow.Indati2m_Sec = indatim2;
                    }

                    var (header, bodies) = BuildPayload(TAXDTL_DATA, taxidNew, indatim, indatim2, MoadianRules.ResolveInsr(issueDateForTaxId, now), IsEbtali);
                    var payments = new List<TaxModel.InvoiceModel.Payment>();
                    ResendRunner.WriteJsonLog(header, bodies, payments);

                    sendInvoicesModel = taxService.SendInvoices(header, bodies, payments);
                    foreach (var row in TAXDTL_DATA)
                    {
                        row.UID = sendInvoicesModel.Uid;
                        row.RefrenceNumber = sendInvoicesModel.ReferenceNumber;
                    }
                }
                catch (Exception ex)
                {
                    result.Error = new CustomExceptErMsg().ExpecMsgEr(ex) ?? "خطا در انجام عملیات.";
                    return result;
                }

                try
                {
                    Insert(connectionString, TAXDTL_DATA, taxidNew!, mainSystem, memoryId);
                }
                catch (Exception ex)
                {
                    WriteRecovery(TAXDTL_DATA[0], taxidNew!, sendInvoicesModel, ex);
                    result.Error = "صورتحساب به سامانه ارسال شد اما ثبت آن در دیتابیس ناموفق بود.\n\n" +
                                   $"شماره مالیاتی: {taxidNew}\nکد رهگیری: {sendInvoicesModel.ReferenceNumber}\n\n" +
                                   "اطلاعات در پوشه C:\\CORRECT\\RECOVERY ذخیره شد. لطفاً قبل از هر ارسال مجدد، وضعیت این کد رهگیری را استعلام کنید.";
                    result.TaxId = taxidNew;
                    result.ReferenceNumber = sendInvoicesModel.ReferenceNumber;
                    return result;
                }

                result.Sent = true;
                result.TaxId = taxidNew;
                result.ReferenceNumber = sendInvoicesModel.ReferenceNumber;
                return result;
            }
        }

        /// <summary>#region PrepareModel ِ BTN_SEND_Click. ابطالی بدنه ندارد (ص۱۷).</summary>
        internal static (TaxModel.InvoiceModel.Header, List<TaxModel.InvoiceModel.Body>) BuildPayload(
            List<TAXDTL> rows, string taxidNew, long indatim, long indatim2, int? insr, bool isEbtali)
        {
            var HeadFirst = rows[0];
            var header = new TaxModel.InvoiceModel.Header
            {
                Taxid = taxidNew,
                Indatim = indatim,
                Indati2m = indatim2,
                Inty = Convert.ToInt32(HeadFirst.Inty),
                Inno = HeadFirst.Inno,
                Irtaxid = HeadFirst.Irtaxid,
                Inp = Convert.ToInt32(HeadFirst.Inp),
                Ins = Convert.ToInt32(HeadFirst.Ins),
                // قاعده ارسال (ماده ۹) — جدول ۸۶ ص۹۳؛ ملاک فاصله‌ی صدور تا ارسالِ همین صورتحساب است
                Insr = insr,
                Tins = HeadFirst.Tins,
                Tob = Convert.ToInt32(HeadFirst.Tob),
                // Bid را خام از TAXDTL پخش نمی‌کنیم: مقدارِ قدیمیِ با طولِ نامعتبر با 0101104 رد می‌شود
                Bid = MoadianRules.SanitizeBid(Convert.ToInt32(HeadFirst.Tob), HeadFirst.Bid),
                Tinb = HeadFirst.Tinb,
                Sbc = HeadFirst.Sbc,
                Bbc = HeadFirst.Bbc,
                Bpc = HeadFirst.Bpc,
                Ft = Convert.ToInt32(HeadFirst.Ft),
                Scln = HeadFirst.Scln,
                Crn = HeadFirst.Crn,
                Billid = HeadFirst.Billid,
                Tprdis = Convert.ToDecimal(HeadFirst.Tprdis),
                Tdis = Convert.ToDecimal(HeadFirst.Tdis),
                Tadis = Convert.ToDecimal(HeadFirst.Tadis),
                Tvam = Convert.ToDecimal(HeadFirst.Tvam),
                Todam = Convert.ToDecimal(HeadFirst.Todam),
                Tbill = Convert.ToDecimal(HeadFirst.Tbill),
                Setm = Convert.ToInt32(HeadFirst.Setm),
                Cap = Convert.ToDecimal(HeadFirst.Cap),
                Insp = Convert.ToDecimal(HeadFirst.Insp),
                Tvop = Convert.ToDecimal(HeadFirst.Tvop),
                Tax17 = Convert.ToDecimal(HeadFirst.Tax17),
                Cdcd = Convert.ToInt32(HeadFirst.Cdcd),
                Tonw = Convert.ToDecimal(HeadFirst.Tonw),
                Torv = Convert.ToDecimal(HeadFirst.Torv),
                Tocv = Convert.ToDecimal(HeadFirst.Tocv),
            };

            var bodies = new List<TaxModel.InvoiceModel.Body>();
            if (!isEbtali)
            {
                foreach (var item in rows)
                {
                    bodies.Add(new TaxModel.InvoiceModel.Body
                    {
                        Sstid = item.Sstid, Sstt = item.Sstt, Mu = item.Mu,
                        Am = Convert.ToDecimal(item.Am), Fee = Convert.ToDecimal(item.Fee), Cfee = Convert.ToDecimal(item.Cfee),
                        Cut = item.Cut, Exr = Convert.ToDecimal(item.Exr),
                        Prdis = Convert.ToDecimal(item.Prdis), Dis = Convert.ToDecimal(item.Dis), Adis = Convert.ToDecimal(item.Adis),
                        Vra = Convert.ToDecimal(item.Vra), Vam = Convert.ToDecimal(item.Vam),
                        Odt = item.Odt, Odr = Convert.ToDecimal(item.Odr), Odam = Convert.ToDecimal(item.Odam),
                        Olt = item.Olt, Olr = Convert.ToDecimal(item.Olr), Olam = Convert.ToDecimal(item.Olam),
                        Consfee = Convert.ToDecimal(item.Consfee), Spro = Convert.ToDecimal(item.Spro), Bros = Convert.ToDecimal(item.Bros),
                        Tcpbs = Convert.ToDecimal(item.Tcpbs), Cop = Convert.ToDecimal(item.Cop), Vop = Convert.ToDecimal(item.Vop),
                        Bsrn = item.Bsrn, Tsstam = Convert.ToDecimal(item.Tsstam),
                        Nw = Convert.ToDecimal(item.Nw), Ssrv = Convert.ToDecimal(item.Ssrv), Sscv = Convert.ToDecimal(item.Sscv)
                    });
                }
            }
            return (header, bodies);
        }

        /// <summary>درجِ ردیف‌ها در یک تراکنش، تا ردیفِ ناقص نماند (همان insertSql ِ پنجره).</summary>
        private static void Insert(string cs, List<TAXDTL> rows, string taxidNew, bool mainSystem, string? memoryId)
        {
            const string insertSql = @"INSERT INTO dbo.TAXDTL (
Taxid, Indatim, Indati2m, Indatim_Sec, Indati2m_Sec, Inty, Inno, Irtaxid, Inp, Ins, Tins, Tob, Bid, Tinb, Sbc, Bpc, Ft, Bpn, Scln, Scc, Crn, Billid, Tprdis, Tdis, Tadis, Tvam, Todam, Tbill, Setm, Cap, Insp, Tvop, Tax17, Cdcd, Tonw, Torv, Tocv, Sstid, Sstt, Mu, Am, Fee, Cfee, Cut, Exr, Prdis, Dis, Adis, Vra, Vam, Odt, Odr, Odam, Olt, Olr, Olam, Consfee, Spro, Bros, Tcpbs, Cop, Vop, Bsrn, Tsstam, Nw, Ssrv, Sscv, IDD, UID, RefrenceNumber, TheStatus, ApiTypeSent, SentTaxMemory, NUMBER, TAG, DATE_N)
VALUES (@Taxid, @Indatim, @Indati2m, @Indatim_Sec, @Indati2m_Sec, @Inty, @Inno, @Irtaxid, @Inp, @Ins, @Tins, @Tob, @Bid, @Tinb, @Sbc, @Bpc, @Ft, @Bpn, @Scln, @Scc, @Crn, @Billid, @Tprdis, @Tdis, @Tadis, @Tvam, @Todam, @Tbill, @Setm, @Cap, @Insp, @Tvop, @Tax17, @Cdcd, @Tonw, @Torv, @Tocv, @Sstid, @Sstt, @Mu, @Am, @Fee, @Cfee, @Cut, @Exr, @Prdis, @Dis, @Adis, @Vra, @Vam, @Odt, @Odr, @Odam, @Olt, @Olr, @Olam, @Consfee, @Spro, @Bros, @Tcpbs, @Cop, @Vop, @Bsrn, @Tsstam, @Nw, @Ssrv, @Sscv, @IDD, @UID, @RefrenceNumber, @TheStatus, @ApiTypeSent, @SentTaxMemory, @NUMBER, @TAG, @DATE_N);";
            const int timeout = 300;
            byte api = (byte)(mainSystem ? 1 : 0);

            using var db = new SqlConnection(cs);
            db.Open();
            using var txn = db.BeginTransaction();
            try
            {
                var nextIdd = db.ExecuteScalar<int>("SELECT ISNULL(MAX(IDD), 0) + 1 FROM dbo.TAXDTL WITH (UPDLOCK, HOLDLOCK);", transaction: txn, commandTimeout: timeout);
                foreach (var src_item in rows)
                {
                    db.Execute(insertSql, new
                    {
                        Taxid = CL_MOADIAN.SafeString(taxidNew, 22),
                        Indatim = (DateTime?)null,
                        Indati2m = (DateTime?)null,
                        src_item.Indatim_Sec, src_item.Indati2m_Sec, src_item.Inty,
                        Inno = CL_MOADIAN.SafeString(src_item.Inno, 10),
                        Irtaxid = CL_MOADIAN.SafeString(src_item.Irtaxid, 22),
                        src_item.Inp, src_item.Ins,
                        Tins = CL_MOADIAN.SafeString(src_item.Tins, 14),
                        src_item.Tob,
                        Bid = CL_MOADIAN.SafeString(src_item.Bid, 12),
                        Tinb = CL_MOADIAN.SafeString(src_item.Tinb, 14) ?? string.Empty,
                        Sbc = CL_MOADIAN.SafeString(src_item.Sbc, 10),
                        Bpc = CL_MOADIAN.SafeString(src_item.Bpc, 10),
                        src_item.Ft,
                        Bpn = CL_MOADIAN.SafeString(src_item.Bpn, 9),
                        Scln = CL_MOADIAN.SafeString(src_item.Scln, 14),
                        Scc = CL_MOADIAN.SafeString(src_item.Scc, 5),
                        Crn = CL_MOADIAN.SafeString(src_item.Crn, 12),
                        Billid = CL_MOADIAN.SafeString(src_item.Billid, 19),
                        src_item.Tprdis, src_item.Tdis, src_item.Tadis, src_item.Tvam, src_item.Todam, src_item.Tbill, src_item.Setm,
                        src_item.Cap, src_item.Insp, src_item.Tvop, src_item.Tax17, src_item.Cdcd, src_item.Tonw, src_item.Torv, src_item.Tocv,
                        Sstid = CL_MOADIAN.SafeString(src_item.Sstid, 13),
                        Sstt = CL_MOADIAN.SafeString(src_item.Sstt, 400),
                        Mu = CL_MOADIAN.SafeDecimal(src_item.Mu),
                        src_item.Am, src_item.Fee, src_item.Cfee,
                        Cut = CL_MOADIAN.SafeString(src_item.Cut, 3),
                        src_item.Exr, src_item.Prdis, src_item.Dis, src_item.Adis, src_item.Vra, src_item.Vam,
                        Odt = CL_MOADIAN.SafeString(src_item.Odt, 255),
                        src_item.Odr, src_item.Odam,
                        Olt = CL_MOADIAN.SafeString(src_item.Olt, 255),
                        src_item.Olr, src_item.Olam, src_item.Consfee, src_item.Spro, src_item.Bros, src_item.Tcpbs, src_item.Cop, src_item.Vop,
                        Bsrn = CL_MOADIAN.SafeString(src_item.Bsrn, 12),
                        src_item.Tsstam, src_item.Nw, src_item.Ssrv, src_item.Sscv,
                        IDD = nextIdd++,
                        UID = CL_MOADIAN.SafeString(src_item.UID, 100),
                        RefrenceNumber = CL_MOADIAN.SafeString(src_item.RefrenceNumber, 100),
                        TheStatus = "PENDING",
                        ApiTypeSent = api,
                        SentTaxMemory = CL_MOADIAN.SafeString(memoryId, 12),
                        src_item.NUMBER, src_item.TAG, src_item.DATE_N
                    }, txn, commandTimeout: timeout);
                }
                txn.Commit();
            }
            catch
            {
                txn.Rollback();
                throw;
            }
        }

        /// <summary>
        /// صورتحساب به سامانه رفته ولی در دیتابیس ثبت نشده: شماره‌ی مالیاتی و کدِ رهگیری نباید گم شوند (پنجره همین را می‌نوشت).
        /// </summary>
        private static void WriteRecovery(TAXDTL head, string taxidNew, TaxModel.SendInvoicesModel? sent, Exception ex)
        {
            try
            {
                string recoveryDir = @"C:\CORRECT\RECOVERY";
                if (!Directory.Exists(recoveryDir)) Directory.CreateDirectory(recoveryDir);
                var recovery = new
                {
                    SavedAt = DateTime.Now,
                    Reason = "ارسال به سامانه انجام شد اما ثبت در دیتابیس ناموفق بود",
                    Taxid = taxidNew, head.Inno, head.Irtaxid, head.Ins, head.NUMBER, head.TAG,
                    sent?.ReferenceNumber, sent?.Uid,
                    DbError = ex.Message
                };
                File.WriteAllText(Path.Combine(recoveryDir, $"{DateTime.Now:yyyy-MM-dd-HH-mm-ss-fff}-RECOVERY.json"), JsonSerializer.Serialize(recovery));
            }
            catch { /* بازیابی نباید خودش خطای تازه بسازد */ }
        }

        private static TimeZoneInfo IranZone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time"); }
            catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran"); }
        }
    }
}
