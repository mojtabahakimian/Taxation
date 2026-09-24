// ============================================================================
//  شیم TaxCollectData.Library 0.0.34 — فقط برای اجرای هارنس تست روی لینوکس.
//  هرگز همراه برنامه منتشر نشود. توضیح در README.md همین پوشه.
// ============================================================================
//
//  ترتیب و نام فیلدها از روی فایل‌های مرجع
//  TestTools/LocalMoadian/golden/*.json برداشته شده — آن فایل‌ها با SDK واقعی
//  روی ماشین توسعه‌دهنده ضبط شده‌اند و تنها شاهد مستقیم قالب سیم‌اند.
//  نوع‌ها (nullable بودن) از روی استفاده در Prg_Moadian و هارنس حدس زده شده‌اند؛
//  ۱۷-۲ فقط Insr=int? را صریحاً قفل کرده.

using System.Text.Json.Serialization;

namespace TaxCollectData.Library.Dto.Content
{
    public class InvoiceDto
    {
        public InvoiceHeaderDto Header { get; set; }
        public List<InvoiceBodyDto> Body { get; set; }
        public List<PaymentDto> Payments { get; set; }
        public List<InvoiceExtension> Extension { get; set; }
    }

    public class InvoiceHeaderDto
    {
        public string Taxid { get; set; }
        public long? Indatim { get; set; }
        public long? Indati2m { get; set; }
        public int? Inty { get; set; }
        public string Inno { get; set; }
        public string Irtaxid { get; set; }
        public int? Inp { get; set; }
        public int? Ins { get; set; }
        public string Tins { get; set; }
        public int? Tob { get; set; }
        public string Bid { get; set; }
        public string Tinb { get; set; }
        public string Sbc { get; set; }
        public string Bpc { get; set; }
        public string Bbc { get; set; }
        public int? Ft { get; set; }
        public string Bpn { get; set; }
        public string Scln { get; set; }
        public string Scc { get; set; }
        public string Crn { get; set; }
        public string Billid { get; set; }
        public decimal? Tprdis { get; set; }
        public decimal? Tdis { get; set; }
        public decimal? Tadis { get; set; }
        public decimal? Tvam { get; set; }
        public decimal? Todam { get; set; }
        public decimal? Tbill { get; set; }
        public int? Setm { get; set; }
        public decimal? Cap { get; set; }
        public decimal? Insp { get; set; }
        public decimal? Tvop { get; set; }
        public decimal? Tax17 { get; set; }
        public string Cdcn { get; set; }
        public int? Cdcd { get; set; }
        public decimal? Tonw { get; set; }
        public decimal? Torv { get; set; }
        public decimal? Tocv { get; set; }
        public string Tinc { get; set; }
        public string Lno { get; set; }
        public string Lrno { get; set; }
        public string Ocu { get; set; }
        public string Oci { get; set; }
        public string Dco { get; set; }
        public string Dci { get; set; }
        public string Tid { get; set; }
        public string Rid { get; set; }
        public string Lt { get; set; }
        public string Cno { get; set; }
        public string Did { get; set; }
        public List<object> Sg { get; set; }
        public string Asn { get; set; }
        public int? Asd { get; set; }
        public string In { get; set; }
        public string An { get; set; }
        /// <summary>قاعده ارسال. در 0.0.30 رشته بود، در 0.0.34 int? (تست ۱۷-۲).</summary>
        public int? Insr { get; set; }
        public string Nti1 { get; set; }
        public string Nti2 { get; set; }
    }

    public class InvoiceBodyDto
    {
        public string Sstid { get; set; }
        public string Sstt { get; set; }
        public string Mu { get; set; }
        public decimal? Am { get; set; }
        public decimal? Fee { get; set; }
        public decimal? Cfee { get; set; }
        public string Cut { get; set; }
        public decimal? Exr { get; set; }
        public decimal? Prdis { get; set; }
        public decimal? Dis { get; set; }
        public decimal? Adis { get; set; }
        public decimal? Vra { get; set; }
        public decimal? Vam { get; set; }
        public string Odt { get; set; }
        public decimal? Odr { get; set; }
        public decimal? Odam { get; set; }
        public string Olt { get; set; }
        public decimal? Olr { get; set; }
        public decimal? Olam { get; set; }
        public decimal? Consfee { get; set; }
        public decimal? Spro { get; set; }
        public decimal? Bros { get; set; }
        public decimal? Tcpbs { get; set; }
        public decimal? Cop { get; set; }
        public decimal? Vop { get; set; }
        public string Bsrn { get; set; }
        public decimal? Tsstam { get; set; }
        public decimal? Nw { get; set; }
        public decimal? Ssrv { get; set; }
        public decimal? Sscv { get; set; }
        public decimal? Cui { get; set; }
        public decimal? Cpr { get; set; }
        public decimal? Sovat { get; set; }
        public string Hs { get; set; }
        public decimal? Vba { get; set; }
    }

