using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Prg_Moadian.CNNMANAGER
{
    public class TaxModel
    {
        public class InvoiceModel
        {
            public class Header
            {
                public string? Taxid { get; set; }

                public DateTime ExportOn { get; set; }

                public DateTime CreateOn { get; set; }

                public long Indatim { get; set; }

                public long Indati2m { get; set; }

                public int Inty { get; set; }

                public string? Inno { get; set; }

                public string? Irtaxid { get; set; }

                public int Inp { get; set; }

                public int Ins { get; set; }

                public string? Tins { get; set; }

                public int Tob { get; set; }

                public string? Bid { get; set; }

                public string? Tinb { get; set; }

                public string? Sbc { get; set; }

                public string? Bpc { get; set; }

                public string? Bbc { get; set; }

                public int Ft { get; set; }

                public string? Bpn { get; set; }

                public string? Scln { get; set; }

                public string? Scc { get; set; }

                public string? Crn { get; set; }

                public string? Billid { get; set; }

                public decimal Tprdis { get; set; }

                public decimal Tdis { get; set; }

                public decimal Tadis { get; set; }

                public decimal Tvam { get; set; }

                public decimal Todam { get; set; }

                public decimal Tbill { get; set; }

                public int Setm { get; set; }

                public decimal Cap { get; set; }

                public decimal Insp { get; set; }

                public decimal Tvop { get; set; }

                public decimal Tax17 { get; set; }

                public string? Cdcn { get; set; }

                public int Cdcd { get; set; }

                public decimal Tonw { get; set; }

                public decimal Torv { get; set; }

                public decimal Tocv { get; set; }
            }

            public class Body
            {
                public string TaxId { get; set; }

                public string? Sstid { get; set; }

                public string? Sstt { get; set; }

                public string? Mu { get; set; }

                public decimal Am { get; set; }

                public decimal Fee { get; set; }

                public decimal Cfee { get; set; }

                public string? Cut { get; set; }

                public decimal Exr { get; set; }

                public decimal Prdis { get; set; }

                public decimal Dis { get; set; }

                public decimal Adis { get; set; }

                public decimal Vra { get; set; }

                public decimal Vam { get; set; }

                public string? Odt { get; set; }

                public decimal Odr { get; set; }

                public decimal Odam { get; set; }

                public string? Olt { get; set; }

                public decimal Olr { get; set; }

                public decimal Olam { get; set; }

                public decimal Consfee { get; set; }

                public decimal Spro { get; set; }

                public decimal Bros { get; set; }

                public decimal Tcpbs { get; set; }

                public decimal Cop { get; set; }

                public decimal Vop { get; set; }

                public string? Bsrn { get; set; }

                public decimal Tsstam { get; set; }

                public decimal Nw { get; set; }

                public decimal Ssrv { get; set; }

                public decimal Sscv { get; set; }
            }

            public class Payment
            {
                public string TaxId { get; set; }

                public string? Iinn { get; set; }

                public string? Acn { get; set; }

                public string? Trmn { get; set; }

                public string? Trn { get; set; }

                public string? Pcn { get; set; }

                public string? Pid { get; set; }

                public long Pdt { get; set; }

                public int Pmt { get; set; }

                public long Pv { get; set; }
            }
        }

        public class RequestTokenModel
        {
            public string Token { get; set; }

            public long ExpireIn { get; set; }
        }

        public class SendInvoicesModel
        {
            public string ReferenceNumber { get; set; }

            public string Uid { get; set; }

            public string TaxId { get; set; }
        }

        public class InquiryByReferenceIdModel
        {
            public class Error
            {
                public string code { get; set; }

                public string message { get; set; }

                public string errorType { get; set; }
            }

            public class Root
            {
                public object confirmationReferenceId { get; set; }

                public List<Error> error { get; set; }

                public List<Warning> warning { get; set; }

                public bool success { get; set; }

                public string status { get; set; }
            }

            public class Warning
            {
                public string code { get; set; }

                public string message { get; set; }

                public string errorType { get; set; }
            }
        }
    }
}
