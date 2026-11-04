using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.Generaly;
using Prg_Moadian.Service;
using Prg_Moadian.SQLMODELS;
using TaxCollectData.Library.Business;
using TaxCollectData.Library.Dto.Config;
using TaxCollectData.Library.Dto.Content;
using TaxCollectData.Library.Dto.Properties;
using TaxCollectData.Library.Enums;
using static Prg_Moadian.Generaly.CL_Generaly;

namespace Prg_Moadian.FUNCTIONS
{
    public class CL_ESTELAM
    {
        CL_CCNNMANAGER dbms = new CL_CCNNMANAGER();

        private string MemoryTax { get; set; }
        private string PrivateKeyTax { get; set; }
        /// <summary>
        /// آدرس یو آر ال برای استعلام
        /// </summary>
        public string TaxURL { get; set; }

        public static TaxService? taxService = null;
        public static TaxModel.InquiryByReferenceIdModel.Root root = null;

        /// <summary>
        /// مقدار های کلید حافظه و آدرس فرعی یا اصلی رو میگیره این کار به صورت دستی انجام میشه , برای اینکه بتونیم در حین باز بودن پنجره وضعیت رو عوض کنیم و در عین حال اگر لازم شد ریکوئست توکن بزنم
        /// </summary>
        public void GET_INIT_TAX(string _taxurl)
        {
            TaxURL = _taxurl;

            var _newsaz = dbms.DoGetDataSQL<SAZMAN>("SELECT MEMORYID,MEMORYIDsand,PRIVIATEKEY,Dcertificate FROM dbo.SAZMAN").FirstOrDefault();
            PrivateKeyTax = _newsaz.PRIVIATEKEY.Replace("-----BEGIN PRIVATE KEY-----\r\n", "").Replace("\r\n-----END PRIVATE KEY-----\r\n", "").Trim();

            if (TaxURL == "https://tp.tax.gov.ir/req/api/")
            {
                MemoryTax = _newsaz.MEMORYID.Trim(); //حافظه مالیاتی اصلی
            }
            else
            {
                MemoryTax = _newsaz.MEMORYIDsand.Trim(); //حافظه مالیاتی تستی سندباکس
            }

            #region TEST
            //TaxApiService.Instance.Init(MemoryTax, new SignatoryConfig(PrivateKeyTax, null), new NormalProperties(ClientType.SELF_TSP), TaxURL);
            //ServerInformationModel serverInformationModel = TaxApiService.Instance.TaxApis.GetServerInformation();


            //TokenModel tokenModel = TaxApiService.Instance.TaxApis.RequestToken();


            //var inquiryResultModels = TaxApiService.Instance.TaxApis.InquiryByReferenceId(new() { "8e786be5-395e-49e0-88e2-4fae9b75ea83" });
            //List<string> lst = new List<string>();
            ////lst.Add("7f67b747-29c1-4921-a93b-fdf733858cf0");
            //var resultModel0 = TaxApiService.Instance.TaxApis.InquiryByReferenceId(lst);
            //List<InquiryResultModel> resultModel = TaxApiService.Instance.TaxApis.InquiryByReferenceId(lst);

            //CL_Generaly.DoWritePRGLOG($"List<InquiryResultModel> resultModel = TaxApiService.Instance.TaxApis.InquiryByReferenceId(lst); : Passed", default);
            //string Data = resultModel.Select(x => x.Data).FirstOrDefault()?.ToString();
            //CL_Generaly.DoWritePRGLOG($"string Data = resultModel.Select(x => x.Data).FirstOrDefault()?.ToString(); : Passed", default);

            //JsonTextReader reader = new JsonTextReader(new StringReader(Data));
            //JsonSerializer jsonSerializer = new JsonSerializer();
            //CL_Generaly.DoWritePRGLOG($"JsonSerializer jsonSerializer = new JsonSerializer(); : Passed", default);
            //CL_MOADIAN.ErrorResult_PROP result = jsonSerializer.Deserialize<CL_MOADIAN.ErrorResult_PROP>(reader);
            //CL_Generaly.DoWritePRGLOG($"CL_MOADIAN.ErrorResult_PROP result = jsonSerializer.Deserialize<CL_MOADIAN.ErrorResult_PROP>(reader); : Passed", default);

            //CL_Generaly.DoWritePRGLOG($"result.Error : {result.Error}", default);
            #endregion

            if (TaxURL is null || TaxURL is "")
            {
                throw new NullyExceptiony("TaxURL is NULL");
            }
            if (MemoryTax is null || MemoryTax is "")
            {
                throw new NullyExceptiony("MemoryTax is NULL");
            }
            if (PrivateKeyTax is null || PrivateKeyTax is "")
            {
                throw new NullyExceptiony("PrivateKeyTax is NULL");
            }

            taxService = new TaxService(MemoryTax, PrivateKeyTax, TaxURL);
            try
            {
                var token = taxService.RequestToken();
                root = new TaxModel.InquiryByReferenceIdModel.Root();

                #region EXPIRATION_TIME_TOKEN
                TokenLifeTime.ExpirationTokenTimeUtc = TokenLifeTime.ServerUtcTime.AddMilliseconds(token.ExpireIn);
                #endregion
            }
            catch (Exception er)
            {
                if (TaxURL is "https://tp.tax.gov.ir/req/api/") //اصلی
                {
                    throw new NullyExceptiony("Authentication is not completed or incorrect entered");
                }
                else if (TaxURL is "https://sandboxrc.tax.gov.ir/req/api/") //آزمایشی
                {
                    if (er.Message is "امضای بسته صحیح نمی باشد")
                    {
                        try
                        {
                            DoGetwriteAppenLog($"{er.Message}  : \n" +
                            $"PrivateKeyTax :{PrivateKeyTax}\n" +
                            $"TaxURL : {TaxURL} \n" +
                            $"MemoryTax : {MemoryTax}\n" +
                            $"Exception : {er.InnerException}");
                        }
                        catch (Exception) { }
                        //throw new NullyExceptiony("Memory Tax is not Match");
                    }
                    throw new NullyExceptiony("Memory Tax is Incorrect");
                }
            }


        }
        public void GETESTELAM_REFCODE_UPDATE(string _the_refrence_code_)
        {
            root = taxService.InquiryByReferenceId(_the_refrence_code_);
            // سامانه چیزی برنگرداند: چیزی برای نوشتن نیست (قبلاً اینجا NullReference می‌شد).
            if (root is null) return;

            List<string>? _warn_lst = new List<string>();
            foreach (var item in root.warning)
                _warn_lst.Add(item.code + " | " + item.message);

            List<string>? _er_lst = new List<string>();
            foreach (var item in root.error)
                _er_lst.Add(item.code + " | " + item.message);

            string? ERVALS = _er_lst.Count > 0 ? string.Join(",", _er_lst) : null;
            string? WRVALS = _warn_lst.Count > 0 ? string.Join(",", _warn_lst) : null;

            //string? UPDT_QRE = "";
            //if (ERVALS is null)
            //    UPDT_QRE = $"UPDATE dbo.TAXDTL SET TheConfirmationReferenceId=N'{root.confirmationReferenceId}', TheError=NULL, TheStatus=N'{root.status}', TheSuccess={Convert.ToByte(root.success)} WHERE RefrenceNumber = N'{_the_refrence_code_}'";
            //if (WRVALS is null)
            //    UPDT_QRE = $"UPDATE dbo.TAXDTL SET TheConfirmationReferenceId=N'{root.confirmationReferenceId}', TheError=N'{(ERVALS)}', TheWarning=NULL ,TheStatus=N'{root.status}', TheSuccess={Convert.ToByte(root.success)} WHERE RefrenceNumber = N'{_the_refrence_code_}'";

            //if (ERVALS is not null && WRVALS is not null)
            //{
            //    UPDT_QRE = $"UPDATE dbo.TAXDTL SET TheConfirmationReferenceId=N'{root.confirmationReferenceId}', TheError=N'{(ERVALS)}',TheStatus=N'{root.status}', TheSuccess={Convert.ToByte(root.success)} WHERE RefrenceNumber = N'{_the_refrence_code_}'";
            //}
            //dbms.DoExecuteSQL(UPDT_QRE);

            // SUCCESS هرگز پایین نمی‌آید — همان قاعدهٔ PersistInquiryResult در پنجرهٔ پیگیری:
            // اگر یک استعلام گذرا FAILED بدهد و SUCCESS را FAILED کنیم، فاکتور به لیست
            // ارسال‌نشده‌ها برمی‌گردد و راه ثبت دوباره در کارپوشه باز می‌شود.
            const string updateSql = @"UPDATE dbo.TAXDTL SET TheConfirmationReferenceId=@ConfirmationReferenceId, TheError=@TheError, TheWarning=@TheWarning, TheStatus=@TheStatus, TheSuccess=@TheSuccess WHERE RefrenceNumber=@RefrenceNumber AND NOT (ISNULL(TheStatus, N'') = N'SUCCESS' AND ISNULL(@TheStatus, N'') <> N'SUCCESS')";
            var updateParams = new
            {
                ConfirmationReferenceId = SafeString((string?)root.confirmationReferenceId, 100),
                TheError = SafeString(ERVALS, 4000),
                TheWarning = SafeString(WRVALS, 4000),
                TheStatus = SafeString(root.status, 50),
                TheSuccess = Convert.ToByte(root.success),
                RefrenceNumber = SafeString(_the_refrence_code_, 100)
            };
            dbms.DoExecuteSQL(updateSql, updateParams);

            if (root.status == "SUCCESS")
            {
                try
                {
                    const string updateIrtaxidSql = @"
UPDATE hle
SET hle.irtaxid = t.Taxid
FROM dbo.HEAD_LST_EXTENDED hle
INNER JOIN dbo.TAXDTL t ON hle.NUMBER = t.NUMBER AND hle.TGU = t.TAG
WHERE t.RefrenceNumber = @RefrenceNumber AND t.Taxid IS NOT NULL";
                    dbms.DoExecuteSQL(updateIrtaxidSql, new { RefrenceNumber = SafeString(_the_refrence_code_, 100) });
                }
                catch (Exception) { }
            }
        }

