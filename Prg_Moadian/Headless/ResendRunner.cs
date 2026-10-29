using System.Globalization;
using Microsoft.Data.SqlClient;
using Dapper;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.FUNCTIONS;
using Prg_Moadian.Service;
using Prg_Moadian.SQLMODELS;

namespace Prg_Moadian.Headless
{
    /// <summary>
    /// «ارسالِ مجدد» بدونِ پنجره — همان حلقه‌ی RESEND_BTN_Click ِ Prg_TrackSentInvoice/MainWindow.xaml.cs، با
    /// CreateHeaderFromFullTaxDtl، CreateBodyListFromFullTaxDtl و InsertNewTaxDtlRecord ِ همان‌جا.
    ///
    /// صورتحسابِ تازه نمی‌سازد: ردیف‌های TAXDTL ِ نسخه‌ی پایه (اولین ارسالِ غیرِ ResendDuplicate) عیناً دوباره می‌روند —
    /// همان taxid، همان inno، همان تاریخ — و ردیف‌های تازه با REMARKS = ResendDuplicate|… و PENDING ثبت می‌شوند.
    /// شماره‌ی مالیاتیِ یکسان همان چیزی است که این کار را بی‌خطر می‌کند (CLAUDE.md، بخشِ ارسالِ مجدد).
    ///
    /// این متن عینِ پنجره است؛ گروه ۳۲ payload ِ این کلاس را با کدِ خودِ پنجره (ResendTransplant) مقایسه می‌کند،
    /// پس اگر یکی عوض شد و دیگری نه، قرمز می‌شود. پرسشِ پیش از ارسال و قاعده‌ی «فقط PENDING / IN_PROGRESS / FAILED»
    /// (INVOCIE_DTGR_SelectionChanged) کارِ میزبان است؛ <see cref="Resendable"/> همان قاعده است.
    /// </summary>
    public static class ResendRunner
    {
        public sealed class Item
        {
            public string TaxId { get; set; } = "";
            public bool Sent { get; set; }
            public string? ReferenceNumber { get; set; }
            public string? Error { get; set; }
        }

        public sealed class Result
        {
            public List<Item> Items { get; } = new List<Item>();
            /// <summary>خطای کلی (کلید، حافظه، اتصال)؛ آن‌وقت هیچ ارسالی انجام نشده است.</summary>
            public string? Error { get; set; }
        }

        /// <summary>INVOCIE_DTGR_SelectionChanged: فقط این وضعیت‌ها ارسالِ مجدد دارند (UNKNOWN عمداً بیرون است).</summary>
        public static bool Resendable(string? status) => (status?.ToUpperInvariant()) is "PENDING" or "IN_PROGRESS" or "FAILED";

