using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaxCollectData.Library.Business;
using TaxCollectData.Library.Dto.Config;
using TaxCollectData.Library.Dto.Content;
using TaxCollectData.Library.Dto.Properties;
using TaxCollectData.Library.Dto.Transfer;
using TaxCollectData.Library.Dto;
using TaxCollectData.Library.Enums;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.Generaly;
using static Prg_Moadian.Generaly.CL_Generaly;
using Newtonsoft.Json.Linq;
using Prg_Moadian.FUNCTIONS;

namespace Prg_Moadian.Service
{
    public class TaxService
    {
        public TaxService(string MemoryId, string PrivateKey, string TaxUrl)
        {
            TaxApiService.Instance.Init(MemoryId, new SignatoryConfig(PrivateKey, null), new NormalProperties(ClientType.SELF_TSP), TaxUrl);
            ServerInformationModel serverInformation = TaxApiService.Instance.TaxApis.GetServerInformation();

            TokenLifeTime.ServerUtcTime = DateTimeOffset.FromUnixTimeMilliseconds(serverInformation.ServerTime).UtcDateTime;
            TokenLifeTime.ServerClockSkew = TokenLifeTime.ServerUtcTime - DateTime.UtcNow;
        }

        public TaxModel.RequestTokenModel RequestToken()
        {
            TokenModel tokenModel = TaxApiService.Instance.TaxApis.RequestToken();
            var TMRT = new TaxModel.RequestTokenModel();
            if (tokenModel?.ExpiresIn != null)
            {
                TMRT.ExpireIn = tokenModel.ExpiresIn;
            }
            TMRT.Token = tokenModel.Token;
            return TMRT;
        }

        public string RequestTaxId(string memoryId, DateTime date)
        {
            Random random = new Random();
            long serial = random.Next(999999999);
            return TaxApiService.Instance.TaxIdGenerator.GenerateTaxId(memoryId, serial, date);
        }

        public static long ConvertDateToLong(DateTime dateTime)
        {
            ////شاید بعدا , صرفا اینجا باشه
            ////// حذف ثانیه و میلی‌ثانیه 
            ////dateTime = dateTime.AddTicks(-(dateTime.Ticks % TimeSpan.TicksPerMinute));
            ////return new DateTimeOffset(dateTime).ToUnixTimeMilliseconds();

            return new DateTimeOffset(dateTime).ToUnixTimeMilliseconds();
        }

