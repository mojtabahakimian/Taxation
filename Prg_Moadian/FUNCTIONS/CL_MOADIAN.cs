using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.SQLMODELS;
using Prg_Moadian.Generaly;
using static Prg_Moadian.Generaly.CL_Generaly;
using System.Text.Json;
using static Prg_Moadian.CNNMANAGER.TaxModel;
using Prg_Moadian.Service;
using TaxCollectData.Library.Business;
using TaxCollectData.Library.Dto.Content;
using TaxCollectData.Library.Dto.Transfer;
using TaxCollectData.Library.Dto;
using Microsoft.Data.SqlClient;
using System.Data;

namespace Prg_Moadian.FUNCTIONS
{
    public static class CL_MOADIAN
    {
        public static CL_CCNNMANAGER dbms = new CL_CCNNMANAGER();
        public static CL_FUNTIONS TheFunctions = new CL_FUNTIONS();

        public static List<DRV_TBL> L_DRV_TBL_US { get; set; } = new List<DRV_TBL>();
        public static List<SAZMAN> L_Baseknow_US { get; set; } = new List<SAZMAN>();

        public static List<TAXDTL> L_TAXDTL_US { get; set; }

        public static Int64 NUMBER { get; set; } = 44;
        public static int TAG { get; set; } = 2;

        static string PrivateKey_Path_US { get; set; } //= @"C:\prg\Taxation\NewInfo\PrivateKey.txt";
        public static string MemoryID { get; set; } // = "A114K8";

        public static string TaxURL { get; set; } = "https://sandboxrc.tax.gov.ir/req/api/";
        //اصلی//"https://tp.tax.gov.ir/req/api/"
        public static int IDD_OF_TAXDTL { get; set; } = -1;

        public static string CALLER_NAME { get; set; }

        /// <summary>
        /// شماره فاکتور دخالی یا شماره سریال داخلی صورت حساب مالیاتی که inno هست و باید ادامه سال قبل هم باشه
        /// </summary>
        public static string StarterInnoNumber { get; set; } = "1";

        public class ErrorResult_PROP
        {
            public int? ConfirmationReferenceId { get; set; }
            public List<Error_PROP> Error { get; set; }
            public List<object> Warning { get; set; }
            public bool Success { get; set; }
        }
        public class Error_PROP
        {
            public string Code { get; set; }
            public string Message { get; set; }
            public string ErrorType { get; set; }
        }

        // توابع کمکی برای جلوگیری از مقادیر نامعتبر
        public static string? SafeString(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            value = value.Trim();
            return value.Length > maxLength ? value.Substring(0, maxLength) : value;
        }
        public static decimal? SafeDecimal(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return decimal.TryParse(value, out var d) ? d : (decimal?)null;
        }

