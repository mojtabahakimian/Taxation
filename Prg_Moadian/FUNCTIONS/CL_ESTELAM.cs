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

            const string updateSql = @"UPDATE dbo.TAXDTL SET TheConfirmationReferenceId=@ConfirmationReferenceId, TheError=@TheError, TheWarning=@TheWarning, TheStatus=@TheStatus, TheSuccess=@TheSuccess WHERE RefrenceNumber=@RefrenceNumber";
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
