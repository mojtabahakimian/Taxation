// ============================================================================
//  شیم TaxCollectData.Library 0.0.34 — فقط برای اجرای هارنس تست روی لینوکس.
//  هرگز همراه برنامه منتشر نشود. توضیح در README.md همین پوشه.
// ============================================================================
//
//  لایهٔ «SDK → سیم» بازنویسی ماست، نه کد رسمی. پروتکل از روی
//  TestTools/LocalMoadian/moadian_mock.py (که خودش از روی SDK 0.0.34 نوشته شده)
//  و قالب JSON از روی golden/*.json پیاده شده است.

using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using TaxCollectData.Library.Dto;
using TaxCollectData.Library.Dto.Config;
using TaxCollectData.Library.Dto.Content;
using TaxCollectData.Library.Dto.Properties;
using TaxCollectData.Library.Dto.Transfer;
using TaxCollectData.Library.Enums;

namespace TaxCollectData.Library.Business
{
    /// <summary>
    /// کد تولید این استثنا را با نام نوعش می‌شناسد
    /// (<c>ex.GetType().Name == "TaxApiException"</c>) چون در SDK واقعی internal است.
    /// </summary>
    internal sealed class TaxApiException : Exception
    {
        public TaxApiException(string message, int status = 0, Exception inner = null) : base(message, inner)
        {
            Status = status;
        }
        public int Status { get; }
    }

    public sealed class TaxApiService
    {
        private static readonly Lazy<TaxApiService> _instance = new(() => new TaxApiService());
        public static TaxApiService Instance => _instance.Value;

        private TaxApiService() { }

        public ITaxApis TaxApis { get; private set; }
        public ITaxIdGenerator TaxIdGenerator { get; } = new DefaultTaxIdGenerator();

        public void Init(string clientId, SignatoryConfig signatoryConfig, NormalProperties normalProperties,
                         string apiUrl = "https://tp.tax.gov.ir/req/api/", EncryptionConfig encryptionConfig = null)
        {
            TaxApis = new ShimTaxApis(clientId, signatoryConfig, normalProperties, apiUrl, encryptionConfig);
        }
    }

    public interface ITaxIdGenerator
    {
        string GenerateTaxId(string memoryId, long serial, DateTime createDate);
    }

    public interface ITaxApis
    {
        ServerInformationModel GetServerInformation();
        TokenModel RequestToken();
        HttpResponse<AsyncResponseModel> SendInvoices(List<InvoiceDto> invoices, Dictionary<string, string> headers);
        List<InquiryResultModel> InquiryByReferenceId(List<string> referenceIds, DateTime? start = null, DateTime? end = null);
        List<InquiryResultModel> InquiryByUidAndFiscalId(List<UidAndFiscalId> uidAndFiscalIds, DateTime? start = null, DateTime? end = null);
        EconomicCodeModel GetEconomicCodeInformation(string economicCode);
        FiscalInformationModel GetFiscalInformation(string memoryId);
    }

    /// <summary>
    /// ساخت شماره منحصربه‌فرد مالیاتی ۲۲ کاراکتری:
    ///   memoryId(6) + hex(روزهای گذشته از اپک، ۵ رقم) + hex(serial، ۱۰ رقم) + رقم کنترل Verhoeff
    /// رقم کنترل روی رشتهٔ دهدهی
    ///   toDecimal(memoryId) + روزها(۶ رقم) + serial(۱۲ رقم)
    /// حساب می‌شود که در toDecimal هر رقم خودش و هر حرف کد ASCII اش است.
    ///
    /// شاهد: سه شماره مالیاتی واقعی که در کامنت‌های مخزن مانده‌اند
    /// (A278R704C75000163ECF64، A2HEZ304D010021C3C5D15، A2HGPP04F5E00227BB8965)
    /// با همین فرمول رقم کنترل درست می‌گیرند (۳ از ۳).
    ///
    /// عدم قطعیت: الگوریتم از روی SDK عمومی بازسازی شده، نه از سورس 0.0.34.
    /// محاسبهٔ «روز» (UTC یا وقت محلی، و رفتار با DateTime.Kind) قطعی نیست —
    /// اینجا new DateTimeOffset(date) است، یعنی Unspecified/Local با منطقهٔ زمانی
    /// ماشین. نزدیک نیمه‌شب ایران ممکن است یک روز با SDK واقعی فرق کند. هارنس فقط
    /// طول ۲۲ و پیشوند memoryId را می‌سنجد (۳-۱، ۳-۲).
    /// </summary>
    internal sealed class DefaultTaxIdGenerator : ITaxIdGenerator
    {
        public string GenerateTaxId(string memoryId, long serial, DateTime createDate)
        {
            long days = new DateTimeOffset(createDate).ToUnixTimeMilliseconds() / 86_400_000L;
            string hexTime = days.ToString("X").PadLeft(5, '0');
            string hexSerial = serial.ToString("X").PadLeft(10, '0');

            var dec = new StringBuilder();
            foreach (char c in memoryId)
                dec.Append(char.IsDigit(c) ? c.ToString() : ((int)c).ToString());
            dec.Append(days.ToString().PadLeft(6, '0'));
            dec.Append(serial.ToString().PadLeft(12, '0'));

            char check = Verhoeff.Generate(dec.ToString());
            return (memoryId + hexTime + hexSerial + check).ToUpperInvariant();
        }
    }