        public TaxModel.SendInvoicesModel SendInvoices(TaxModel.InvoiceModel.Header header, List<TaxModel.InvoiceModel.Body> body, List<TaxModel.InvoiceModel.Payment> payment)
        {
            List<InvoiceDto> list = new List<InvoiceDto>();
            List<InvoiceBodyDto> list2 = new List<InvoiceBodyDto>();
            List<PaymentDto> list3 = new List<PaymentDto>();
            List<InvoiceExtension> list4 = new List<InvoiceExtension>();
            InvoiceHeaderDto header2 = new InvoiceHeaderDto
            {
                Bbc = ((header.Bbc == string.Empty) ? null : header.Bbc),
                Bid = ((header.Bid == string.Empty) ? null : header.Bid),
                Billid = ((header.Billid == string.Empty) ? null : header.Billid),
                Bpc = ((header.Bpc == string.Empty) ? null : header.Bpc),
                Bpn = ((header.Bpn == string.Empty) ? null : header.Bpn),
                Cap = header.Cap,
                Cdcd = ((header.Cdcd == 0) ? null : new int?(header.Cdcd)),
                Cdcn = ((header.Cdcn == string.Empty) ? null : header.Cdcn),
                Crn = ((header.Crn == string.Empty) ? null : header.Crn),
                Ft = ((header.Ft == 0) ? null : new int?(header.Ft)),
                Indati2m = header.Indati2m,
                Indatim = header.Indatim,
                Inno = ((header.Inno == string.Empty) ? null : header.Inno),
                Inp = header.Inp,
                Ins = header.Ins,
                Insp = header.Insp,
                Inty = header.Inty,
                Irtaxid = ((header.Irtaxid == string.Empty) ? null : header.Irtaxid),
                Sbc = ((header.Sbc == string.Empty) ? null : header.Sbc),
                Scc = ((header.Scc == string.Empty) ? null : header.Scc),
                Scln = ((header.Scln == string.Empty) ? null : header.Scln),
                Setm = header.Setm,
                Tadis = header.Tadis,
                Tax17 = header.Tax17,
                Taxid = ((header.Taxid == string.Empty) ? null : header.Taxid),
                Tbill = header.Tbill,
                Tdis = header.Tdis,
                Tinb = ((header.Tinb == string.Empty) ? null : header.Tinb),
                Tins = ((header.Tins == string.Empty) ? null : header.Tins),
                Tob = header.Tob,
                Tocv = ((header.Tocv == 0m) ? null : new decimal?(header.Tocv)),
                Todam = header.Todam,
                Tonw = ((header.Tonw == 0m) ? null : new decimal?(header.Tonw)),
                Torv = ((header.Torv == 0m) ? null : new decimal?(header.Torv)),
                Tprdis = header.Tprdis,
                Tvam = header.Tvam,
                Tvop = header.Tvop
            };
            foreach (TaxModel.InvoiceModel.Body item in body)
            {
                list2.Add(new InvoiceBodyDto
                {
                    Adis = item.Adis,
                    Am = item.Am,
                    Bros = item.Bros,
                    Bsrn = ((item.Bsrn == string.Empty) ? null : item.Bsrn),
                    Cfee = ((item.Cfee == 0m) ? null : new decimal?(item.Cfee)),
                    Consfee = item.Consfee,
                    Cop = item.Cop,
                    Cut = ((item.Cut == string.Empty) ? null : item.Cut),
                    Dis = item.Dis,
                    Exr = ((item.Exr == 0m) ? null : new decimal?(item.Exr)),
                    Fee = item.Fee,
                    Mu = ((item.Mu == string.Empty) ? null : item.Mu),
                    Nw = ((item.Nw == 0m) ? null : new decimal?(item.Nw)),
                    Odam = item.Odam,
                    Odr = item.Odr,
                    Odt = ((item.Odt == string.Empty) ? null : item.Odt),
                    Olam = item.Olam,
                    Olr = item.Olr,
                    Olt = ((item.Olt == string.Empty) ? null : item.Olt),
                    Prdis = item.Prdis,
                    Spro = item.Spro,
                    Sscv = ((item.Sscv == 0m) ? null : new decimal?(item.Sscv)),
                    Ssrv = ((item.Ssrv == 0m) ? null : new decimal?(item.Ssrv)),
                    Sstid = ((item.Sstid == string.Empty) ? null : item.Sstid),
                    Sstt = ((item.Sstt == string.Empty) ? null : item.Sstt),
                    Tcpbs = item.Tcpbs,
                    Tsstam = item.Tsstam,
                    Vam = item.Vam,
                    Vop = item.Vop,
                    Vra = item.Vra
                });
            }

            foreach (TaxModel.InvoiceModel.Payment item2 in payment)
            {
                list3.Add(new PaymentDto
                {
                    Acn = item2.Acn,
                    Iinn = item2.Iinn,
                    Pcn = item2.Pcn,
                    Pdt = item2.Pdt,
                    Pid = item2.Pid,
                    Pmt = item2.Pmt,
                    Pv = item2.Pv,
                    Trmn = item2.Trmn,
                    Trn = item2.Trn
                });
            }

            list4.Add(new InvoiceExtension());
            list.Add(new InvoiceDto
            {
                Header = header2,
                Body = list2,
                Payments = list3,
                Extension = list4
            });

            TaxModel.SendInvoicesModel sendInvoicesModel = new TaxModel.SendInvoicesModel();
            HttpResponse<AsyncResponseModel> httpResponse = TaxApiService.Instance.TaxApis.SendInvoices(list, null);
            const string serverSideErrorMessage = "خطا در ارتباط با سامانه مودیان رخ داد و مشکل از سمت سرورهای سامانه است. لطفاً بعداً دوباره تلاش کنید.";
            HashSet<PacketResponse>? packetResponses = httpResponse.Body?.Result;
            if (packetResponses == null || !packetResponses.Any())
            {
                List<ErrorModel>? responseErrors = httpResponse.Body?.Errors;
                List<string>? errorMessages = responseErrors?
                    .Where(error => error != null && (!string.IsNullOrWhiteSpace(error.ErrorCode) || !string.IsNullOrWhiteSpace(error.Detail)))
                    .Select(error => $"{error?.ErrorCode}: {error?.Detail}")
                    .Where(message => !string.IsNullOrWhiteSpace(message))
                    .ToList();

                string detailMessage = (errorMessages != null && errorMessages.Count > 0)
                    ? string.Join(" | ", errorMessages)
                    : $"Status Code : {httpResponse.Status}";

                throw new InvalidOperationException($"{serverSideErrorMessage} جزئیات: {detailMessage}");
            }

            PacketResponse? packetResponse = packetResponses.FirstOrDefault();

            if (packetResponse == null)
            {
                throw new InvalidOperationException($"{serverSideErrorMessage} جزئیات: پاسخ دریافتی از سرور نامعتبر بود.");
            }

            sendInvoicesModel.ReferenceNumber = packetResponse.ReferenceNumber;
            sendInvoicesModel.Uid = packetResponse.Uid;
            sendInvoicesModel.TaxId = header.Taxid;
            return sendInvoicesModel;

        }

        public TaxModel.InquiryByReferenceIdModel.Root InquiryByReferenceId(string referenceCode)
        {
            List<string> list = new List<string>();
            list.Add(referenceCode);
            List<InquiryResultModel> list2 = TaxApiService.Instance.TaxApis.InquiryByReferenceId(list);
            TaxModel.InquiryByReferenceIdModel inquiryByReferenceIdModel = new TaxModel.InquiryByReferenceIdModel();
            if (list2 == null || !list2.Any())
            {
                return null;
            }
            string value = list2.Select((InquiryResultModel x) => x.Data).FirstOrDefault()!.ToString();
            TaxModel.InquiryByReferenceIdModel.Root root = JsonConvert.DeserializeObject<TaxModel.InquiryByReferenceIdModel.Root>(value);
            root.status = list2[0].Status;
            return root;
        }

        public string RequestTaxIdWithSpecificSerial(string memoryId, DateTime date, long serial)
        {
            // اینجا دیگه رندوم نیست، دقیقاً سریالی که می‌خواهیم را می‌فرستیم
            return TaxApiService.Instance.TaxIdGenerator.GenerateTaxId(memoryId, serial, date);
        }

    }
}