        /// <param name="user">برای REMARKS («ResendDuplicate|کاربر|زمان») — در پنجره Environment.UserName.</param>
        public static Result Run(string connectionString, bool mainSystem, IEnumerable<string> taxIds, string user, string? taxUrlOverride = null)
        {
            var uniqueTaxids = taxIds.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct().ToList();
            var result = new Result();
            if (string.IsNullOrWhiteSpace(connectionString)) { result.Error = "رشتهٔ اتصال دیتابیس خالی است."; return result; }
            if (uniqueTaxids.Count == 0) return result;

            using (HeadlessGate.Enter(connectionString))
            {
                var dbms = new CL_CCNNMANAGER();
                var Functions = new CL_FUNTIONS();
                string windowsUser = user;
                bool isMainApi = mainSystem;
                TaxService taxService;
                try
                {
                    var sazmanInfo = dbms.DoGetDataSQL<SAZMAN>("SELECT MEMORYID, MEMORYIDsand, PRIVIATEKEY FROM dbo.SAZMAN").FirstOrDefault()
                                     ?? throw new Exception("اطلاعات سازمان در دیتابیس یافت نشد.");
                    var privateKey = sazmanInfo.PRIVIATEKEY.Replace("-----BEGIN PRIVATE KEY-----\r\n", "").Replace("\r\n-----END PRIVATE KEY-----\r\n", "").Trim();
                    var memoryId = isMainApi ? sazmanInfo.MEMORYID.Trim() : sazmanInfo.MEMORYIDsand.Trim();
                    var taxUrl = taxUrlOverride ?? (isMainApi ? SingleSendRunner.MainUrl : SingleSendRunner.SandboxUrl);
                    taxService = new TaxService(memoryId, privateKey, taxUrl);
                    taxService.RequestToken();
                }
                catch (Exception ex)
                {
                    result.Error = new CustomExceptErMsg().ExpecMsgEr(ex) ?? "خطای کلی در فرآیند ارسال مجدد: " + ex.Message;
                    return result;
                }

                foreach (var taxid in uniqueTaxids)
                {
                    var item = new Item { TaxId = taxid };
                    result.Items.Add(item);
                    try
                    {
                        // ۱) UID نسخهٔ پایه (اولین ارسالِ غیر ResendDuplicate) را بگیر
                        var baseUid = dbms.DoGetDataSQL<string>(@"
                                                SELECT TOP (1) Uid
                                                FROM dbo.TAXDTL
                                                WHERE Taxid = @taxid AND ApiTypeSent = @api
                                                  AND ISNULL(REMARKS,'') NOT LIKE 'ResendDuplicate%'
                                                ORDER BY CRT ASC
                                            ", new { taxid, api = Convert.ToInt32(isMainApi) }).FirstOrDefault();

                        if (string.IsNullOrEmpty(baseUid))
                        {
                            baseUid = dbms.DoGetDataSQL<string>(@"
                                                    SELECT TOP (1) Uid
                                                    FROM dbo.TAXDTL
                                                    WHERE Taxid = @taxid AND ApiTypeSent = @api
                                                    ORDER BY CRT ASC
                                                ", new { taxid, api = Convert.ToInt32(isMainApi) }).FirstOrDefault();
                        }

                        var originalInvoiceRows = dbms.DoGetDataSQL<FULL_TAXDTL>(@"
                                                SELECT *
                                                FROM dbo.TAXDTL
                                                WHERE Taxid = @taxid AND ApiTypeSent = @api AND Uid = @baseUid
                                                ORDER BY CRT, IDD
                                            ", new { taxid, api = Convert.ToInt32(isMainApi), baseUid });

                        if (!originalInvoiceRows.Any())
                        {
                            item.Error = "ردیفی از این ارسال در TAXDTL پیدا نشد.";
                            continue;
                        }

                        var header = CreateHeaderFromFullTaxDtl(originalInvoiceRows.First());
                        var bodies = CreateBodyListFromFullTaxDtl(originalInvoiceRows.ToList());
                        var payments = new List<TaxModel.InvoiceModel.Payment>();
                        Clean(header, bodies);
                        WriteJsonLog(header, bodies, payments);

                        var sendResult = taxService.SendInvoices(header, bodies, payments);

                        foreach (var originalRow in originalInvoiceRows)
                        {
                            var newLogRow = (FULL_TAXDTL)originalRow.Clone();
                            newLogRow.IDD = Functions.GetNewIDD();
                            newLogRow.RefrenceNumber = sendResult.ReferenceNumber;
                            newLogRow.UID = sendResult.Uid;
                            newLogRow.CRT = DateTime.Now;
                            newLogRow.TheStatus = "PENDING";
                            newLogRow.ApiTypeSent = isMainApi;
                            newLogRow.TheError = null;
                            newLogRow.TheConfirmationReferenceId = null;
                            newLogRow.TheSuccess = false;
                            newLogRow.REMARKS = TruncateString($"ResendDuplicate|{windowsUser}|{DateTime.Now:yy/MM/dd HH:mm}", 49);
                            InsertNewTaxDtlRecord(dbms, newLogRow);
                        }
                        item.Sent = true;
                        item.ReferenceNumber = sendResult.ReferenceNumber;
                    }
                    catch (Exception ex)
                    {
                        item.Error = new CustomExceptErMsg().ExpecMsgEr(ex) ?? ex.Message;
                        CL_Generaly_Log($"Failed to resend invoice with TaxID {taxid}", ex);
                    }
                }
                return result;
            }
        }

        /// <summary>#region Cleaning_RestoreValiding ِ همان حلقه.</summary>
        internal static void Clean(TaxModel.InvoiceModel.Header header, List<TaxModel.InvoiceModel.Body> bodies)
        {
            if (header is TaxModel.InvoiceModel.Header TheHead)
            {
                if (TheHead?.Bpn != null) //شماره گذرنامه خریدار
                {
                    if (string.IsNullOrWhiteSpace(TheHead?.Bpn) || TheHead?.Bpn == "0")
                        TheHead.Bpn = null;
                }
                if (TheHead?.Scc != null) //کد گمرک محل اظهار فروشنده
                {
                    if (string.IsNullOrWhiteSpace(TheHead?.Scc) || TheHead?.Scc == "0")
                        TheHead.Scc = null;
                }
                if (string.IsNullOrEmpty(TheHead?.Crn) || TheHead?.Crn == "0")
                {
                    if (TheHead?.Crn != null)
                        TheHead.Crn = null;
                }
                if (TheHead?.Irtaxid != null)
                {
                    if (string.IsNullOrWhiteSpace(TheHead?.Irtaxid))
                    {
                        if (TheHead?.Inp != 7)
                            TheHead.Irtaxid = null;
                    }
                }
            }
            foreach (var item in bodies)
            {
                if (item?.Cut != null)
                {
                    if (string.IsNullOrWhiteSpace(item?.Cut))
                        item.Cut = null;
                }
                if (item?.Cfee == null) item.Cfee = 0;
                if (string.IsNullOrWhiteSpace(item?.Odt)) item.Odt = "0";
                if (item?.Odr == null) item.Odr = 0;
                if (item?.Odam == null) item.Odam = 0;
                if (string.IsNullOrWhiteSpace(item?.Olt)) item.Olt = "0";
                if (item?.Olr == null) item.Olr = 0;
                if (item?.Olam == null) item.Olam = 0;
                if (item?.Consfee == null) item.Consfee = 0;
                if (item?.Spro == null) item.Spro = 0;
                if (item?.Bros == null) item.Bros = 0;
                if (item?.Tcpbs == null) item.Tcpbs = 0;
                if (item?.Cop == null) item.Cop = 0;
                if (item?.Vop == null) item.Vop = 0;
                if (string.IsNullOrWhiteSpace(item?.Bsrn)) item.Bsrn = null;
                if (item?.Nw == null || item.Nw == 0) item.Nw = 0;
                if (item?.Ssrv == null || item.Ssrv == 0) item.Ssrv = 0;
                if (item?.Sscv == null || item.Sscv == 0) item.Sscv = 0;
            }
        }

        /// <summary>#region JSON_LOG — همان پوشه‌ی پنجره؛ شکستش ارسال را نمی‌ایستاند.</summary>
        internal static void WriteJsonLog(object header, object bodies, object payments)
        {
            try
            {
                string directoryPath = @"C:\CORRECT\SENTS";
                if (!Directory.Exists(directoryPath))
                    Directory.CreateDirectory(directoryPath);
                string jsonData = System.Text.Json.JsonSerializer.Serialize(new { Header = header, Bodies = bodies, Payments = payments });
                File.WriteAllText(Path.Combine(directoryPath, $"{DateTime.Now:yyyy-MM-dd-HH-mm-ss-fff}-Combined.json"), jsonData);
            }
            catch (Exception) { }
        }

        private static void CL_Generaly_Log(string message, Exception ex)
        {
            try { Generaly.CL_Generaly.DoWritePRGLOG(message, ex); } catch { }
        }

        private static string TruncateString(string input, int maxLength)
        {
            if (string.IsNullOrEmpty(input) || maxLength <= 0)
                return string.Empty;
            if (input.Length > maxLength)
                return input.Substring(0, maxLength);
            return input;
        }

        public static TaxModel.InvoiceModel.Header CreateHeaderFromFullTaxDtl(FULL_TAXDTL row)
        {
            var h = new TaxModel.InvoiceModel.Header();

            h.Taxid = row.Taxid; //شماره منحصر به فرد مالیاتی
            h.Indatim = (long)row.Indatim_Sec; //تاریخ و زمان صدور صورت حساب - میلادی
            h.Indati2m = (long)row.Indati2m_Sec; //تاریخ و زمان ایجاد صورتحساب - میلادی

            if (row.Inty != null) h.Inty = row.Inty.Value; //نوع صورت حساب // نوع اول , دوم , سوم
            if (row.Inno != null) h.Inno = row.Inno; // سریال صورت حساب
            if (row.Irtaxid != null) h.Irtaxid = row.Irtaxid; //شماره منحصر به فرد مالیاتی صورتحساب مرجع - برای اصلاح , ابطال , برگشت
            if (row.Inp != null) h.Inp = row.Inp.Value; //الگوی صورتحساب
            if (row.Ins != null) h.Ins = row.Ins.Value; //موضوع صورتحساب //ابطالی , اصلاحی
            if (row.Tins != null) h.Tins = row.Tins; //شماره اقتصادی فروشنده
            if (row.Tob != null) h.Tob = row.Tob.Value; //نوع شخص خریدار
            // همان دلیل فرم اصلاحی: مقدار قدیمی با طول نامعتبر نباید دوباره ارسال شود.
            h.Bid = MoadianRules.SanitizeBid(Convert.ToInt32(row.Tob ?? 0), row.Bid); //شناسه ملی خریدار
            if (row.Tinb != null) h.Tinb = row.Tinb; // شماره اقتصادی خریدار
            if (row.Sbc != null) h.Sbc = row.Sbc; //کد شعبه فروشنده
            if (!string.IsNullOrEmpty(row.Bbc)) h.Bbc = row.Bbc; //کد شعبه خریدار
            if (!string.IsNullOrEmpty(row.Bpc)) h.Bpc = row.Bpc; //کد پستی خریدار
            if (row.Ft != null) h.Ft = row.Ft.Value; //نوع پرواز
            if (row.Bpn != null) h.Bpn = row.Bpn; //شماره گذرنامه خریدار
            if (row.Scln != null) h.Scln = row.Scln; //شماره پروانه گمرکی
            if (row.Scc != null) h.Scc = row.Scc; //کد گمرک محل اظهار فروشنده
            if (row.Crn != null) h.Crn = row.Crn; //شناسه یکتای ثبت قرار داد فروشنده
            if (row.Billid != null) h.Billid = row.Billid; //شماره اشتراک/ شناسه قبض بهرهبردار
            if (row.Tprdis != null) h.Tprdis = row.Tprdis.Value; //مجموع مبلغ قبل از کسر تخفیف
            if (row.Tdis != null) h.Tdis = row.Tdis.Value; //مجموع تخفیفات
            if (row.Tadis != null) h.Tadis = row.Tadis.Value; // مجموع مبلغ پس از کسر تخفیف
            if (row.Tvam != null) h.Tvam = row.Tvam.Value; // مجموع مالیات بر ارزش افزوده
            if (row.Todam != null) h.Todam = row.Todam.Value; // مجموع سایر مالیات، عوارض و وجوه قانونی
            if (row.Tbill != null) h.Tbill = row.Tbill.Value; //مجموع صورتحساب
            if (row.Setm != null) h.Setm = (int)row.Setm.Value; //روش تسویه
            if (row.Cap != null) h.Cap = row.Cap.Value; //مبلغ پرداختی نقدی
            if (row.Insp != null) h.Insp = row.Insp.Value; //مبلغ نسیه
            if (row.Tvop != null) h.Tvop = row.Tvop.Value; // مجموع سهم مالیات بر ارزش افزوده از پرداخت
            if (row.Tax17 != null) h.Tax17 = row.Tax17.Value;

            if (row.Cdcd.HasValue) h.Cdcd = row.Cdcd.Value; // تاریخ کوتاژ اظهارنامه گمرکی
            if (row.Tonw.HasValue) h.Tonw = row.Tonw.Value; //مجموع وزن خالص
            if (row.Torv.HasValue) h.Torv = row.Torv.Value; //مجموع ارزش ریالی
            if (row.Tocv.HasValue) h.Tocv = row.Tocv.Value; //مجموع ارزش ارزی

            return h;
        }

        public static List<TaxModel.InvoiceModel.Body> CreateBodyListFromFullTaxDtl(List<FULL_TAXDTL> rows)
        {
            var bodies = new List<TaxModel.InvoiceModel.Body>();

            foreach (var row in rows)
            {
                var b = new TaxModel.InvoiceModel.Body();

                if (row.Sstid != null) b.Sstid = row.Sstid; //شناسه کالا/خدمت
                if (row.Sstt != null) b.Sstt = row.Sstt;  //شرح کالا/خدمت

                if (!string.IsNullOrEmpty(row.Mu))
                    b.Mu = decimal.Truncate(decimal.Parse(row.Mu, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture); //واحد اندازه گیری

                if (row.Am != null) b.Am = row.Am.Value;   //تعداد/مقدار
                if (row.Fee != null) b.Fee = row.Fee.Value;  //مبلغ واحد
                b.Cfee = 0; //میزان ارز
                if (row.Prdis != null) b.Prdis = row.Prdis.Value; //مبلغ قبل از تخفیف
                if (row.Dis != null) b.Dis = row.Dis.Value;  //مبلغ تخفیف
                if (row.Adis != null) b.Adis = row.Adis.Value; //مبلغ بعد از تخفیف
                if (row.Vra != null) b.Vra = row.Vra.Value;  //نرخ مالیات بر ارزش افزوده
                if (row.Vam != null) b.Vam = row.Vam.Value;  //مبلغ مالیات بر ارزش افزوده
                b.Odt = "0"; //موضوع سایر مالیات و عوارض
                b.Odr = 0; //نرخ سایر مالیات و عوارض
                b.Odam = 0; //مبلغ سایر مالیات و عوارض
                b.Olt = "0"; //موضوع سایر وجوه قانونی
                b.Olr = 0; //نرخ سایر وجوه قانونی
                b.Olam = 0; //مبلغ سایر وجوه قانونی
                b.Consfee = 0; //اجرت ساخت
                b.Spro = 0; //سود فروشنده
                b.Bros = 0; //حق العمل
                b.Tcpbs = 0; //جمع کل اجرت , حق العمل و سود
                b.Cop = 0; //سهم نقدی از پرداخت
                b.Vop = 0; //سهم ارزش افزوده از پرداخت
                b.Bsrn = null; //شناسه یکتای ثبت قرارداد حق العملکاری
                if (row.Tsstam != null) b.Tsstam = row.Tsstam.Value; //مبلغ کل کالا/خدمت
                b.Nw = 0; //وزن خالص
                b.Ssrv = 0; //ارزش ریالی کالا
                b.Sscv = 0; //ارزش ارزی کالا

                if (row.Exr.HasValue) b.Exr = row.Exr.Value; //نرخ برابری ارز با ریال

                bodies.Add(b);
            }

            return bodies;
        }

        /// <summary>InsertNewTaxDtlRecord ِ پنجره، با همه‌ی ستون‌ها از جمله NUMBER و TAG (گروه ۲۴).</summary>
        internal static void InsertNewTaxDtlRecord(CL_CCNNMANAGER dbms, FULL_TAXDTL src_item)
        {
            const string insertSql = @"INSERT INTO dbo.TAXDTL (
            Taxid, Indatim_Sec, Indati2m_Sec, Inty, Inno, Irtaxid, Inp, Ins, Tins, Tob, Bid, Tinb, Sbc, Bpc, Ft, Bpn, Scln, Scc, Crn, Billid, Tprdis, Tdis, Tadis, Tvam, Todam, Tbill, Setm, Cap, Insp, Tvop, Tax17, Cdcd, Tonw, Torv, Tocv, Sstid, Sstt, Mu, Am, Fee, Cfee, Cut, Exr, Prdis, Dis, Adis, Vra, Vam, Odt, Odr, Odam, Olt, Olr, Olam, Consfee, Spro, Bros, Tcpbs, Cop, Vop, Bsrn, Tsstam, Nw, Ssrv, Sscv, IDD, UID, RefrenceNumber, TheStatus, ApiTypeSent, SentTaxMemory,DATE_N, REMARKS, CRT, NUMBER, TAG)
            VALUES (@Taxid, @Indatim_Sec, @Indati2m_Sec, @Inty, @Inno, @Irtaxid, @Inp, @Ins, @Tins, @Tob, @Bid, @Tinb, @Sbc, @Bpc, @Ft, @Bpn, @Scln, @Scc, @Crn, @Billid, @Tprdis, @Tdis, @Tadis, @Tvam, @Todam, @Tbill, @Setm, @Cap, @Insp, @Tvop, @Tax17, @Cdcd, @Tonw, @Torv, @Tocv, @Sstid, @Sstt, @Mu, @Am, @Fee, @Cfee, @Cut, @Exr, @Prdis, @Dis, @Adis, @Vra, @Vam, @Odt, @Odr, @Odam, @Olt, @Olr, @Olam, @Consfee, @Spro, @Bros, @Tcpbs, @Cop, @Vop, @Bsrn, @Tsstam, @Nw, @Ssrv, @Sscv, @IDD, @UID, @RefrenceNumber, @TheStatus, @ApiTypeSent, @SentTaxMemory, @DATE_N , @REMARKS ,@CRT, @NUMBER, @TAG);";
            dbms.DoExecuteSQL(insertSql, new
            {
                src_item.Taxid, src_item.Indatim_Sec, src_item.Indati2m_Sec, src_item.Inty, src_item.Inno, src_item.Irtaxid, src_item.Inp, src_item.Ins,
                src_item.Tins, src_item.Tob, src_item.Bid, src_item.Tinb, src_item.Sbc, src_item.Bpc, src_item.Ft, src_item.Bpn, src_item.Scln,
                src_item.Scc, src_item.Crn, src_item.Billid, src_item.Tprdis, src_item.Tdis, src_item.Tadis, src_item.Tvam, src_item.Todam,
                src_item.Tbill, src_item.Setm, src_item.Cap, src_item.Insp, src_item.Tvop, src_item.Tax17, src_item.Cdcd, src_item.Tonw,
                src_item.Torv, src_item.Tocv, src_item.Sstid, src_item.Sstt, src_item.Mu, src_item.Am, src_item.Fee, src_item.Cfee, src_item.Cut,
                src_item.Exr, src_item.Prdis, src_item.Dis, src_item.Adis, src_item.Vra, src_item.Vam, src_item.Odt, src_item.Odr, src_item.Odam,
                src_item.Olt, src_item.Olr, src_item.Olam, src_item.Consfee, src_item.Spro, src_item.Bros, src_item.Tcpbs, src_item.Cop,
                src_item.Vop, src_item.Bsrn, src_item.Tsstam, src_item.Nw, src_item.Ssrv, src_item.Sscv, src_item.IDD, src_item.UID,
                src_item.RefrenceNumber, src_item.TheStatus, src_item.ApiTypeSent, src_item.SentTaxMemory, src_item.DATE_N, src_item.REMARKS,
                src_item.CRT,
                // بدون این‌ها ردیفِ ارسالِ مجدد در گریدِ پیگیری بی‌شماره‌ی فاکتور و بی‌مشتری ظاهر می‌شود (گروه ۲۴)
                src_item.NUMBER, src_item.TAG
            });
        }
    }
}
