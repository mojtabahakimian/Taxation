using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Security;
using System.Security.Cryptography;
using System.Text;
using static Prg_Moadian.FUNCTIONS.CL_MOADIAN;
using TaxCollectData.Library.Business;
using TaxCollectData.Library.Dto.Config;
using TaxCollectData.Library.Dto.Content;
using TaxCollectData.Library.Dto.Properties;
using TaxCollectData.Library.Enums;
using Org.BouncyCastle.OpenSsl;

namespace Prg_Moadian.FUNCTIONS
{
    public class CL_ORIGINAL_SDK
    {
        public static void TestSendViaSDK()
        {
            var privateKey = File.ReadAllText("C:\\Correct\\privatekeyfile.txt");
            TaxApiService.Instance.Init("A278R7", new SignatoryConfig(privateKey, null), new NormalProperties(ClientType.SELF_TSP), TaxURL);
            ServerInformationModel serverInformationModel = TaxApiService.Instance.TaxApis.GetServerInformation();

            TokenModel tokenModel = TaxApiService.Instance.TaxApis.RequestToken();

            #region Rahgiri
            var inquiryResultModels = TaxApiService.Instance.TaxApis.InquiryByReferenceId(new() { "7f67b747-29c1-4921-a93b-fdf733858cf0" });

            List<string> lst = new List<string>();
            lst.Add("7f67b747-29c1-4921-a93b-fdf733858cf0");
            List<InquiryResultModel> resultModel = TaxApiService.Instance.TaxApis.InquiryByReferenceId(lst);

            string Data = resultModel.Select(x => x.Data).FirstOrDefault().ToString();

            JsonTextReader reader = new JsonTextReader(new StringReader(Data));
            JsonSerializer jsonSerializer = new JsonSerializer();
            ErrorResult_PROP result = jsonSerializer.Deserialize<ErrorResult_PROP>(reader);

            #endregion

            List<InvoiceDto> invoiceDtos = new List<InvoiceDto>();

            List<InvoiceBodyDto> invoiceBodyDtos = new List<InvoiceBodyDto>();
            List<PaymentDto> paymentDtos = new List<PaymentDto>();
            List<InvoiceExtension> invoiceExtensions = new List<InvoiceExtension>();

            var random = new Random();
            long randomSerialDecimal = random.Next(999999999);
            var taxId = TaxApiService.Instance.TaxIdGenerator.GenerateTaxId("A278R7", randomSerialDecimal, DateTime.Now);

            var now = new DateTimeOffset(DateTime.Now).ToUnixTimeMilliseconds();

            invoiceBodyDtos.Add(new InvoiceBodyDto
            {
                Sstid = "1254219865985",
                Sstt = "روغن بهران",
                Am = 1,
                //Mu = 1,
                Fee = 1000,
                Cfee = 0,
                Cut = "0",
                //Exr = "0",
                Prdis = 1000,
                Dis = 0,
                Adis = 1000,
                Vra = 0.09M,
                Vam = 10,
                Odt = "0",
                Odr = 0,
                Odam = 0,
                Olt = "0",
                Olr = 0,
                Olam = 0,
                Consfee = 0,
                Spro = 0,
                Bros = 0,
                Tcpbs = 0,
                Cop = 0,
                //Vop = "1000",
                Bsrn = "",
                Tsstam = 1010,
            });
            paymentDtos.Add(new PaymentDto
            {
                Iinn = "125036",
                Acn = "252544",
                Trmn = "2356566",
                Trn = "252545",
                Pcn = "6037991785693265",
                Pid = "19117484910002",
                Pdt = 1665490061447
            });
            invoiceExtensions.Add(new InvoiceExtension
            {

            });
            InvoiceHeaderDto invoiceHeaderDto = new InvoiceHeaderDto
            {
                Taxid = taxId,
                Indatim = 1665490063785,
                Indati2m = 1665490063785,
                Inty = 1,
                //Inno = 0000011300,
                Irtaxid = "",
                Inp = 1,
                Ins = 1,
                Tins = "19117484910001",
                Tob = 1,
                Bid = "0",
                Tinb = "19117484910002",
                Sbc = "0",
                Bpc = "0",
                Bbc = "0",
                Ft = 0,
                Bpn = "0",
                //Scln = 0,
                Scc = "0",
                //Crn = 0,
                Billid = "0",
                Tprdis = 1000,
                Tdis = 0,
                Tadis = 0,
                Tvam = 10,
                Todam = 0,
                Tbill = 1010,
                Setm = 1,
                Cap = 1010,
                Insp = 0,
                Tvop = 1010,
                Tax17 = 0

            };
            invoiceDtos.Add(new InvoiceDto
            {
                Header = invoiceHeaderDto,
                Body = invoiceBodyDtos,
                Payments = paymentDtos,
                Extension = invoiceExtensions
            });


            var responseModel = TaxApiService.Instance.TaxApis.SendInvoices(invoiceDtos, null);
            var packetResponse = responseModel.Body.Result.First();
            var uid = packetResponse.Uid;
            var referenceNumber = packetResponse.ReferenceNumber;


        }
        public class CryptoUtils
        {
            public static byte[] StringToByteArray(string hex)
            {
                return Enumerable.Range(0, hex.Length).Where(x => x % 2 == 0).Select(x => Convert.ToByte(hex.Substring(x, 2), 16)).ToArray();
            }
            public static string NormalJson(object obj, Dictionary<string, string> header)
            {
                if (obj == null && header == null)
                    throw new AccessViolationException();
                Dictionary<string, object> map = null;
                if (obj != null)
                {
                    if (obj.GetType() == typeof(string))
                    {
                        if (obj.ToString().Trim().StartsWith("["))
                        {
                            obj = ToList<object>((string)obj);
                        }
                        else
                        {
                            obj = JsonConvert.DeserializeObject<object>((string)obj);
                        }
                    }
                    if (obj.GetType().IsGenericType && obj.GetType().GetGenericTypeDefinition() == typeof(List<>))
                    {
                        // PacketsWrapper packetsWrapper = new PacketsWrapper(obj); map = ToDictionary<object>(packetsWrapper);
                    }
                    else
                    {
                        map = ToDictionary<object>(obj);
                    }
                }
                if (map == null && header != null)
                {
                    map = new Dictionary<string, object>(); foreach (var headerElem in header) map.Add(headerElem.Key, headerElem.Value.ToString());
                }
                if (map != null && header != null)
                {
                    foreach (var headerElem in header)
                        map.Add(headerElem.Key, headerElem.Value);
                }
                Dictionary<string, object> result = new Dictionary<string, object>();
                result = JsonHelper.DeserializeAndFlatten(JsonConvert.SerializeObject(map));
                StringBuilder sb = new StringBuilder();
                HashSet<string> keysSet = new HashSet<string>(result.Keys);
                if (keysSet == null || !keysSet.Any()) { return null; }
                var keys = keysSet.OrderBy(x => x).ToList();
                foreach (var key in keys)
                {
                    string textValue; object value;
                    if (result.TryGetValue(key, out value))
                    {
                        if (value != null)
                        {
                            if (value.Equals(true) || value.Equals(false) || value.ToString().Equals("False") || value.ToString().Equals("True"))
                            {
                                textValue = value.ToString().ToLower();
                            }
                            else
                            {
                                textValue = value.ToString();
                            }
                            if (textValue == null || textValue.Equals(""))
                            {
                                textValue = "#";
                            }
                            else
                            {
                                textValue = textValue.Replace("#", "##");
                            }
                        }
                        else
                        {
                            textValue = "#";
                        }
                    }
                    else
                    {
                        textValue = "#";
                    }
                    sb.Append(textValue).Append('#');
                }
                return sb.Remove(sb.Length - 1, 1).ToString();
            }
            private static string getKey(string rootKey, string myKey)
            {
                if (rootKey != null)
                {
                    return rootKey + "." + myKey;
                }
                else
                {
                    return myKey;
                }
            }
            public static Dictionary<string, TValue> ToDictionary<TValue>(object obj)
            {
                var json = JsonConvert.SerializeObject(obj);
                var dictionary = JsonConvert.DeserializeObject<Dictionary<string, TValue>>(json);
                return dictionary;
            }
            public static List<Dictionary<string, object>> ToList<TValue>(string obj)
            {
                var dictionary = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(obj);
                return dictionary;
            }
        }
        public class JsonHelper
        {
            public static Dictionary<string, object> DeserializeAndFlatten(string json)
            {
                Dictionary<string, object> dict = new Dictionary<string, object>();
                JToken token = JToken.ReadFrom(new JsonTextReader(new StringReader(json)));
                FillDictionaryFromJToken(dict, token, "");
                return dict;
            }
            private static void FillDictionaryFromJToken(Dictionary<string, object> dict, JToken token, string prefix)
            {
                switch (token.Type)
                {
                    case JTokenType.Object:
                        foreach (JProperty prop in token.Children<JProperty>())
                        {
                            FillDictionaryFromJToken(dict, prop.Value, Join(prefix, prop.Name));
                        }
                        break;
                    case JTokenType.Array:
                        int index = 0;
                        foreach (JToken value in token.Children())
                        {
                            FillDictionaryFromJToken(dict, value, Join(prefix, index.ToString()));
                            index++;
                        }
                        break;
                    default:
                        dict.Add(prefix, ((JValue)token).Value);
                        break;
                }
            }
            private static string Join(string prefix, string name)
            {
                return (string.IsNullOrEmpty(prefix) ? name : prefix + "." + name);
            }
        }
        public class OtherMethods
        {
            public static string SignData(String stringToBeSigned, string privateKey)
            {
                var pem = "-----BEGIN PRIVATE KEY-----\n" + privateKey + "\n-----  END PRIVATE KEY---- - "; // Add header and footer
                PemReader pr = new PemReader(new StringReader(pem));
                AsymmetricKeyParameter privateKeyParams =
                (AsymmetricKeyParameter)pr.ReadObject();
                RSAParameters rsaParams =
                DotNetUtilities.ToRSAParameters((RsaPrivateCrtKeyParameters)privateKeyParams)
                ;
                RSACryptoServiceProvider csp = new RSACryptoServiceProvider();//                 cspParams);
                csp.ImportParameters((RSAParameters)rsaParams);
                var dataBytes = Encoding.UTF8.GetBytes(stringToBeSigned);
                return Convert.ToBase64String(csp.SignData(dataBytes,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            }
        }
    }
}