    internal static class Verhoeff
    {
        private static readonly int[,] D =
        {
            {0,1,2,3,4,5,6,7,8,9},{1,2,3,4,0,6,7,8,9,5},{2,3,4,0,1,7,8,9,5,6},{3,4,0,1,2,8,9,5,6,7},
            {4,0,1,2,3,9,5,6,7,8},{5,9,8,7,6,0,4,3,2,1},{6,5,9,8,7,1,0,4,3,2},{7,6,5,9,8,2,1,0,4,3},
            {8,7,6,5,9,3,2,1,0,4},{9,8,7,6,5,4,3,2,1,0},
        };
        private static readonly int[,] P =
        {
            {0,1,2,3,4,5,6,7,8,9},{1,5,7,6,2,8,3,0,9,4},{5,8,0,3,7,9,6,1,4,2},{8,9,1,6,0,4,3,5,2,7},
            {9,4,5,3,1,2,6,8,7,0},{4,2,8,6,5,7,3,9,0,1},{2,7,9,3,8,0,6,4,1,5},{7,0,4,6,9,1,3,2,5,8},
        };
        private static readonly int[] Inv = { 0, 4, 3, 2, 1, 5, 6, 7, 8, 9 };

        public static char Generate(string digits)
        {
            int c = 0;
            for (int i = 0; i < digits.Length; i++)
            {
                int d = digits[digits.Length - 1 - i] - '0';
                c = D[c, P[(i + 1) % 8, d]];
            }
            return (char)('0' + Inv[c]);
        }
    }