        /// <summary>
        /// تعیین تکلیف ارسالی که پاسخش نرسید (UNKNOWN) با uid ِ خودمان.
        ///
        /// ارسال UNKNOWN کد رهگیری ندارد، پس InquiryByReferenceId نمی‌تواند بپرسدش. از
        /// ۱۴۰۵/۰۸/۱۳ مسیر تکی uid را پیش از ارسال خودش می‌سازد (مثل گروهی) و روی
        /// ردیف‌های UNKNOWN هم می‌نویسد؛ با همان uid و شناسهٔ حافظه از سامانه می‌پرسیم.
        ///
        /// فقط پاسخ قطعی چیزی را عوض می‌کند:
        ///   NOT_FOUND یا بی‌پاسخ → همان UNKNOWN می‌ماند. «پیدا نشد» ممکن است فقط
        ///     «هنوز نرسیده» باشد؛ اگر FAILED کنیم، فاکتور دوباره ارسال‌شدنی می‌شود.
        ///   PENDING / IN_PROGRESS → PENDING با کد رهگیری، تا استعلام عادی ادامه دهد.
        ///   SUCCESS / FAILED → همان، با کد رهگیری، خطا و هشدار.
        /// فقط ردیف‌هایی که هنوز UNKNOWN‌اند و همین Taxid و uid را دارند به‌روز می‌شوند.
        /// </summary>
        /// <returns>وضعیتی که سامانه گفت (NOT_FOUND هم)، یا null اگر پاسخی نیامد.</returns>
        public string? ResolveUnknownByUid(string taxid, string uid)
        {
            if (string.IsNullOrWhiteSpace(taxid) || string.IsNullOrWhiteSpace(uid)) return null;
            taxid = taxid.Trim(); uid = uid.Trim();

            var results = TaxApiService.Instance.TaxApis.InquiryByUidAndFiscalId(
                new List<UidAndFiscalId> { new UidAndFiscalId(uid, MemoryTax) });

            // پاسخ را با uid جفت کن، نه با ترتیب (بخش ۴ CLAUDE.md، باگ جفت‌شدن گروهی).
            var r = results?.FirstOrDefault(x => x != null &&
                        (string.IsNullOrWhiteSpace(x.Uid) || string.Equals(x.Uid.Trim(), uid, StringComparison.OrdinalIgnoreCase)));
            if (r is null) return null;

            var status = (r.Status ?? string.Empty).Trim().ToUpperInvariant();
            if (status.Length == 0) return null;
            if (status == "NOT_FOUND") return status;

            var written = SaveInquiryResult(dbms, r, reference: null, taxid: taxid, uid: uid, onlyIfUnknown: true);
            try { DoGetwriteAppenLog($"ResolveUnknownByUid taxid={taxid} uid={uid}: system={status} saved={written ?? "-"}"); } catch (Exception) { }
            return written ?? status;
        }

