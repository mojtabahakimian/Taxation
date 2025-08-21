using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.Generaly;
using Prg_Moadian.Service;
using Prg_Moadian.SQLMODELS;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static Prg_Moadian.CNNMANAGER.TaxModel;
using static Prg_Moadian.Generaly.CL_Generaly;

namespace Prg_Moadian.FUNCTIONS
{
    public static class CL_EXTRA_MOADIAN_RS
    {
        public static CL_CCNNMANAGER dbms = new CL_CCNNMANAGER();
        public static CL_FUNTIONS TheFunctions = new CL_FUNTIONS();

        static string PrivateKey_Path_US { get; set; }
        public static string MemoryID { get; set; }

        public static string TaxURL { get; set; } = "https://sandboxrc.tax.gov.ir/req/api/";
        //اصلی//"https://tp.tax.gov.ir/req/api/"
        public static int IDD_OF_TAXDTL { get; set; } = -1;

        public static void DoSendInvoice(string _tmptablename_)
        {
            string RSDTL_TMP_NAME = _tmptablename_;

            var _newsaz = dbms.DoGetDataSQL<SAZMAN>("SELECT MEMORYID,MEMORYIDsand,PRIVIATEKEY,Dcertificate FROM dbo.SAZMAN").FirstOrDefault();
            PrivateKey_Path_US = _newsaz.PRIVIATEKEY.Replace("-----BEGIN PRIVATE KEY-----\r\n", "").Replace("\r\n-----END PRIVATE KEY-----\r\n", "").Trim();

            if (CL_MOADIAN.TaxURL == "https://tp.tax.gov.ir/req/api/")
            {
                MemoryID = _newsaz.MEMORYID.Trim(); //حافظه مالیاتی اصلی
            }
            else
            {
                MemoryID = _newsaz.MEMORYIDsand.Trim(); //حافظه مالیاتی تستی سندباکس
            }

            string? privateKey = PrivateKey_Path_US;

            TaxService? taxService = default;
            try
            {
                taxService = new TaxService(MemoryID, privateKey, TaxURL);
            }
            catch (Exception)
            {
                return;
            }

            try
            {
                RequestTokenModel? model = taxService.RequestToken();
            }
            catch (Exception er)
            {
                if (CL_MOADIAN.TaxURL is "https://tp.tax.gov.ir/req/api/") //اصلی
                {
                    throw new NullyExceptiony("Authentication is not completed or incorrect entered");
                }
                else if (CL_MOADIAN.TaxURL is "https://sandboxrc.tax.gov.ir/req/api/") //آزمایشی
                {
                    if (er.Message is "امضای بسته صحیح نمی باشد")
                    {
                        try
                        {
                            DoGetwriteAppenLog($"From SendingInvoice :{er.Message}  : \n" +
                            $"TaxURL : {CL_MOADIAN.TaxURL} \n" +
                            $"MemoryTax : {CL_MOADIAN.MemoryID}\n" +
                            $"{er.InnerException}");
                        }
                        catch (Exception) { }
                        //throw new NullyExceptiony("Memory Tax is not Match");
                    }
                    throw new NullyExceptiony("Memory Tax is Incorrect");
                }
                return;
            }

            DateTime dt = DateTime.UtcNow;

            //جدول موقتی که فاکتور برای برگشت فروش داخل اون هست
            var RS_ROW = dbms.DoGetDataSQL<FULL_TAXDTL>($"SELECT * FROM {RSDTL_TMP_NAME}").ToList();

            foreach (var item in RS_ROW)
            {
                item.Ins = 4; //از نوع برگشتی است
                item.Mu = decimal.Truncate(decimal.Parse(item.Mu, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
            }

            var src_taxid = taxService.RequestTaxId(MemoryID, dt); //A114K804C0B000AB33C0A7
            var src_Indatim = TaxService.ConvertDateToLong(dt); //1681977600000
            var src_Indati2m = TaxService.ConvertDateToLong(dt); //1681977600000

            TaxModel.InvoiceModel.Header header = new TaxModel.InvoiceModel.Header();
            header.Taxid = src_taxid; //شماره منحصر به فرد مالیاتی
            header.Indatim = src_Indatim; //تاریخ و زمان صدور صورتحساب (میلادی)
            header.Indati2m = src_Indati2m; //تاریخ و زمان ایجاد صورتحساب (میلادی)

            header.Inty = Convert.ToInt32(RS_ROW.First().Inty); //(انواع صورتحساب الکترونیکی 1و2و3) نوع صورتحساب
            header.Inno = RS_ROW.First().Inno; //سریال صورتحساب  //NUMBER	 HEAD_LST
            header.Irtaxid = RS_ROW.First().Taxid; //شماره منحصر به فرد مالیاتی صورتحساب مرجع
            header.Inp = Convert.ToInt32(RS_ROW.First().Inp); //الگوی صورتحساب
            header.Ins = 4; //فقط برگشت فروش | موضوع صورتحساب
            header.Tins = RS_ROW.First().Tins; //شماره اقتصادی فروشنده
            header.Tob = Convert.ToInt32(RS_ROW.First().Tob); //نوع شخص خریدار
            header.Bid = RS_ROW.First().Bid; //شماره/شناسه ملی/شناسه مشارکت مدنی/کد فراگیر خریدار //MCODEM	SAZMAN
            header.Tinb = RS_ROW.First().Tinb; //شماره اقتصادی خریدار //ECODE CUST_HESAB
            header.Sbc = RS_ROW.First().Sbc; //کد شعبه فروشنده //MCODEM	CUST_HESAB
            //header.Bbc = RS_ROW.First().Bbc; //کد شعبه خریدار
            header.Ft = Convert.ToInt32(RS_ROW.First().Ft); //نوع پرواز
            //header.Bpn = RS_ROW.First().Bpn; //شماره گذرنامه خریدار
            header.Scln = RS_ROW.First().Scln; //شماره پروانه گمرکی فروشنده
            //header.Scc = RS_ROW.First().Scc; //کد گمرک محل اظهار
            header.Crn = RS_ROW.First().Crn; //شناسه یکتای ثبت قرارداد فروشنده
            header.Billid = RS_ROW.First().Billid; //شماره اشتراک/شناسه قبض بهره بردار
            header.Tprdis = Convert.ToDecimal(RS_ROW.First().Tprdis); //مجموع مبلغ قبل از کسر تخفیف //INVO_LST	Sum(MABL_K)
            header.Tdis = Convert.ToDecimal(RS_ROW.First().Tdis); //مجموع تخفیفات //INVO_LST	Sum(N_MOIN)
            header.Tadis = Convert.ToDecimal(RS_ROW.First().Tadis); //مجموع مبلغ پس از کسر تخفیف //INVO_LST 	Sum(INVO_LST.MABL_K - INVO_LST.N_MOIN AS mabkbt)
            header.Tvam = Convert.ToDecimal(RS_ROW.First().Tvam); //مجموع مالیات بر ارزش افزوده //INVO_LST	Sum(IMBAA)
            header.Todam = Convert.ToDecimal(RS_ROW.First().Todam); //مجموع سایر مالیات , عوارض و وجوه قانونی
            header.Tbill = Convert.ToDecimal(RS_ROW.First().Tbill); //مجموع صورت حساب //mabkn	INVO_LST.MABL_K-INVO_LST.N_MOIN+INVO_LST.IMBAA AS mabkn
            header.Setm = Convert.ToInt32(RS_ROW.First().Setm); //روش تسویه
            header.Cap = Convert.ToDecimal(RS_ROW.First().Cap); //مبلغ پرداختی نقدی
            header.Insp = Convert.ToDecimal(RS_ROW.First().Insp); //مبلغ پرداختی نسیه
            header.Tvop = Convert.ToDecimal(RS_ROW.First().Tvop); //مجموع سهم مالیات بر ارزش افزوده از پرداخت
            header.Tax17 = Convert.ToDecimal(RS_ROW.First().Tax17); //مالیات موضوع ماده 17
            header.Cdcd = Convert.ToInt32(RS_ROW.First().Cdcd); //تاریخ کوتاژ اظهارنامه گمرکی
            header.Tonw = Convert.ToDecimal(RS_ROW.First().Tonw); //مجموع وزن خالص
            header.Torv = Convert.ToDecimal(RS_ROW.First().Torv); //مجموع ارزش ریالی
            header.Tocv = Convert.ToDecimal(RS_ROW.First().Tocv); //مجموع ارزش ارزی

            List<TaxModel.InvoiceModel.Body>? bodies = new List<TaxModel.InvoiceModel.Body>();
            foreach (var item in RS_ROW)
            {
                bodies.Add(new TaxModel.InvoiceModel.Body
                {
                    Sstid = item.Sstid, //شناسه کالا/خدمت //CODE	STUF_DEF      
                    Sstt = item.Sstt, //شرح کالا/خدمت //NAME	STUF_DEF
                    Mu = item.Mu, //واحد اندازه گیری //VNAMES	TCOD_VAHEDS
                    Am = Convert.ToDecimal(item.Am),//تعداد/مقدار //MEGH	INVO_LST
                    Fee = Convert.ToDecimal(item.Fee), //مبلغ واحد //MABL	INVO_LST
                    Cfee = Convert.ToDecimal(item.Cfee), //میزان ارز
                    Cut = item.Cut, //نوع ارز
                    Exr = Convert.ToDecimal(item.Exr), //نرخ برابری ارز با ریال
                    Prdis = Convert.ToDecimal(item.Prdis), //مبلغ قبل از تخفیف //MABL_K	INVO_LST
                    Dis = Convert.ToDecimal(item.Dis), //مبلغ تخفیف //N_MOIN	INVO_LST
                    Adis = Convert.ToDecimal(item.Adis), //مبلغ بعد از تخفیف //Sum(INVO_LST.MABL_K - INVO_LST.N_MOIN AS mabkbt)	INVO_LST
                    Vra = Convert.ToDecimal(item.Vra), //نرخ مالیات بر ارزش افزوده
                    Vam = Convert.ToDecimal(item.Vam), //مبلغ مالیات بر ارزش افزوده //IMBAA	 INVO_LST
                    Odt = item.Odt, //موضوع سایر مالیات و عوارض
                    Odr = Convert.ToDecimal(item.Odr), //نرخ سایر مالیات و عوارض
                    Odam = Convert.ToDecimal(item.Odam), //مبلغ سایر مالیات و عوارض
                    Olt = item.Olt, //موضوع سایر وجوه قانونی
                    Olr = Convert.ToDecimal(item.Olr), //نرخ سایر وجوه قانونی
                    Olam = Convert.ToDecimal(item.Olam), //مبلغ سایر وجوه قانونی
                    Consfee = Convert.ToDecimal(item.Consfee), //اجرت ساخت
                    Spro = Convert.ToDecimal(item.Spro), //سود فروشنده
                    Bros = Convert.ToDecimal(item.Bros), //حق العمل
                    Tcpbs = Convert.ToDecimal(item.Tcpbs), //جمع کل اجرت , حق العمل و سود
                    Cop = Convert.ToDecimal(item.Cop), //سهم نقدی از پرداخت
                    Vop = Convert.ToDecimal(item.Vop), //سهم ارزش افزوده از پرداخت
                    Bsrn = item.Bsrn, //شناسه یکتای ثبت قرارداد حق العملکاری
                    Tsstam = Convert.ToDecimal(item.Tsstam), //مبلغ کل کالا/خدمت //MABL_K	INVO_LST
                    Nw = Convert.ToDecimal(item.Nw), //وزن خالص
                    Ssrv = Convert.ToDecimal(item.Ssrv), //ارزش ریالی کالا
                    Sscv = Convert.ToDecimal(item.Sscv) //ارزش ارزی کالا
                });
            }
            List<TaxModel.InvoiceModel.Payment>? payments = new List<TaxModel.InvoiceModel.Payment>();

            #region JSON_LOG
            try
            {
                string directoryPath = @"C:\CORRECT\SENTS";
                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }
                // Create a custom JSON object to hold header, bodies, and payments
                var jsonObject = new
                {
                    Header = header,
                    Bodies = bodies,
                    Payments = payments
                };
                // Serialize the custom JSON object to JSON
                string jsonData = JsonSerializer.Serialize(jsonObject);
                // Define the file path for the combined JSON file
                string combinedFilePath = Path.Combine(directoryPath, $"{DateTime.Now.ToString("yyyy-mm-dd-ss-fff")}-Combined.json");

                File.WriteAllText(combinedFilePath, jsonData);
            }
            catch (Exception ex)
            {
            }
            #endregion

            TaxModel.SendInvoicesModel sendInvoicesModel = taxService.SendInvoices(header, bodies, payments);

            //بروز رسانی کد های رهگیری در لیست سی شارپ
            for (int i = 0; i < RS_ROW.Count; i++)
            {
                RS_ROW[i].UID = sendInvoicesModel.Uid;
                RS_ROW[i].RefrenceNumber = sendInvoicesModel.ReferenceNumber;
            }

            //به کدام سامانه ارسال شده
            byte _apitypesent = 0;
            if (CL_MOADIAN.TaxURL is "https://tp.tax.gov.ir/req/api/") // اگر روی اصلیه
                _apitypesent = 1;  // [1 | True] = Main
            else
                _apitypesent = 0;  // [0 | False] = SandBox Testy

            try
            {
                //{ درج در جدول مالیات در دیتابیس 
                foreach (var src_item in RS_ROW)
                {
                    IDD_OF_TAXDTL = TheFunctions.GetNewIDD();

                    const string insertSql = @"INSERT INTO dbo.TAXDTL (
                                               Taxid, Indatim, Indati2m, Indatim_Sec, Indati2m_Sec, Inty, Inno, Irtaxid, Inp, Ins, Tins, Tob, Bid, Tinb, Sbc, Bpc, Ft, Bpn, Scln, Scc, Crn, Billid, Tprdis, Tdis, Tadis, Tvam, Todam, Tbill, Setm, Cap, Insp, Tvop, Tax17, Cdcd, Tonw, Torv, Tocv, Sstid, Sstt, Mu, Am, Fee, Cfee, Cut, Exr, Prdis, Dis, Adis, Vra, Vam, Odt, Odr, Odam, Olt, Olr, Olam, Consfee, Spro, Bros, Tcpbs, Cop, Vop, Bsrn, Tsstam, Nw, Ssrv, Sscv, IDD, UID, RefrenceNumber, TheStatus, ApiTypeSent, SentTaxMemory)
                                               VALUES (@Taxid, @Indatim, @Indati2m, @Indatim_Sec, @Indati2m_Sec, @Inty, @Inno, @Irtaxid, @Inp, @Ins, @Tins, @Tob, @Bid, @Tinb, @Sbc, @Bpc, @Ft, @Bpn, @Scln, @Scc, @Crn, @Billid, @Tprdis, @Tdis, @Tadis, @Tvam, @Todam, @Tbill, @Setm, @Cap, @Insp, @Tvop, @Tax17, @Cdcd, @Tonw, @Torv, @Tocv, @Sstid, @Sstt, @Mu, @Am, @Fee, @Cfee, @Cut, @Exr, @Prdis, @Dis, @Adis, @Vra, @Vam, @Odt, @Odr, @Odam, @Olt, @Olr, @Olam, @Consfee, @Spro, @Bros, @Tcpbs, @Cop, @Vop, @Bsrn, @Tsstam, @Nw, @Ssrv, @Sscv, @IDD, @UID, @RefrenceNumber, @TheStatus, @ApiTypeSent, @SentTaxMemory);";

                    var p = new
                    {
                        Taxid = CL_MOADIAN.SafeString(src_item.Taxid, 22),
                        Indatim = (DateTime?)null,
                        Indati2m = (DateTime?)null,
                        src_item.Indatim_Sec,
                        src_item.Indati2m_Sec,
                        src_item.Inty,
                        Inno = CL_MOADIAN.SafeString(src_item.Inno, 10),
                        Irtaxid = CL_MOADIAN.SafeString(src_item.Irtaxid, 22),
                        src_item.Inp,
                        src_item.Ins,
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
                        src_item.Tprdis,
                        src_item.Tdis,
                        src_item.Tadis,
                        src_item.Tvam,
                        src_item.Todam,
                        src_item.Tbill,
                        src_item.Setm,
                        src_item.Cap,
                        src_item.Insp,
                        src_item.Tvop,
                        src_item.Tax17,
                        src_item.Cdcd,
                        src_item.Tonw,
                        src_item.Torv,
                        src_item.Tocv,
                        Sstid = CL_MOADIAN.SafeString(src_item.Sstid, 13),
                        Sstt = CL_MOADIAN.SafeString(src_item.Sstt, 400),
                        Mu = CL_MOADIAN.SafeDecimal(src_item.Mu),
                        src_item.Am,
                        src_item.Fee,
                        src_item.Cfee,
                        Cut = CL_MOADIAN.SafeString(src_item.Cut, 3),
                        src_item.Exr,
                        src_item.Prdis,
                        src_item.Dis,
                        src_item.Adis,
                        src_item.Vra,
                        src_item.Vam,
                        Odt = CL_MOADIAN.SafeString(src_item.Odt, 255),
                        src_item.Odr,
                        src_item.Odam,
                        Olt = CL_MOADIAN.SafeString(src_item.Olt, 255),
                        src_item.Olr,
                        src_item.Olam,
                        src_item.Consfee,
                        src_item.Spro,
                        src_item.Bros,
                        src_item.Tcpbs,
                        src_item.Cop,
                        src_item.Vop,
                        Bsrn = CL_MOADIAN.SafeString(src_item.Bsrn, 12),
                        src_item.Tsstam,
                        src_item.Nw,
                        src_item.Ssrv,
                        src_item.Sscv,
                        IDD = IDD_OF_TAXDTL,
                        UID = CL_MOADIAN.SafeString(src_item.UID, 100),
                        RefrenceNumber = CL_MOADIAN.SafeString(src_item.RefrenceNumber, 100),
                        TheStatus = "PENDING",
                        ApiTypeSent = _apitypesent,
                        SentTaxMemory = CL_MOADIAN.SafeString(MemoryID, 12),
                    };

                    dbms.DoExecuteSQL(insertSql, p);
                }
                //آماده سازی داده ها }
            }
            catch (Exception ex)
            {
                CL_Generaly.DoGetwriteAppenLog($"Message : {ex.Message} \n\n {ex}");
                throw new NullyExceptiony("Invoice Sent but could not save it to db");
            }
            FactorInfoSent.ReferenceNumber = sendInvoicesModel.ReferenceNumber;
            try
            {
                Thread.Sleep(10_000);
                //پیگیری
                TheFunctions.TrackingCodeInquiry(sendInvoicesModel.ReferenceNumber, MemoryID, privateKey, TaxURL, NUMBER, TAG, IDD_OF_TAXDTL);
            }
            catch (Exception) { /*OnErrorResumeNext*/ }
        }
    }
}