        /// <summary>
        /// MAIN
        /// </summary>
        /// <param name="args"></param>
        public static void DoSendInvoice(string[] args)
        {
            L_TAXDTL_US = new List<TAXDTL>();

            var _newsaz = dbms.DoGetDataSQL<SAZMAN>("SELECT MOADINA_SCNUM , YEA , MEMORYID,MEMORYIDsand,PRIVIATEKEY,Dcertificate FROM dbo.SAZMAN").FirstOrDefault();

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

            #region Prepair_Get_Input_Text_Passed_And
            if (args.Length > 0)
            {
                // Access the first command-line argument (index 0)
                string? input = args[0];
                if (!(input is null))
                {

                    if (!string.IsNullOrEmpty(input) && input.Length != 0)
                    {
                        string[]? NUMBER_AND_TAG = input.Split('_');
                        // Check if the input was split into two parts
                        if (NUMBER_AND_TAG.Length == 3)
                        {
                            NUMBER = Int64.Parse(NUMBER_AND_TAG[0]);
                            TAG = Int32.Parse(NUMBER_AND_TAG[1]);
                            CALLER_NAME = NUMBER_AND_TAG[2].ToLower(); //نرم افزاری که اون رو صدا زده
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine("No input provided."); return;
            }
            #endregion


            //////MOADINA_SCNUM این حذف شد چون کاریش نداریم

            if (_newsaz != null && _newsaz.YEA > 0)
            {

                StarterInnoNumber = _newsaz.YEA.ToString() + "00" + NUMBER;
            }

            ////TheFunctions.SendSampleInvoiceTest1(MemoryID, privateKey, TaxURL);////return; //Just Test and Get Back--------------------------------------------------------------------------|

            #region TTTTEEEESSSSTTTT
            //TaxApiService.Instance.Init(MemoryID, new SignatoryConfig(privateKey, null), new NormalProperties(ClientType.SELF_TSP), TaxURL);
            //ServerInformationModel serverInformationModel = TaxApiService.Instance.TaxApis.GetServerInformation();
            //TokenModel tokenModel = TaxApiService.Instance.TaxApis.RequestToken();


            //var economicCodeInformation8 = TaxApiService.Instance.TaxApis.GetEconomicCodeInformation("10840014242"); // یزد سپار
            //var economicCodeInformation9 = TaxApiService.Instance.TaxApis.GetEconomicCodeInformation("411375646679");
            //var economicCodeInformation10 = TaxApiService.Instance.TaxApis.GetEconomicCodeInformation("10840011810");
            //var fiscalInformation = TaxApiService.Instance.TaxApis.GetFiscalInformation(MemoryID);
            #endregion

            //Let's Go
            TaxService? taxService = default;
            try
            {
                taxService = new TaxService(MemoryID, privateKey, TaxURL);
                //CL_Generaly.DoGetwriteAppenLog("TaxService Passed");
            }
            catch (Exception)
            {
                CL_ERRLST.ERROR_MAIN_LST.FirstOrDefault(e => e.ER_ID == 1).ER_HAPPENED = true;
                //CL_Generaly.DoGetwriteAppenLog("ER_ID == 1");
                return;
            }
            try
            {
                RequestTokenModel? model = taxService.RequestToken();
                //CL_Generaly.DoGetwriteAppenLog("taxService.RequestToken Passed");
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
                //CL_Generaly.DoGetwriteAppenLog("ER_ID == 1");
                return;
            }


            //آماده سازی داده ها {
            L_Baseknow_US.AddRange(dbms.DoGetDataSQL<SAZMAN>("SELECT * FROM dbo.SAZMAN"));


            if (CALLER_NAME == MrCorrect) //اگر مسترکارکت هست
            {
                L_DRV_TBL_US.AddRange(dbms.DoGetDataSQL<DRV_TBL>($@"SELECT DATE_N, NUMBER1, HESAB, ADDRESS, TEL, CODE, ECODE, MCODEM, PCODE, IYALAT, CITY, KALA, MEGH, MABL, MABL_K, VNAMES, N_MOIN, mabkbt, IMBAA, mabkn, BARCODE, MEGHk, MOLAH, DEPART, DEPNAME, NUMBER,tob,DRVD_TBL.sstid,DRVD_TBL.mu,DRVD_TBL.vra, MEGH_MAR
                  FROM(SELECT dbo.HEAD_BACK_ANBAR.NUMBER1, dbo.HEAD_BACK_ANBAR.NUMBER, dbo.HEAD_BACK_ANBAR.DATE_N, dbo.INVO_LST.NUMBER AS INUMBER, dbo.HEAD_BACK_ANBAR.HTAG, dbo.INVO_LST.ANBAR, dbo.INVO_LST.RADIF, dbo.INVO_LST.CODE, dbo.INVO_LST.MEGH, dbo.INVO_LST.MEGHk, dbo.INVO_LST.MEGH_MAR, dbo.INVO_LST.MANDAH, dbo.INVO_LST.MABL, dbo.INVO_LST.MABL_K, dbo.INVO_LST.FROM_A, dbo.INVO_LST.N_RASID, dbo.INVO_LST.MEGH_R, dbo.INVO_LST.RADAH, dbo.INVO_LST.SANAD_NO, dbo.INVO_LST.ANBARF, dbo.INVO_LST.VAHED_K, dbo.STUF_DEF.NAME, dbo.TCOD_ANBAR.NAMES, dbo.TCOD_VAHEDS.NAMES AS VNAMES, dbo.HEAD_BACK_ANBAR.TAH, dbo.HEAD_BACK_ANBAR.MOLAH, dbo.CUSTKIND.CUSTKNAME, dbo.DEPART.DEPNAME, dbo.SHIFT.SHNAME, dbo.CUST_HESAB.NAME AS HESAB, dbo.CUST_HESAB.ADDRESS, dbo.CUST_HESAB.TEL, ISNULL(dbo.STUF_DEF.NAME, N' ')+N' '+ISNULL(dbo.INVO_LST.MANDAH, N' ') AS KALA, dbo.HEAD_BACK_ANBAR.CUST_NO, dbo.INVO_LST.N_KOL, dbo.INVO_LST.N_MOIN, dbo.HEAD_BACK_ANBAR.FNUMCO, dbo.CUST_HESAB.ECODE, dbo.CUST_HESAB.PCODE, dbo.CUST_HESAB.IYALAT, dbo.CUST_HESAB.MCODEM, dbo.CUST_HESAB.CITY, dbo.INVO_LST.MABL_K-dbo.INVO_LST.N_MOIN AS mabkbt, dbo.INVO_LST.IMBAA, dbo.INVO_LST.MABL_K-dbo.INVO_LST.N_MOIN+dbo.INVO_LST.IMBAA AS mabkn, dbo.CUST_HESAB.CODE_E, dbo.HEAD_BACK_ANBAR.TAKHFIF, dbo.HEAD_BACK_ANBAR.MBAA, dbo.STUF_DEF.N_FANI, dbo.HEAD_BACK_ANBAR.SADER, dbo.HEAD_BACK_ANBAR.ANBARF AS ANBARFF, dbo.OTHER_DTL.REQUEST_NO, dbo.OTHER_DTL.BARNAMEH, dbo.OTHER_DTL.DRIVER, dbo.OTHER_DTL.DRIVER_MOB, dbo.OTHER_DTL.CAMIUN_NUM, dbo.OTHER_DTL.MAGHSAD, dbo.OTHER_DTL.CAM_KHALY, dbo.OTHER_DTL.CAM_POOR, dbo.OTHER_DTL.TOZIH, dbo.OTHER_DTL.CAMIUN, dbo.OTHER_DTL_SUB.CODE AS CODEG, dbo.OTHER_DTL_SUB.CAM_KHALY AS CAM_KHALYG, dbo.OTHER_DTL_SUB.CAM_POOR AS CAM_POORG, dbo.OTHER_DTL_SUB.MEGHk AS MEGHkG, dbo.OTHER_DTL_SUB.TOZIH AS TOZIHG, dbo.OTHER_DTL_SUB.VAZNH, STUF_DEF_1.NAME AS NAMEG, dbo.HEAD_BACK_ANBAR.SHARAYET, dbo.DEPART.DEPART, dbo.INVO_LST.TKHN, dbo.HEAD_BACK_ANBAR.MAS, dbo.HEAD_BACK_ANBAR.HTAG AS TAG, dbo.SALA_DTL.EMZA AS EMZA1, SALA_DTL_1.EMZA AS EMZA2, SALA_DTL_2.EMZA AS EMZA3, dbo.HEAD_BACK_ANBAR.SGN1usid, dbo.HEAD_BACK_ANBAR.sgn2usid, dbo.HEAD_BACK_ANBAR.sgn3usid, dbo.HEAD_LST.SGN1, dbo.HEAD_LST.SGN2, dbo.HEAD_LST.SGN3, dbo.HEAD_LST.SGN4, dbo.STUF_DEF.BARCODE, dbo.CUST_HESAB.tob , dbo.STUF_DEF.sstid,dbo.STUF_DEF.mu , dbo.STUF_DEF.vra
                        FROM dbo.STUF_DEF
                            RIGHT OUTER JOIN dbo.SHIFT
                                             RIGHT OUTER JOIN dbo.DEPART
                                                              RIGHT OUTER JOIN dbo.HEAD_LST
                                                                               INNER JOIN dbo.INVO_LST
                                                                                          INNER JOIN dbo.TCOD_ANBAR ON dbo.INVO_LST.ANBAR=dbo.TCOD_ANBAR.CODE
                                                                                          INNER JOIN dbo.HEAD_BACK_ANBAR ON dbo.INVO_LST.NUMBER=dbo.HEAD_BACK_ANBAR.NUMBER AND dbo.INVO_LST.TAG=dbo.HEAD_BACK_ANBAR.HTAG ON dbo.HEAD_LST.NUMBER=dbo.HEAD_BACK_ANBAR.NUMBER AND dbo.HEAD_LST.TAG=dbo.HEAD_BACK_ANBAR.TAG ON dbo.DEPART.DEPATMAN=dbo.HEAD_BACK_ANBAR.DEPATMAN
                                                              LEFT OUTER JOIN dbo.CUSTKIND ON dbo.HEAD_BACK_ANBAR.CUST_KIND=dbo.CUSTKIND.CUST_COD ON dbo.SHIFT.SHIFT_ID=dbo.HEAD_BACK_ANBAR.SHIFT
                                             LEFT OUTER JOIN dbo.OTHER_DTL ON dbo.HEAD_BACK_ANBAR.NUMBER=dbo.OTHER_DTL.NUMBER AND dbo.HEAD_BACK_ANBAR.HTAG=dbo.OTHER_DTL.TAG
                                             LEFT OUTER JOIN dbo.SALA_DTL AS SALA_DTL_2 ON dbo.HEAD_BACK_ANBAR.sgn3usid=SALA_DTL_2.IDD
                                             LEFT OUTER JOIN dbo.SALA_DTL AS SALA_DTL_1 ON dbo.HEAD_BACK_ANBAR.sgn2usid=SALA_DTL_1.IDD
                                             LEFT OUTER JOIN dbo.SALA_DTL ON dbo.HEAD_BACK_ANBAR.SGN1usid=dbo.SALA_DTL.IDD
                                             LEFT OUTER JOIN dbo.STUF_DEF AS STUF_DEF_1
                                                             INNER JOIN dbo.OTHER_DTL_SUB ON STUF_DEF_1.CODE=dbo.OTHER_DTL_SUB.CODE ON dbo.INVO_LST.CODE=dbo.OTHER_DTL_SUB.CODE AND dbo.INVO_LST.NUMBER=dbo.OTHER_DTL_SUB.NUMBER AND dbo.INVO_LST.TAG=dbo.OTHER_DTL_SUB.TAGG
                                             LEFT OUTER JOIN dbo.CUST_HESAB ON dbo.HEAD_BACK_ANBAR.CUST_NO=dbo.CUST_HESAB.hes
                                             LEFT OUTER JOIN dbo.TCOD_VAHEDS ON dbo.INVO_LST.VAHED_K=dbo.TCOD_VAHEDS.CODE ON dbo.STUF_DEF.CODE=dbo.INVO_LST.CODE) AS DRVD_TBL
                  WHERE NUMBER={NUMBER} AND DRVD_TBL.TAG={TAG}
                  "));
            }
            else //دنافراز
            {
                L_DRV_TBL_US.AddRange(dbms.DoGetDataSQL<DRV_TBL>($@"SELECT        dbo.HEAD_LST.NUMBER, dbo.HEAD_LST.TAG, dbo.HEAD_LST.DATE_N, dbo.INVO_LST.MABL, dbo.INVO_LST.MABL_K, dbo.INVO_LST.N_MOIN, dbo.INVO_LST.MABL_K - dbo.INVO_LST.N_MOIN AS mabkbt, 
                                    dbo.INVO_LST.IMBAA, dbo.INVO_LST.MABL_K - dbo.INVO_LST.N_MOIN + dbo.INVO_LST.IMBAA AS mabkn, dbo.CUST_HESAB.MCODEM, dbo.CUST_HESAB.tob, dbo.INVO_LST.CODE, dbo.STUF_DEF.sstid, dbo.STUF_DEF.mu, 
                                    ISNULL(dbo.STUF_DEF.NAME, N' ') + N' ' + ISNULL(dbo.INVO_LST.MANDAH, N' ') AS KALA, dbo.INVO_LST.MEGHk, dbo.STUF_DEF.vra, dbo.CUST_HESAB.ECODE,dbo.INVO_LST.MEGH_MAR
                                    FROM            dbo.HEAD_LST INNER JOIN
                                                             dbo.INVO_LST ON dbo.HEAD_LST.NUMBER = dbo.INVO_LST.NUMBER AND dbo.HEAD_LST.TAG = dbo.INVO_LST.TAG INNER JOIN
                                                             dbo.CUST_HESAB ON dbo.HEAD_LST.CUST_NO = dbo.CUST_HESAB.hes INNER JOIN
                                                             dbo.STUF_DEF ON dbo.INVO_LST.CODE = dbo.STUF_DEF.CODE
                                    WHERE        (dbo.HEAD_LST.NUMBER = {NUMBER}) AND (dbo.HEAD_LST.TAG = {TAG})
                                       "));
            }

            var _contin = true;
            // چک کن واحد و شناسه کالا درست باشه
            foreach (var item in L_DRV_TBL_US)
            {
                if (string.IsNullOrEmpty(item.sstid))
                {
                    CL_ERRLST.ERROR_BODY_LST.Add(new CL_ERRLST.ER_BOD_MODEL { CODE = item.CODE, SSTID = item.sstid });
                    _contin = false;
                }
                if (string.IsNullOrEmpty(item.mu))
                {
                    CL_ERRLST.ERROR_BODY_LST.Add(new CL_ERRLST.ER_BOD_MODEL { CODE = item.CODE, MU = item.mu });
                    _contin = false;
                }
            }
            if (_contin is false)
            {
                return;
            }

            if (!L_DRV_TBL_US.Any())
            {
                throw new NullyExceptiony($"HEAD_LST not found for invoice{NUMBER}");
            }

            string? src_ECODE = L_DRV_TBL_US.FirstOrDefault().ECODE; //10840014242

            var _HEAD_EXTENDED = dbms.DoGetDataSQL<HEAD_LST_EXTENDED>($"SELECT * FROM dbo.HEAD_LST_EXTENDED WHERE NUMBER = {NUMBER} AND TGU = {TAG}").FirstOrDefault();

            if (_HEAD_EXTENDED?.inty is null)
            {
                throw new NullyExceptiony("HEAD_EXTENDED is null");
            }

            //به تفکیک آدرس و شعبه طبق4 واحد زیر مجموعه سازمان
            var ROWDPEART = dbms.DoGetDataSQL<DEPART>($"SELECT * FROM dbo.DEPART WHERE DEPATMAN = (SELECT DEPATMAN FROM dbo.HEAD_LST WHERE TAG = {TAG} AND NUMBER = {NUMBER})").FirstOrDefault();
            if (ROWDPEART != null && ROWDPEART?.DEPATMAN != null)
            {
                //New Line Added : یکشنبه 7 مرداد 1403 ساعت 08:53 دقیقه

                if (ROWDPEART?.BBC != null)
                {
                    _HEAD_EXTENDED.bbc = ROWDPEART.BBC; //کد شعبه خریدار
                }
                if (ROWDPEART?.PCODE != null)
                {
                    _HEAD_EXTENDED.bpc = ROWDPEART.PCODE; //کد پستی خریدار
                }
            }

            ////جایگذاری و پر کردن دیتاهای مربوطه
            DateTime DtNowBase;
            string? src_taxid = null;
            long src_Indatim = 0;
            long src_Indati2m = 0;
            // تعیین DTBASE و src_Indatim
            switch (_HEAD_EXTENDED.ins) //نوع صورت حساب
            {
                case 4: //از نوع برگشتی
                    var rDate = dbms.DoGetDataSQL<string>($"SELECT DATE_N FROM dbo.HEAD_LST_FBK WHERE NUMBER1 = {NUMBER}").FirstOrDefault();
                    DtNowBase = TheFunctions.GetGregorianDateTime(rDate);

                    src_taxid = taxService.RequestTaxId(MemoryID, DtNowBase); //1. //تولید شماره منحصربه فرد مالیاتی طبق تاریخ الان
                    src_Indatim = TaxService.ConvertDateToLong(DtNowBase); //2.
                    break;

                case 2: //اصلاحی
                case 3: //یا ابطالی
                    var iranTZ = TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time");
                    DtNowBase = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, iranTZ);

                    src_taxid = taxService.RequestTaxId(MemoryID, DtNowBase); //تولید شماره منحصربه فرد مالیاتی طبق تاریخ الان
                    src_Indatim = TaxService.ConvertDateToLong(DtNowBase);
                    break;

                default:
                    DtNowBase = TheFunctions.GetGregorianDateTime(L_DRV_TBL_US.First().DATE_N.ToString());

                    src_taxid = taxService.RequestTaxId(MemoryID, DtNowBase); //تولید شماره منحصربه فرد مالیاتی طبق تاریخ الان
                    src_Indatim = TaxService.ConvertDateToLong(DtNowBase);
                    break;
            }
            src_Indati2m = TaxService.ConvertDateToLong(DtNowBase); //فیلدی صرفا برای مقایسه فاصله زمانی ثبت فاکتور تا مرحله ارسال توسط برنامه البته اینجا معنی نداره


            string ECODE_M = null; //کد اقتصادی : Tinb
            string CODEMELI_M = null; // کد ملی/ شناسه ملی : Bid
            if (_HEAD_EXTENDED?.inty is 1) // الگوی صورت حساب روی نوع اول است
            {
                //حقیقی1  
                if (L_DRV_TBL_US.FirstOrDefault()?.tob == 1)
                {
                    //کد اقتصادی : Tinb
                    if (src_ECODE is not null) //Real Example : 10840014242 || 411375646679
                    {
                        if (src_ECODE?.Length > 14)
                        {
                            throw new NullyExceptiony("Over Length 14 Ecode for tob 1");
                        }
                        else ECODE_M = src_ECODE;
                    }
                }
                //حقوقی2
                else //L_DRV_TBL_US.FirstOrDefault()?.tob == 2
                {
                    //کد اقتصادی : Tinb
                    if (src_ECODE is not null)
                    {
                        if (src_ECODE?.Length > 11)
                        {
                            throw new NullyExceptiony("Over Length 11 Ecode for tob 2");
                        }
                        else ECODE_M = src_ECODE;
                    }
                }
            }

            if (_HEAD_EXTENDED?.inty is 1) // الگوی صورت حساب روی نوع اول است
            {
                //if (string.IsNullOrEmpty(CODEMELI_M))
                //{
                //    throw new NullyExceptiony("MCODE is null");
                //}
            }

            //New Edit Update Fields:
            var bbcStr = _HEAD_EXTENDED?.bbc;
            if (!long.TryParse(bbcStr, out var bbcVal) || bbcVal <= 0)
            {
                _HEAD_EXTENDED.bbc = null;
            }
            var sbcStr = _HEAD_EXTENDED?.sbc;
            if (!long.TryParse(sbcStr, out var sbcVal) || sbcVal <= 0)
            {
                _HEAD_EXTENDED.sbc = null;
            }


            //صادرات
            bool IsSaderaty7 = false;
            if (_HEAD_EXTENDED?.inp == 7) //الگوی صورتحساب
            {
                IsSaderaty7 = true;
            }
            else
            {
                _HEAD_EXTENDED.cut = null;
                _HEAD_EXTENDED.exr = null;
            }

            //محاسبه مجدد مبالغ جهت رفع اعشار
            #region FLOATFIXER 
            foreach (var item in L_DRV_TBL_US)
            {
                //1-MABL Cutter 
                item.MABL = Math.Truncate((decimal)item.MABL); // مبلغ 
                                                               //2- TAKHFIF Cutter
                item.N_MOIN = Math.Truncate((decimal)item.N_MOIN); //مبلغ تخفیف

                if (_HEAD_EXTENDED.ins == 4)   //اگر از نوع برگشتی است
                {
                    item.MEGH_MAR = Math.Round((decimal)item.MEGH_MAR, 4);

                    item.MEGHk = item.MEGH_MAR; //مقدار مرجوعی رو میذاریم جای مقدار کالا
                }
                else
                {
                    item.MEGHk = Math.Round((decimal)item.MEGHk, 4);
                }

                //3-MABL_K Calcute and Cut
                item.MABL_K = Math.Truncate((decimal)(item.MABL * item.MEGHk)); //مجموع مبلغ قبل از کسر تخفیف //Commented in 2024
                var ty = Convert.ToInt64((item.MABL * item.MEGHk));
                //double? MABLAHGH = item.MABL * item.MEGHk;
                //MABLAHGH = Math.Truncate((double)MABLAHGH);
                //item.MABL_K = Math.Round((double)(item.MABL * item.MEGHk)); //مجموع مبلغ قبل از کسر تخفیف //Added in 2024

                item.mabkbt = item.MABL_K - item.N_MOIN;  //مجموع مبلغ پس از کسر تخفیف    //dbo.INVO_LST.MABL_K - dbo.INVO_LST.N_MOIN AS mabkbt

                if (item?.vra > 0 && item.IMBAA <= 0) //نرخ درصد مالیات داره اما خود مبلغ مالیات نداره
                {
                    throw new NullyExceptiony("NO IMBAA BUT HAS VRA");
                }

                //حاصلضرب مبلغ کالا پس از کسر تخفیفات و سایر مبالغ که در قانون اشاره شده در نرخ مالیات بر ارزش افزوده.
                if (item.IMBAA > 0)
                {
                    item.IMBAA = item.mabkbt * item.vra / 100;

                    //4-IMBAA Cutter
                    item.IMBAA = Math.Truncate((decimal)item.IMBAA);
                }

                item.mabkn = item.mabkbt + item.IMBAA; //مبلغ کل کالا /خدمت  // dbo.INVO_LST.MABL_K - dbo.INVO_LST.N_MOIN + dbo.INVO_LST.IMBAA AS mabkn

            }
            #endregion

            //پاک کردن مقدار کل هایی که صفر است به خاطر مرجوع کامل یک سطر
            if (_HEAD_EXTENDED.ins == 4) //اگر از نوع برگشتی است
            {
                L_DRV_TBL_US = L_DRV_TBL_US.Where(row => row.MEGHk > 0).ToList();

                if (!L_DRV_TBL_US.Any())
                {
                    throw new NullyExceptiony($"صورت حساب برگشتی با شماره {NUMBER} شامل هیچ کالایی جهت ارسال نمی باشد.");
                }
            }

            var src_Sum_MABL_K = L_DRV_TBL_US.Sum(x => x.MABL_K);
            var src_Sum_N_MOIN = L_DRV_TBL_US.Sum(x => x.N_MOIN);
            var src_Sum_mabkbt = L_DRV_TBL_US.Sum(x => x.mabkbt);
            var src_Sum_IMBAA = L_DRV_TBL_US.Sum(x => x.IMBAA);
            var src_Sum_mabkn = L_DRV_TBL_US.Sum(x => x.mabkn);

            #region PrepareTheModel
            foreach (var item in L_DRV_TBL_US)
            {
                ////قطع اعشار از مبلغ قبل از تخفیف
                //var FMBLK = item.MEGHk * item.MABL;
                //item.MABL_K = (double?)Math.Floor((decimal)FMBLK);
                ////---------------------------------------------------------

                if (IsSaderaty7)
                {
                    _HEAD_EXTENDED.torv = src_Sum_mabkn;  //مجموع ارزش ریالی
                                                          //_HEAD_EXTENDED.exr //نرخ برابری ارز با ریال
                                                          //_HEAD_EXTENDED.cut //نوع ارز
                    DoGetwriteAppenLog($"_HEAD_EXTENDED.torv = {_HEAD_EXTENDED.torv}");

                    var SAA = (src_Sum_mabkn / _HEAD_EXTENDED.exr);
                    SAA = Math.Round((decimal)SAA, 4);
                    _HEAD_EXTENDED.tocv = SAA; //مجموع ارزش ارزی

                    DoGetwriteAppenLog($"_HEAD_EXTENDED.tocv = {_HEAD_EXTENDED.tocv}");

                    _HEAD_EXTENDED.ssrv = item.MABL; //ارزش ریالی کالا -----??????? or MABL_K

                    var AAK = (item.MABL / _HEAD_EXTENDED.exr);
                    AAK = Math.Round((decimal)AAK, 4);
                    _HEAD_EXTENDED.sscv = AAK; //ارزش ارزی کالا

                    DoGetwriteAppenLog($"_HEAD_EXTENDED.sscv = {_HEAD_EXTENDED.sscv}");
                }

                if (string.IsNullOrEmpty(_HEAD_EXTENDED?.crn) || _HEAD_EXTENDED.crn == "0") //New Line | 2024-05-07 سه شنبه - ۱۸ اردیبهشت ۱۴۰۳
                {
                    _HEAD_EXTENDED.crn = null; //شناسه یکتای ثبت قرار داد فروشنده
                }

                if (StarterInnoNumber == "1")
                {
                    StarterInnoNumber = NUMBER.ToString();
                }

                L_TAXDTL_US.Add(new TAXDTL
                {
                    //headz{
                    Taxid = src_taxid, //شماره منحصر به فرد مالیاتی
                    Indatim_Sec = src_Indatim, //تاریخ و زمان صدور صورت حساب - میلادی
                    Indati2m_Sec = src_Indati2m, //تاریخ و زمان ایجاد صورتحساب - میلادی
                    Inty = _HEAD_EXTENDED?.inty, //نوع صورت حساب // نوع اول , دوم , سوم
                    Inno = TheFunctions.InnoAddZeroes(StarterInnoNumber), // سریال صورت حساب
                    Irtaxid = "", //شماره منحصر به فرد مالیاتی صورتحساب مرجع - برای اصلاح , ابطال , برگشت
                    Inp = _HEAD_EXTENDED.inp, //الگوی صورتحساب // الگوی:1 فروشالگوی:2 فروش ارزی الگوی:3 صورتحساب طال، جواهر و پالتین
                    Ins = _HEAD_EXTENDED.ins, //موضوع صورتحساب //ابطالی , اصلاحی 
                    Tins = L_Baseknow_US.First().ECODE,/*"10840014242"*/ //WAS "MCODE" BEFORE //شماره اقتصادی فروشنده //توی نرم افزار شماره ثبت هم در درباره تهیه کنندگان زده
                    Tob = (item.tob is null ? 2 : item.tob), //نوع شخص خریدار
                    Bid = CODEMELI_M/*item.MCODEM*/, //شناسه ملی/ شماره ملی/ شناسه مشارکت مدنی/ کد فراگیر اتباع غیر ایرانی خریدار
                    Tinb = ECODE_M/*item.MCODEM*/, // شماره اقتصادی خریدار
                    Sbc = _HEAD_EXTENDED.sbc, //کد شعبه فروشنده
                    Bbc = (string.IsNullOrEmpty(_HEAD_EXTENDED.bbc) ? null : _HEAD_EXTENDED.bbc), //کد شعبه خریدار
                    Bpc = (string.IsNullOrEmpty(_HEAD_EXTENDED.bpc) ? null : _HEAD_EXTENDED?.bpc), //کد پستی خریدار
                    Ft = _HEAD_EXTENDED.ft, //نوع پرواز
                    Bpn = _HEAD_EXTENDED.bpn, //شماره گذرنامه خریدار
                    Scln = _HEAD_EXTENDED.scln, //شماره پروانه گمرکی
                    Scc = _HEAD_EXTENDED.scc, //کد گمرک محل اظهار فروشنده
                    Crn = (_HEAD_EXTENDED.crn), //شناسه یکتای ثبت قرار داد فروشنده
                    Billid = _HEAD_EXTENDED.billid, //شماره اشتراک/ شناسه قبض بهرهبردار
                    Tprdis = (decimal)src_Sum_MABL_K, //مجموع مبلغ قبل از کسر تخفیف
                    Tdis = (decimal)src_Sum_N_MOIN, //مجموع تخفیفات
                    Tadis = (decimal)src_Sum_mabkbt, // مجموع مبلغ پس از کسر تخفیف
                    Tvam = (decimal)src_Sum_IMBAA, // مجموع مالیات بر ارزش افزوده
                    Todam = _HEAD_EXTENDED.todam, // مجموع سایر مالیات، عوارض و وجوه قانونی
                    Tbill = (decimal)src_Sum_mabkn, //مجموع صورتحساب
                    Setm = _HEAD_EXTENDED.setm, //روش تسویه
                    Cap = _HEAD_EXTENDED.cap, //مبلغ پرداختی نقدی
                    Insp = _HEAD_EXTENDED.insp, //مبلغ نسیه
                    Tvop = _HEAD_EXTENDED.tvop, // مجموع سهم مالیات بر ارزش افزوده از پرداخت
                    Tax17 = _HEAD_EXTENDED.tax17,
                    Cdcd = _HEAD_EXTENDED.cdcd, // تاریخ کوتاژ اظهارنامه گمرکی
                    Tonw = _HEAD_EXTENDED.tonw, //مجموع وزن خالص
                    Torv = _HEAD_EXTENDED.torv, //مجموع ارزش ریالی
                    Tocv = _HEAD_EXTENDED.tocv, //مجموع ارزش ارزی

                    //head }

                    //body{
                    Sstid = item.sstid, //شناسه کالا/خدمت //CODE	STUF_DEF      
                    Sstt = item.KALA, //شرح کالا/خدمت //NAME	STUF_DEF
                    Mu = item.mu, //واحد اندازه گیری //VNAMES	TCOD_VAHEDS
                    Am = (decimal)item.MEGHk,//تعداد/مقدار //MEGH	INVO_LST
                    Fee = (decimal)item.MABL, //مبلغ واحد //MABL	INVO_LST
                    Cfee = 0, //میزان ارز
                    Cut = (string.IsNullOrEmpty(_HEAD_EXTENDED?.cut) ? null : _HEAD_EXTENDED?.cut), //نوع ارز
                    Exr = (_HEAD_EXTENDED?.exr is null ? null : _HEAD_EXTENDED?.exr), //نرخ برابری ارز با ریال
                    Prdis = (decimal)item.MABL_K, //مبلغ قبل از تخفیف //MABL_K	INVO_LST
                    Dis = (decimal)item.N_MOIN, //مبلغ تخفیف //N_MOIN	INVO_LST
                    Adis = (decimal)(item.MABL_K - item.N_MOIN), //مبلغ بعد از تخفیف //Sum(INVO_LST.MABL_K - INVO_LST.N_MOIN AS mabkbt)	INVO_LST
                    Vra = (decimal)item.vra, //نرخ مالیات بر ارزش افزوده
                    Vam = (decimal)item.IMBAA, //مبلغ مالیات بر ارزش افزوده //IMBAA	 INVO_LST
                    Odt = "0", //موضوع سایر مالیات و عوارض
                    Odr = 0, //نرخ سایر مالیات و عوارض
                    Odam = 0, //مبلغ سایر مالیات و عوارض
                    Olt = "0", //موضوع سایر وجوه قانونی
                    Olr = 0, //نرخ سایر وجوه قانونی
                    Olam = 0, //مبلغ سایر وجوه قانونی
                    Consfee = 0, //اجرت ساخت
                    Spro = 0, //سود فروشنده
                    Bros = 0, //حق العمل
                    Tcpbs = 0, //جمع کل اجرت , حق العمل و سود
                    Cop = 0, //سهم نقدی از پرداخت
                    Vop = 0, //سهم ارزش افزوده از پرداخت
                    Bsrn = null, //شناسه یکتای ثبت قرارداد حق العملکاری
                    Tsstam = (decimal)item.mabkn, //مبلغ کل کالا/خدمت //MABL_K	INVO_LST
                    Nw = 0, //وزن خالص
                    Ssrv = 0, //ارزش ریالی کالا
                    Sscv = 0 //ارزش ارزی کالا
                             //body}
                });
            }

            string irtaxid_RefNum_cancel = null;
            if (L_TAXDTL_US.First().Ins == 3)   //اگر از نوع ابطالی است
            {
                if (!string.IsNullOrEmpty(_HEAD_EXTENDED.irtaxid))
                {
                    irtaxid_RefNum_cancel = _HEAD_EXTENDED.irtaxid;
                }
                else
                {
                    throw new NullyExceptiony("irtaxid is null");
                }
            }

            if (L_TAXDTL_US.First().Ins == 4)   //اگر از نوع برگشتی است
            {
                if (!string.IsNullOrEmpty(_HEAD_EXTENDED.irtaxid))
                {
                    irtaxid_RefNum_cancel = _HEAD_EXTENDED.irtaxid;
                }
                else
                {
                    throw new NullyExceptiony("irtaxid is null");
                }
            }

            if (L_TAXDTL_US.First().Ins == 2)   //اگر از نوع اصلاحی است
            {
                if (!string.IsNullOrEmpty(_HEAD_EXTENDED.irtaxid))
                {
                    irtaxid_RefNum_cancel = _HEAD_EXTENDED.irtaxid;
                }
                else
                {
                    throw new NullyExceptiony("irtaxid is null");
                }
            }

            TaxModel.InvoiceModel.Header header = new TaxModel.InvoiceModel.Header();
            header.Taxid = L_TAXDTL_US.First().Taxid; //شماره منحصر به فرد مالیاتی
            header.Indatim = (long)L_TAXDTL_US.First().Indatim_Sec; //تاریخ و زمان صدور صورتحساب (میلادی)
            header.Indati2m = (long)L_TAXDTL_US.First().Indati2m_Sec; //تاریخ و زمان ایجاد صورتحساب (میلادی)

            header.Inty = Convert.ToInt32(L_TAXDTL_US.First().Inty); //(انواع صورتحساب الکترونیکی 1و2و3) نوع صورتحساب
            header.Inno = L_TAXDTL_US.First().Inno; //سریال صورتحساب  //NUMBER	 HEAD_LST
            //header.Irtaxid = L_TAXDTL_US.First().Irtaxid; //شماره منحصر به فرد مالیاتی صورتحساب مرجع
            header.Irtaxid = irtaxid_RefNum_cancel; //شماره منحصر به فرد مالیاتی صورتحساب مرجع
            header.Inp = Convert.ToInt32(L_TAXDTL_US.First().Inp); //الگوی صورتحساب
            header.Ins = Convert.ToInt32(L_TAXDTL_US.First().Ins); //موضوع صورتحساب
            header.Tins = L_TAXDTL_US.First().Tins; //شماره اقتصادی فروشنده //ECODE SAZMAN ******************************************************************************************
            header.Tob = Convert.ToInt32(L_TAXDTL_US.First().Tob); //نوع شخص خریدار
            header.Bid = L_TAXDTL_US.First().Bid; //شماره/شناسه ملی/شناسه مشارکت مدنی/کد فراگیر خریدار //MCODEM	SAZMAN
            header.Tinb = L_TAXDTL_US.First().Tinb; //شماره اقتصادی خریدار //ECODE CUST_HESAB
            header.Sbc = L_TAXDTL_US.First().Sbc; //کد شعبه فروشنده //MCODEM	CUST_HESAB
            header.Bbc = L_TAXDTL_US.First().Bbc; //کد شعبه خریدار
            header.Bpc = L_TAXDTL_US.First().Bpc; //کد پستی خریدار
            header.Ft = Convert.ToInt32(L_TAXDTL_US.First().Ft); //نوع پرواز
            //header.Bpn = L_TAXDTL_US.First().Bpn; //شماره گذرنامه خریدار
            header.Scln = L_TAXDTL_US.First().Scln; //شماره پروانه گمرکی فروشنده
            //header.Scc = L_TAXDTL_US.First().Scc; //کد گمرک محل اظهار
            header.Crn = L_TAXDTL_US.First().Crn; //شناسه یکتای ثبت قرارداد فروشنده
            header.Billid = L_TAXDTL_US.First().Billid; //شماره اشتراک/شناسه قبض بهره بردار
            header.Tprdis = Convert.ToDecimal(L_TAXDTL_US.First().Tprdis); //مجموع مبلغ قبل از کسر تخفیف //INVO_LST	Sum(MABL_K)
            header.Tdis = Convert.ToDecimal(L_TAXDTL_US.First().Tdis); //مجموع تخفیفات //INVO_LST	Sum(N_MOIN)
            header.Tadis = Convert.ToDecimal(L_TAXDTL_US.First().Tadis); //مجموع مبلغ پس از کسر تخفیف //INVO_LST 	Sum(INVO_LST.MABL_K - INVO_LST.N_MOIN AS mabkbt)
            header.Tvam = Convert.ToDecimal(L_TAXDTL_US.First().Tvam); //مجموع مالیات بر ارزش افزوده //INVO_LST	Sum(IMBAA)
            header.Todam = Convert.ToDecimal(L_TAXDTL_US.First().Todam); //مجموع سایر مالیات , عوارض و وجوه قانونی
            header.Tbill = Convert.ToDecimal(L_TAXDTL_US.First().Tbill); //مجموع صورت حساب //mabkn	INVO_LST.MABL_K-INVO_LST.N_MOIN+INVO_LST.IMBAA AS mabkn
            header.Setm = Convert.ToInt32(L_TAXDTL_US.First().Setm); //روش تسویه
            header.Cap = Convert.ToDecimal(L_TAXDTL_US.First().Cap); //مبلغ پرداختی نقدی
            header.Insp = Convert.ToDecimal(L_TAXDTL_US.First().Insp); //مبلغ پرداختی نسیه
            header.Tvop = Convert.ToDecimal(L_TAXDTL_US.First().Tvop); //مجموع سهم مالیات بر ارزش افزوده از پرداخت
            header.Tax17 = Convert.ToDecimal(L_TAXDTL_US.First().Tax17); //مالیات موضوع ماده 17
            header.Cdcd = Convert.ToInt32(L_TAXDTL_US.First().Cdcd); //تاریخ کوتاژ اظهارنامه گمرکی
            header.Tonw = Convert.ToDecimal(L_TAXDTL_US.First().Tonw); //مجموع وزن خالص
            header.Torv = Convert.ToDecimal(L_TAXDTL_US.First().Torv); //مجموع ارزش ریالی
            header.Tocv = Convert.ToDecimal(L_TAXDTL_US.First().Tocv); //مجموع ارزش ارزی

            List<TaxModel.InvoiceModel.Body>? bodies = new List<TaxModel.InvoiceModel.Body>();
            foreach (var item in L_TAXDTL_US)
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
            #endregion

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
                Console.WriteLine("Error: " + ex.Message);
            }
            #endregion

            TaxModel.SendInvoicesModel sendInvoicesModel = taxService.SendInvoices(header, bodies, payments);
            //CL_Generaly.DoGetwriteAppenLog("sendInvoicesModel Passed");

            //بروز رسانی کد های رهگیری در لیست سی شارپ
            for (int i = 0; i < L_TAXDTL_US.Count; i++)
            {
                L_TAXDTL_US[i].UID = sendInvoicesModel.Uid;
                L_TAXDTL_US[i].RefrenceNumber = sendInvoicesModel.ReferenceNumber;
            }

            //به کدام سامانه ارسال شده
            byte _apitypesent = 0;
            // [1 | True] = Main
            // [0 | False] = SandBox Testy
            if (CL_MOADIAN.TaxURL is "https://tp.tax.gov.ir/req/api/") // اگر روی اصلیه
                _apitypesent = 1;
            else
                _apitypesent = 0;


            FactorInfoSent.NUMBER = CL_MOADIAN.NUMBER.ToString();
            FactorInfoSent.ReferenceNumber = sendInvoicesModel.ReferenceNumber;
            FactorInfoSent.TaxID = sendInvoicesModel.TaxId;

            CL_Generaly.WriteRecordText($@"
                                             -----------------------------------------------------------------
                                             زمان : {DateTime.Now}
                                             شماره فاکتور : {NUMBER}
                                             شماره مالیاتی صورت حساب : {sendInvoicesModel.TaxId}
                                             شناسه (رهگیری) صورت حساب مودیان: {sendInvoicesModel.ReferenceNumber}
                                             -----------------------------------------------------------------");

            try
            {
                //{ درج در جدول مالیات در دیتابیس 
                foreach (var src_item in L_TAXDTL_US)
                {
                    IDD_OF_TAXDTL = TheFunctions.GetNewIDD();

                    const string insertSql = @"INSERT INTO dbo.TAXDTL (
Taxid, Indatim, Indati2m, Indatim_Sec, Indati2m_Sec, Inty, Inno, Irtaxid, Inp, Ins, Tins, Tob, Bid, Tinb, Sbc, Bpc, Ft, Bpn, Scln, Scc, Crn, Billid, Tprdis, Tdis, Tadis, Tvam, Todam, Tbill, Setm, Cap, Insp, Tvop, Tax17, Cdcd, Tonw, Torv, Tocv, Sstid, Sstt, Mu, Am, Fee, Cfee, Cut, Exr, Prdis, Dis, Adis, Vra, Vam, Odt, Odr, Odam, Olt, Olr, Olam, Consfee, Spro, Bros, Tcpbs, Cop, Vop, Bsrn, Tsstam, Nw, Ssrv, Sscv, IDD, UID, RefrenceNumber, TheStatus, ApiTypeSent, SentTaxMemory, NUMBER, TAG, DATE_N)
VALUES (@Taxid, @Indatim, @Indati2m, @Indatim_Sec, @Indati2m_Sec, @Inty, @Inno, @Irtaxid, @Inp, @Ins, @Tins, @Tob, @Bid, @Tinb, @Sbc, @Bpc, @Ft, @Bpn, @Scln, @Scc, @Crn, @Billid, @Tprdis, @Tdis, @Tadis, @Tvam, @Todam, @Tbill, @Setm, @Cap, @Insp, @Tvop, @Tax17, @Cdcd, @Tonw, @Torv, @Tocv, @Sstid, @Sstt, @Mu, @Am, @Fee, @Cfee, @Cut, @Exr, @Prdis, @Dis, @Adis, @Vra, @Vam, @Odt, @Odr, @Odam, @Olt, @Olr, @Olam, @Consfee, @Spro, @Bros, @Tcpbs, @Cop, @Vop, @Bsrn, @Tsstam, @Nw, @Ssrv, @Sscv, @IDD, @UID, @RefrenceNumber, @TheStatus, @ApiTypeSent, @SentTaxMemory, @NUMBER, @TAG, @DATE_N);";

                    var p = new
                    {
                        Taxid = SafeString(src_item.Taxid, 22),
                        Indatim = (DateTime?)null,
                        Indati2m = (DateTime?)null,
                        src_item.Indatim_Sec,
                        src_item.Indati2m_Sec,
                        src_item.Inty,
                        Inno = SafeString(src_item.Inno, 10),
                        Irtaxid = SafeString(irtaxid_RefNum_cancel, 22), //src_item.Irtaxid
                        src_item.Inp,
                        src_item.Ins,
                        Tins = SafeString(src_item.Tins, 14),
                        src_item.Tob,
                        Bid = SafeString(src_item.Bid, 12),
                        Tinb = SafeString(src_item.Tinb, 14) ?? string.Empty,
                        Sbc = SafeString(src_item.Sbc, 10),
                        Bpc = SafeString(src_item.Bpc, 10),
                        src_item.Ft,
                        Bpn = SafeString(src_item.Bpn, 9),
                        Scln = SafeString(src_item.Scln, 14),
                        Scc = SafeString(src_item.Scc, 5),
                        Crn = SafeString(src_item.Crn, 12),
                        Billid = SafeString(src_item.Billid, 19),
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
                        Sstid = SafeString(src_item.Sstid, 13),
                        Sstt = SafeString(src_item.Sstt, 400),
                        Mu = SafeDecimal(src_item.Mu),
                        src_item.Am,
                        src_item.Fee,
                        src_item.Cfee,
                        Cut = SafeString(src_item.Cut, 3),
                        src_item.Exr,
                        src_item.Prdis,
                        src_item.Dis,
                        src_item.Adis,
                        src_item.Vra,
                        src_item.Vam,
                        Odt = SafeString(src_item.Odt, 255),
                        src_item.Odr,
                        src_item.Odam,
                        Olt = SafeString(src_item.Olt, 255),
                        src_item.Olr,
                        src_item.Olam,
                        src_item.Consfee,
                        src_item.Spro,
                        src_item.Bros,
                        src_item.Tcpbs,
                        src_item.Cop,
                        src_item.Vop,
                        Bsrn = SafeString(src_item.Bsrn, 12),
                        src_item.Tsstam,
                        src_item.Nw,
                        src_item.Ssrv,
                        src_item.Sscv,
                        IDD = IDD_OF_TAXDTL,
                        UID = SafeString(src_item.UID, 100),
                        RefrenceNumber = SafeString(src_item.RefrenceNumber, 100),
                        TheStatus = "PENDING",
                        ApiTypeSent = _apitypesent,
                        SentTaxMemory = SafeString(MemoryID, 12),
                        NUMBER,
                        TAG,
                        DATE_N = L_DRV_TBL_US.First().DATE_N
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