        /// <summary>
        /// نوشتن نتیجهٔ یک استعلام در TAXDTL — یک جا برای همهٔ مسیرها (استعلام دستی پنجره، شاخهٔ
        /// uid ِ آن، و تعیین تکلیف UNKNOWN)، تا هیچ‌کدام قاعده‌ها را جا نیندازد:
        ///
        ///   • فقط وضعیت قطعی یا در صف نوشته می‌شود: SUCCESS، FAILED، PENDING (IN_PROGRESS هم
        ///     PENDING ثبت می‌شود تا استعلام پس‌زمینه ادامه دهد). NOT_FOUND و خالی هیچ چیزی نمی‌نویسند.
        ///   • SUCCESS هرگز پایین نمی‌آید (سامانه ثبات ندارد؛ بخش ۴ CLAUDE.md).
        ///   • کلید: یا کد رهگیری، یا Taxid + uid. در حالت uid، پاسخ فقط وقتی پذیرفته می‌شود که
        ///     uid ِ برگشتی (اگر آمده) همان uid ِ پرسیده‌شده باشد؛ کد رهگیریِ موجود بازنویسی نمی‌شود.
        ///   • اگر بدنهٔ پاسخ خوانا نبود، فقط وضعیت نوشته می‌شود؛ خطا و هشدار قبلی پاک نمی‌شوند.
        /// </summary>
        /// <returns>وضعیتی که نوشته شد، یا null اگر چیزی نوشته نشد.</returns>
        public static string? SaveInquiryResult(CL_CCNNMANAGER db, InquiryResultModel? result,
                                                string? reference, string? taxid = null, string? uid = null,
                                                bool onlyIfUnknown = false)
        {
            if (result is null) return null;
            var status = (result.Status ?? string.Empty).Trim().ToUpperInvariant();
            if (status == "IN_PROGRESS") status = "PENDING";
            if (status is not ("SUCCESS" or "FAILED" or "PENDING")) return null;

            bool byUid = !string.IsNullOrWhiteSpace(taxid) && !string.IsNullOrWhiteSpace(uid);
            if (!byUid && string.IsNullOrWhiteSpace(reference)) return null;
            if (byUid && !string.IsNullOrWhiteSpace(result.Uid) &&
                !string.Equals(result.Uid.Trim(), uid!.Trim(), StringComparison.OrdinalIgnoreCase))
                return null;   // پاسخ مال تلاش دیگری است

            TaxModel.InquiryByReferenceIdModel.Root? data = null;
            try
            {
                var json = result.Data?.ToString();
                if (!string.IsNullOrWhiteSpace(json))
                    data = JsonConvert.DeserializeObject<TaxModel.InquiryByReferenceIdModel.Root>(json);
            }
            catch (Exception) { data = null; }

            string where = byUid ? "Taxid = @Taxid AND UID = @Uid" : "RefrenceNumber = @Reference";
            if (onlyIfUnknown) where += " AND TheStatus = N'UNKNOWN'";
            where += " AND NOT (ISNULL(TheStatus, N'') = N'SUCCESS' AND @TheStatus <> N'SUCCESS')";

            string set = "TheStatus = @TheStatus, TheSuccess = @TheSuccess";
            if (byUid) set += ", RefrenceNumber = COALESCE(NULLIF(RefrenceNumber, ''), @NewRef)";
            if (data is not null)
                set += ", TheConfirmationReferenceId = @Conf, TheError = @TheError, TheWarning = @TheWarning";

            var errs = (data?.error ?? new List<TaxModel.InquiryByReferenceIdModel.Error>()).Select(x => x.code + " | " + x.message).ToList();
            var warns = (data?.warning ?? new List<TaxModel.InquiryByReferenceIdModel.Warning>()).Select(x => x.code + " | " + x.message).ToList();

            db.DoExecuteSQL($"UPDATE dbo.TAXDTL SET {set} WHERE {where}", new
            {
                TheStatus = status,
                TheSuccess = status == "SUCCESS" ? (byte)1 : (byte)0,
                NewRef = SafeString(result.ReferenceNumber, 100),
                Conf = SafeString(data?.confirmationReferenceId?.ToString(), 100),
                TheError = SafeString(errs.Count > 0 ? string.Join(",", errs) : null, 4000),
                TheWarning = SafeString(warns.Count > 0 ? string.Join(",", warns) : null, 4000),
                Taxid = taxid?.Trim(),
                Uid = uid?.Trim(),
                Reference = SafeString(reference, 100)
            });

            if (status == "SUCCESS")
            {
                try
                {
                    db.DoExecuteSQL($@"
UPDATE hle
SET hle.irtaxid = t.Taxid
FROM dbo.HEAD_LST_EXTENDED hle
INNER JOIN dbo.TAXDTL t ON hle.NUMBER = t.NUMBER AND hle.TGU = t.TAG
WHERE {(byUid ? "t.Taxid = @Taxid AND t.UID = @Uid" : "t.RefrenceNumber = @Reference")} AND t.Taxid IS NOT NULL",
                        new { Taxid = taxid?.Trim(), Uid = uid?.Trim(), Reference = SafeString(reference, 100) });
                }
                catch (Exception) { }
            }
            return status;
        }