    public class PaymentDto
    {
        public string Iinn { get; set; }
        public string Acn { get; set; }
        public string Trmn { get; set; }
        public int? Pmt { get; set; }
        public string Trn { get; set; }
        public string Pcn { get; set; }
        public string Pid { get; set; }
        public long? Pdt { get; set; }
        public decimal? Pv { get; set; }
    }

    /// <summary>در مرجع‌های golden همیشه به شکل <c>{}</c> روی سیم است.</summary>
    public class InvoiceExtension
    {
    }

    public class ServerInformationModel
    {
        public long ServerTime { get; set; }
        public List<PublicKeyModel> PublicKeys { get; set; }
    }

    public class PublicKeyModel
    {
        public string Key { get; set; }
        public string Id { get; set; }
        public string Algorithm { get; set; }
        public int Purpose { get; set; }
    }

    public class TokenModel
    {
        public string Token { get; set; }
        public long ExpiresIn { get; set; }
    }

    public class InquiryResultModel
    {
        public string ReferenceNumber { get; set; }
        public string Uid { get; set; }
        public string Status { get; set; }
        /// <summary>JObject (Newtonsoft)؛ کد تولید آن را با ToString() به JSON برمی‌گرداند.</summary>
        public object Data { get; set; }
        public string PacketType { get; set; }
        public string FiscalId { get; set; }
    }

    public class UidAndFiscalId
    {
        public UidAndFiscalId() { }
        public UidAndFiscalId(string uid, string fiscalId) { Uid = uid; FiscalId = fiscalId; }
        public string Uid { get; set; }
        public string FiscalId { get; set; }
    }

    public class EconomicCodeModel
    {
        public string NameTrade { get; set; }
        public string TaxpayerStatus { get; set; }
        public string TaxpayerType { get; set; }
        public string PostalcodeTaxpayer { get; set; }
        public string AddressTaxpayer { get; set; }
        public string NationalId { get; set; }
    }

    public class FiscalInformationModel
    {
        public string NameTrade { get; set; }
        public string FiscalStatus { get; set; }
        public decimal? SaleThreshold { get; set; }
        public string EconomicCode { get; set; }
    }
}

namespace TaxCollectData.Library.Dto.Transfer
{
    public class PacketResponse
    {
        public PacketResponse() { }

        public PacketResponse(string uid, string referenceNumber, string errorCode, string errorDetail)
        {
            Uid = uid;
            ReferenceNumber = referenceNumber;
            ErrorCode = errorCode;
            ErrorDetail = errorDetail;
        }

        public string Uid { get; set; }
        public string ReferenceNumber { get; set; }
        public string ErrorCode { get; set; }
        public string ErrorDetail { get; set; }
    }

    public class AsyncResponseModel
    {
        public long Timestamp { get; set; }
        public HashSet<PacketResponse> Result { get; set; }
        public List<TaxCollectData.Library.Dto.ErrorModel> Errors { get; set; }
    }
}

namespace TaxCollectData.Library.Dto
{
    public class ErrorModel
    {
        public string Detail { get; set; }
        public string ErrorCode { get; set; }
    }

    public class HttpResponse<T>
    {
        public HttpResponse() { }
        public HttpResponse(T body, int status) { Body = body; Status = status; }
        public T Body { get; set; }
        public int Status { get; set; }
    }
}

namespace TaxCollectData.Library.Dto.Config
{
    public class SignatoryConfig
    {
        public SignatoryConfig(string privateKey, string certificate)
        {
            PrivateKey = privateKey;
            Certificate = certificate;
        }
        public string PrivateKey { get; }
        public string Certificate { get; }
    }

    public class EncryptionConfig
    {
        public EncryptionConfig(string publicKey, string keyId) { PublicKey = publicKey; KeyId = keyId; }
        public string PublicKey { get; }
        public string KeyId { get; }
    }
}

namespace TaxCollectData.Library.Dto.Properties
{
    using TaxCollectData.Library.Enums;

    public class NormalProperties
    {
        public NormalProperties(ClientType clientType, string apiVersion = null)
        {
            ClientType = clientType;
            ApiVersion = apiVersion;
        }
        public ClientType ClientType { get; }
        public string ApiVersion { get; }
    }
}

namespace TaxCollectData.Library.Enums
{
    public enum ClientType
    {
        SELF_TSP,
        TSP,
    }
}