    internal sealed class ShimTaxApis : ITaxApis
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(100) };

        private readonly string _clientId;
        private readonly string _baseUrl;
        private readonly string _prefix;
        private readonly RSA _signer;
        private string _token;
        private RSA _serverKey;
        private string _serverKeyId;

        public ShimTaxApis(string clientId, SignatoryConfig signatory, NormalProperties props, string apiUrl,
                           EncryptionConfig encryption)
        {
            _clientId = clientId;
            _baseUrl = (apiUrl ?? "").EndsWith("/") ? apiUrl : apiUrl + "/";
            _prefix = props?.ClientType == ClientType.TSP ? "tsp" : "self-tsp";
            _signer = Crypto.LoadPrivateKey(signatory?.PrivateKey);
            if (encryption != null)
            {
                _serverKey = Crypto.LoadPublicKey(encryption.PublicKey);
                _serverKeyId = encryption.KeyId;
            }
        }

        // ------------------------------------------------------------ sync

        public ServerInformationModel GetServerInformation()
        {
            var data = Sync("GET_SERVER_INFORMATION", null, auth: false);
            var model = data?.ToObject<ServerInformationModel>();
            var key = model?.PublicKeys?.FirstOrDefault();
            if (key != null && !string.IsNullOrEmpty(key.Key))
            {
                _serverKey = Crypto.LoadPublicKey(key.Key);
                _serverKeyId = key.Id;
            }
            return model;
        }

        public TokenModel RequestToken()
        {
            var data = Sync("GET_TOKEN", new JObject { ["username"] = _clientId }, auth: false);
            var model = data?.ToObject<TokenModel>();
            _token = model?.Token;
            return model;
        }

        public List<InquiryResultModel> InquiryByReferenceId(List<string> referenceIds, DateTime? start = null, DateTime? end = null)
        {
            var q = new JObject { ["referenceNumber"] = new JArray(referenceIds ?? new List<string>()) };
            if (start.HasValue) q["start"] = start.Value.ToString("yyyy-MM-ddTHH:mm:ss");
            if (end.HasValue) q["end"] = end.Value.ToString("yyyy-MM-ddTHH:mm:ss");
            return Sync("INQUIRY_BY_REFERENCE_NUMBER", q, auth: true)?.ToObject<List<InquiryResultModel>>()
                   ?? new List<InquiryResultModel>();
        }

        public List<InquiryResultModel> InquiryByUidAndFiscalId(List<UidAndFiscalId> ids, DateTime? start = null, DateTime? end = null)
        {
            var arr = new JArray((ids ?? new()).Select(x => new JObject { ["uid"] = x.Uid, ["fiscalId"] = x.FiscalId }));
            return Sync("INQUIRY_BY_UID", arr, auth: true)?.ToObject<List<InquiryResultModel>>()
                   ?? new List<InquiryResultModel>();
        }

        public EconomicCodeModel GetEconomicCodeInformation(string economicCode) =>
            Sync("GET_ECONOMIC_CODE_INFORMATION", new JObject { ["economicCode"] = economicCode }, auth: true)
                ?.ToObject<EconomicCodeModel>();

        public FiscalInformationModel GetFiscalInformation(string memoryId) =>
            Sync("GET_FISCAL_INFORMATION", new JObject { ["memoryId"] = memoryId }, auth: true)
                ?.ToObject<FiscalInformationModel>();

        private JToken Sync(string packetType, JToken data, bool auth)
        {
            var packet = new JObject
            {
                ["uid"] = Guid.NewGuid().ToString(),
                ["packetType"] = packetType,
                ["retry"] = false,
                ["data"] = data,
                ["encryptionKeyId"] = null,
                ["symmetricKey"] = null,
                ["iv"] = null,
                ["fiscalId"] = _clientId,
                ["dataSignature"] = null,
            };
            var headers = NewHeaders(auth);
            var envelope = new JObject
            {
                ["packet"] = packet,
                ["signature"] = Sign(Normalizer.Normalize(packet, headers)),
                ["signatureKeyId"] = null,
            };
            var (status, body) = Post($"{_prefix}/sync/{packetType}", envelope, headers);
            if (status < 200 || status >= 300 || body == null)
                throw new TaxApiException($"HTTP {status} on {packetType}: {Trunc(body?.ToString())}", status);

            if (body["errors"] is JArray errs && errs.Count > 0)
                throw new TaxApiException(string.Join(" | ",
                    errs.Select(e => $"{e["errorCode"]}: {e["detail"]}")), status);

            return body["result"]?["data"];
        }

        // ------------------------------------------------------------ async

        public HttpResponse<AsyncResponseModel> SendInvoices(List<InvoiceDto> invoices, Dictionary<string, string> extraHeaders)
        {
            if (_serverKey == null) GetServerInformation();
            if (_serverKey == null) throw new TaxApiException("server public key unavailable");

            var packets = new JArray();
            foreach (var inv in invoices ?? new List<InvoiceDto>())
            {
                string json = InvoiceJson.Serialize(inv);
                var enc = Crypto.EncryptPacketData(Encoding.UTF8.GetBytes(json), _serverKey);
                packets.Add(new JObject
                {
                    ["uid"] = Guid.NewGuid().ToString(),
                    ["packetType"] = "INVOICE.V01",
                    ["retry"] = false,
                    ["data"] = enc.Data,
                    ["encryptionKeyId"] = _serverKeyId,
                    ["symmetricKey"] = enc.SymmetricKey,
                    ["iv"] = enc.Iv,
                    ["fiscalId"] = _clientId,
                    ["dataSignature"] = Sign(Normalizer.Normalize(JToken.Parse(json), null)),
                });
            }

            var headers = NewHeaders(auth: true);
            if (extraHeaders != null)
                foreach (var kv in extraHeaders) headers[kv.Key] = kv.Value;

            var envelope = new JObject
            {
                ["packets"] = packets,
                ["signature"] = Sign(Normalizer.Normalize(packets, headers)),
                ["signatureKeyId"] = null,
            };
            var (status, body) = Post($"{_prefix}/async/normal-enqueue", envelope, headers);

            AsyncResponseModel model = null;
            if (body != null)
            {
                model = new AsyncResponseModel
                {
                    Timestamp = body["timestamp"]?.Type == JTokenType.Integer ? body.Value<long>("timestamp") : 0,
                    Result = body["result"] is JArray r
                        ? new HashSet<PacketResponse>(r.Select(x => x.ToObject<PacketResponse>()))
                        : null,
                    Errors = body["errors"] is JArray e ? e.ToObject<List<ErrorModel>>() : new List<ErrorModel>(),
                };
            }
            return new HttpResponse<AsyncResponseModel>(model, status);
        }

        // ------------------------------------------------------------ plumbing

        private Dictionary<string, string> NewHeaders(bool auth)
        {
            var h = new Dictionary<string, string>
            {
                ["requestTraceId"] = Guid.NewGuid().ToString(),
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
            };
            if (auth && !string.IsNullOrEmpty(_token)) h["Authorization"] = "Bearer " + _token;
            return h;
        }

        private (int status, JObject body) Post(string path, JObject envelope, Dictionary<string, string> headers)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, _baseUrl + path)
            {
                Content = new StringContent(envelope.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json"),
            };
            foreach (var kv in headers)
            {
                if (kv.Key == "Authorization")
                    req.Headers.Authorization = AuthenticationHeaderValue.Parse(kv.Value);
                else
                    req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
            }

            HttpResponseMessage resp;
            try { resp = Http.Send(req); }
            catch (Exception ex) { throw new TaxApiException("connection failed: " + ex.Message, 0, ex); }

            using (resp)
            {
                string text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                JObject body = null;
                try { body = string.IsNullOrWhiteSpace(text) ? null : JObject.Parse(text); }
                catch { /* non-JSON body */ }
                return ((int)resp.StatusCode, body);
            }
        }

        private string Sign(string text)
        {
            if (_signer == null || text == null) return null;
            var sig = _signer.SignData(Encoding.UTF8.GetBytes(text), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return Convert.ToBase64String(sig);
        }

        private static string Trunc(string s) => s == null ? "" : (s.Length > 300 ? s[..300] : s);
    }

    /// <summary>
    /// نرمال‌سازی پیش از امضا: فلت کردن JSON، مرتب‌سازی کلیدها، چسباندن مقادیر با «#»
    /// (همان الگویی که CL_ORIGINAL_SDK.CryptoUtils.NormalJson بازتولید کرده).
    /// سرور ساختگی امضا را راستی‌آزمایی نمی‌کند، فقط وجودش را.
    /// </summary>
    internal static class Normalizer
    {
        public static string Normalize(JToken data, Dictionary<string, string> headers)
        {
            var flat = new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (data != null && data.Type != JTokenType.Null) Flatten(data, "", flat);
            if (headers != null) foreach (var kv in headers) flat[kv.Key] = kv.Value;
            if (flat.Count == 0) return null;
            return string.Join("#", flat.Values.Select(v =>
                string.IsNullOrEmpty(v) ? "#" : v.Replace("#", "##")));
        }

        private static void Flatten(JToken t, string prefix, IDictionary<string, string> into)
        {
            switch (t.Type)
            {
                case JTokenType.Object:
                    foreach (var p in ((JObject)t).Properties())
                        Flatten(p.Value, prefix.Length == 0 ? p.Name : prefix + "." + p.Name, into);
                    break;
                case JTokenType.Array:
                    int i = 0;
                    foreach (var c in t.Children())
                        Flatten(c, prefix.Length == 0 ? (i++).ToString() : prefix + "." + (i++), into);
                    break;
                case JTokenType.Null:
                    into[prefix] = null;
                    break;
                case JTokenType.Boolean:
                    into[prefix] = t.Value<bool>() ? "true" : "false";
                    break;
                default:
                    into[prefix] = ((JValue)t).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    break;
            }
        }
    }
}