        private static string? SafeString(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            value = value.Trim();
            return value.Length > maxLength ? value.Substring(0, maxLength) : value;
        }
        private static decimal? SafeDecimal(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return decimal.TryParse(value, out var d) ? d : (decimal?)null;
        }

        public string? GETESTELAM_REFCODE_MANUALLY(string _the_refrence_code_)
        {
            TaxApiService.Instance.Init(MemoryTax, new SignatoryConfig(PrivateKeyTax, null), new NormalProperties(ClientType.SELF_TSP), TaxURL);
            ServerInformationModel serverInformationModel = TaxApiService.Instance.TaxApis.GetServerInformation();
            TokenModel tokenModel = TaxApiService.Instance.TaxApis.RequestToken();

            List<string> list = new List<string>();
            list.Add(_the_refrence_code_);
            List<InquiryResultModel> list2 = TaxApiService.Instance.TaxApis.InquiryByReferenceId(list);
            TaxModel.InquiryByReferenceIdModel inquiryByReferenceIdModel = new TaxModel.InquiryByReferenceIdModel();
            string value = list2.Select((InquiryResultModel x) => x.Data).FirstOrDefault()!.ToString();
            TaxModel.InquiryByReferenceIdModel.Root manuallroot = JsonConvert.DeserializeObject<TaxModel.InquiryByReferenceIdModel.Root>(value);
            manuallroot.status = list2[0].Status;

            //List<string>? _warn_lst = new List<string>();
            //foreach (var item in manuallroot.warning)
            //    _warn_lst.Add(item.code + " | " + item.message);

            List<string>? _er_lst = new List<string>();
            foreach (var item in manuallroot.error)
                _er_lst.Add(item.code + " | " + item.message);

            string? ERVALS = (_er_lst != null && _er_lst.Count > 0) ? $"{string.Join(",", _er_lst)}" : null;
            //string? WRVALS = (_warn_lst != null && _warn_lst.Count > 0) ? $"{string.Join(",", _warn_lst)}" : null;

            #region MAIN_SDK
            var inquiryResultModels = TaxApiService.Instance.TaxApis.InquiryByReferenceId(new() { _the_refrence_code_ });
            if (inquiryResultModels.FirstOrDefault().Status is "SUCCESS")
            {
                ERVALS = "SUCCESS";
            }
            #endregion
            return ERVALS;
        }
    }
}
