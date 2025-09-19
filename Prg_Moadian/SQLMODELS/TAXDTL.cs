using System.ComponentModel;

namespace Prg_Moadian.SQLMODELS
{
    public class TAXDTL : INotifyPropertyChanged, IEditableObject, ICloneable
    {
        // ========== INotifyPropertyChanged ==========
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));


        // ========== Properties ==========
        private string? _Taxid;
        public string? Taxid { get => _Taxid; set { if (_Taxid != value) { _Taxid = value; OnPropertyChanged(nameof(Taxid)); } } }

        private string? _REMARKS;
        public string? REMARKS { get => _REMARKS; set { if (_REMARKS != value) { _REMARKS = value; OnPropertyChanged(nameof(REMARKS)); } } }

        private Int64? _Indatim;
        public Int64? Indatim { get => _Indatim; set { if (_Indatim != value) { _Indatim = value; OnPropertyChanged(nameof(Indatim)); } } }

        private Int64? _Indati2m;
        public Int64? Indati2m { get => _Indati2m; set { if (_Indati2m != value) { _Indati2m = value; OnPropertyChanged(nameof(Indati2m)); } } }

        private int? _Inty;
        public int? Inty { get => _Inty; set { if (_Inty != value) { _Inty = value; OnPropertyChanged(nameof(Inty)); } } }

        private string? _Inno;
        public string? Inno { get => _Inno; set { if (_Inno != value) { _Inno = value; OnPropertyChanged(nameof(Inno)); } } }

        private string? _Irtaxid;
        public string? Irtaxid { get => _Irtaxid; set { if (_Irtaxid != value) { _Irtaxid = value; OnPropertyChanged(nameof(Irtaxid)); } } }

        private int? _Inp;
        public int? Inp { get => _Inp; set { if (_Inp != value) { _Inp = value; OnPropertyChanged(nameof(Inp)); } } }

        private int? _Ins;
        public int? Ins { get => _Ins; set { if (_Ins != value) { _Ins = value; OnPropertyChanged(nameof(Ins)); } } }

        private string? _Tins;
        public string? Tins { get => _Tins; set { if (_Tins != value) { _Tins = value; OnPropertyChanged(nameof(Tins)); } } }

        private int? _Tob;
        public int? Tob { get => _Tob; set { if (_Tob != value) { _Tob = value; OnPropertyChanged(nameof(Tob)); } } }

        private string? _Bid;
        public string? Bid { get => _Bid; set { if (_Bid != value) { _Bid = value; OnPropertyChanged(nameof(Bid)); } } }

        private string? _Tinb;
        public string? Tinb { get => _Tinb; set { if (_Tinb != value) { _Tinb = value; OnPropertyChanged(nameof(Tinb)); } } }

        private string? _Sbc;
        public string? Sbc { get => _Sbc; set { if (_Sbc != value) { _Sbc = value; OnPropertyChanged(nameof(Sbc)); } } }

        private string? _Bbc;
        public string? Bbc { get => _Bbc; set { if (_Bbc != value) { _Bbc = value; OnPropertyChanged(nameof(Bbc)); } } }

        private string? _Bpc;
        public string? Bpc { get => _Bpc; set { if (_Bpc != value) { _Bpc = value; OnPropertyChanged(nameof(Bpc)); } } }

        private int? _Ft;
        public int? Ft { get => _Ft; set { if (_Ft != value) { _Ft = value; OnPropertyChanged(nameof(Ft)); } } }

        private string? _Bpn;
        public string? Bpn { get => _Bpn; set { if (_Bpn != value) { _Bpn = value; OnPropertyChanged(nameof(Bpn)); } } }

        private string? _Scln;
        public string? Scln { get => _Scln; set { if (_Scln != value) { _Scln = value; OnPropertyChanged(nameof(Scln)); } } }

        private string? _Scc;
        public string? Scc { get => _Scc; set { if (_Scc != value) { _Scc = value; OnPropertyChanged(nameof(Scc)); } } }

        private string? _Crn;
        public string? Crn { get => _Crn; set { if (_Crn != value) { _Crn = value; OnPropertyChanged(nameof(Crn)); } } }

        private string? _Billid;
        public string? Billid { get => _Billid; set { if (_Billid != value) { _Billid = value; OnPropertyChanged(nameof(Billid)); } } }

        private decimal? _Tprdis;
        public decimal? Tprdis { get => _Tprdis; set { if (_Tprdis != value) { _Tprdis = value; OnPropertyChanged(nameof(Tprdis)); } } }

        private decimal? _Tdis;
        public decimal? Tdis { get => _Tdis; set { if (_Tdis != value) { _Tdis = value; OnPropertyChanged(nameof(Tdis)); } } }

        private decimal? _Tadis;
        public decimal? Tadis { get => _Tadis; set { if (_Tadis != value) { _Tadis = value; OnPropertyChanged(nameof(Tadis)); } } }

        private decimal? _Tvam;
        public decimal? Tvam { get => _Tvam; set { if (_Tvam != value) { _Tvam = value; OnPropertyChanged(nameof(Tvam)); } } }

        private decimal? _Todam;
        public decimal? Todam { get => _Todam; set { if (_Todam != value) { _Todam = value; OnPropertyChanged(nameof(Todam)); } } }

        private decimal? _Tbill;
        public decimal? Tbill { get => _Tbill; set { if (_Tbill != value) { _Tbill = value; OnPropertyChanged(nameof(Tbill)); } } }

        private decimal? _Setm;
        public decimal? Setm { get => _Setm; set { if (_Setm != value) { _Setm = value; OnPropertyChanged(nameof(Setm)); } } }

        private decimal? _Cap;
        public decimal? Cap { get => _Cap; set { if (_Cap != value) { _Cap = value; OnPropertyChanged(nameof(Cap)); } } }

        private decimal? _Insp;
        public decimal? Insp { get => _Insp; set { if (_Insp != value) { _Insp = value; OnPropertyChanged(nameof(Insp)); } } }

        private decimal? _Tvop;
        public decimal? Tvop { get => _Tvop; set { if (_Tvop != value) { _Tvop = value; OnPropertyChanged(nameof(Tvop)); } } }

        private decimal? _Tax17;
        public decimal? Tax17 { get => _Tax17; set { if (_Tax17 != value) { _Tax17 = value; OnPropertyChanged(nameof(Tax17)); } } }

        private string? _Sstid;
        public string? Sstid { get => _Sstid; set { if (_Sstid != value) { _Sstid = value; OnPropertyChanged(nameof(Sstid)); } } }

        private string? _Sstt;
        public string? Sstt { get => _Sstt; set { if (_Sstt != value) { _Sstt = value; OnPropertyChanged(nameof(Sstt)); } } }

        private string? _Mu;
        public string? Mu { get => _Mu; set { if (_Mu != value) { _Mu = value; OnPropertyChanged(nameof(Mu)); } } }

        private decimal? _Am;
        public decimal? Am { get => _Am; set { if (_Am != value) { _Am = value; OnPropertyChanged(nameof(Am)); } } }

        private decimal? _Fee;
        public decimal? Fee { get => _Fee; set { if (_Fee != value) { _Fee = value; OnPropertyChanged(nameof(Fee)); } } }

        private decimal? _Cfee;
        public decimal? Cfee { get => _Cfee; set { if (_Cfee != value) { _Cfee = value; OnPropertyChanged(nameof(Cfee)); } } }

        private string? _Cut;
        public string? Cut { get => _Cut; set { if (_Cut != value) { _Cut = value; OnPropertyChanged(nameof(Cut)); } } }

        private decimal? _Exr;
        public decimal? Exr { get => _Exr; set { if (_Exr != value) { _Exr = value; OnPropertyChanged(nameof(Exr)); } } }

        private decimal? _Prdis;
        public decimal? Prdis { get => _Prdis; set { if (_Prdis != value) { _Prdis = value; OnPropertyChanged(nameof(Prdis)); } } }

        private decimal? _Dis;
        public decimal? Dis { get => _Dis; set { if (_Dis != value) { _Dis = value; OnPropertyChanged(nameof(Dis)); } } }

        private decimal? _Adis;
        public decimal? Adis { get => _Adis; set { if (_Adis != value) { _Adis = value; OnPropertyChanged(nameof(Adis)); } } }

        private decimal? _Vra;
        public decimal? Vra { get => _Vra; set { if (_Vra != value) { _Vra = value; OnPropertyChanged(nameof(Vra)); } } }

        private decimal? _Vam;
        public decimal? Vam { get => _Vam; set { if (_Vam != value) { _Vam = value; OnPropertyChanged(nameof(Vam)); } } }

        private string? _Odt;
        public string? Odt { get => _Odt; set { if (_Odt != value) { _Odt = value; OnPropertyChanged(nameof(Odt)); } } }

        private decimal? _Odr;
        public decimal? Odr { get => _Odr; set { if (_Odr != value) { _Odr = value; OnPropertyChanged(nameof(Odr)); } } }

        private decimal? _Odam;
        public decimal? Odam { get => _Odam; set { if (_Odam != value) { _Odam = value; OnPropertyChanged(nameof(Odam)); } } }

        private string? _Olt;
        public string? Olt { get => _Olt; set { if (_Olt != value) { _Olt = value; OnPropertyChanged(nameof(Olt)); } } }

        private decimal? _Olr;
        public decimal? Olr { get => _Olr; set { if (_Olr != value) { _Olr = value; OnPropertyChanged(nameof(Olr)); } } }

        private decimal? _Olam;
        public decimal? Olam { get => _Olam; set { if (_Olam != value) { _Olam = value; OnPropertyChanged(nameof(Olam)); } } }

        private decimal? _Consfee;
        public decimal? Consfee { get => _Consfee; set { if (_Consfee != value) { _Consfee = value; OnPropertyChanged(nameof(Consfee)); } } }

        private decimal? _Spro;
        public decimal? Spro { get => _Spro; set { if (_Spro != value) { _Spro = value; OnPropertyChanged(nameof(Spro)); } } }

        private decimal? _Bros;
        public decimal? Bros { get => _Bros; set { if (_Bros != value) { _Bros = value; OnPropertyChanged(nameof(Bros)); } } }

        private decimal? _Tcpbs;
        public decimal? Tcpbs { get => _Tcpbs; set { if (_Tcpbs != value) { _Tcpbs = value; OnPropertyChanged(nameof(Tcpbs)); } } }

        private decimal? _Cop;
        public decimal? Cop { get => _Cop; set { if (_Cop != value) { _Cop = value; OnPropertyChanged(nameof(Cop)); } } }

        private decimal? _Vop;
        public decimal? Vop { get => _Vop; set { if (_Vop != value) { _Vop = value; OnPropertyChanged(nameof(Vop)); } } }

        private string? _Bsrn;
        public string? Bsrn { get => _Bsrn; set { if (_Bsrn != value) { _Bsrn = value; OnPropertyChanged(nameof(Bsrn)); } } }

        private decimal? _Tsstam;
        public decimal? Tsstam { get => _Tsstam; set { if (_Tsstam != value) { _Tsstam = value; OnPropertyChanged(nameof(Tsstam)); } } }

        private string? _Iinn;
        public string? Iinn { get => _Iinn; set { if (_Iinn != value) { _Iinn = value; OnPropertyChanged(nameof(Iinn)); } } }

        private string? _Acn;
        public string? Acn { get => _Acn; set { if (_Acn != value) { _Acn = value; OnPropertyChanged(nameof(Acn)); } } }

        private string? _Trmn;
        public string? Trmn { get => _Trmn; set { if (_Trmn != value) { _Trmn = value; OnPropertyChanged(nameof(Trmn)); } } }

        private string? _Trn;
        public string? Trn { get => _Trn; set { if (_Trn != value) { _Trn = value; OnPropertyChanged(nameof(Trn)); } } }

        private string? _Pcn;
        public string? Pcn { get => _Pcn; set { if (_Pcn != value) { _Pcn = value; OnPropertyChanged(nameof(Pcn)); } } }

        private string? _Pid;
        public string? Pid { get => _Pid; set { if (_Pid != value) { _Pid = value; OnPropertyChanged(nameof(Pid)); } } }

        private decimal? _Pdt;
        public decimal? Pdt { get => _Pdt; set { if (_Pdt != value) { _Pdt = value; OnPropertyChanged(nameof(Pdt)); } } }

        private string? _Cdcn;
        public string? Cdcn { get => _Cdcn; set { if (_Cdcn != value) { _Cdcn = value; OnPropertyChanged(nameof(Cdcn)); } } }

        private int? _Cdcd;
        public int? Cdcd { get => _Cdcd; set { if (_Cdcd != value) { _Cdcd = value; OnPropertyChanged(nameof(Cdcd)); } } }

        private decimal? _Tonw;
        public decimal? Tonw { get => _Tonw; set { if (_Tonw != value) { _Tonw = value; OnPropertyChanged(nameof(Tonw)); } } }

        private decimal? _Torv;
        public decimal? Torv { get => _Torv; set { if (_Torv != value) { _Torv = value; OnPropertyChanged(nameof(Torv)); } } }

        private decimal? _Tocv;
        public decimal? Tocv { get => _Tocv; set { if (_Tocv != value) { _Tocv = value; OnPropertyChanged(nameof(Tocv)); } } }

        private decimal? _Nw;
        public decimal? Nw { get => _Nw; set { if (_Nw != value) { _Nw = value; OnPropertyChanged(nameof(Nw)); } } }

        private decimal? _Ssrv;
        public decimal? Ssrv { get => _Ssrv; set { if (_Ssrv != value) { _Ssrv = value; OnPropertyChanged(nameof(Ssrv)); } } }

        private decimal? _Sscv;
        public decimal? Sscv { get => _Sscv; set { if (_Sscv != value) { _Sscv = value; OnPropertyChanged(nameof(Sscv)); } } }

        private int? _Pmt;
        public int? Pmt { get => _Pmt; set { if (_Pmt != value) { _Pmt = value; OnPropertyChanged(nameof(Pmt)); } } }

        private decimal? _PV;
        public decimal? PV { get => _PV; set { if (_PV != value) { _PV = value; OnPropertyChanged(nameof(PV)); } } }

        private int? _IDD;
        public int? IDD { get => _IDD; set { if (_IDD != value) { _IDD = value; OnPropertyChanged(nameof(IDD)); } } }

        private DateTime? _CRT;
        public DateTime? CRT { get => _CRT; set { if (_CRT != value) { _CRT = value; OnPropertyChanged(nameof(CRT)); } } }

        private string? _UID;
        public string? UID { get => _UID; set { if (_UID != value) { _UID = value; OnPropertyChanged(nameof(UID)); } } }

        private string? _RefrenceNumber;
        public string? RefrenceNumber { get => _RefrenceNumber; set { if (_RefrenceNumber != value) { _RefrenceNumber = value; OnPropertyChanged(nameof(RefrenceNumber)); } } }

        private string? _TheConfirmationReferenceId;
        public string? TheConfirmationReferenceId { get => _TheConfirmationReferenceId; set { if (_TheConfirmationReferenceId != value) { _TheConfirmationReferenceId = value; OnPropertyChanged(nameof(TheConfirmationReferenceId)); } } }

        private string? _TheError;
        public string? TheError { get => _TheError; set { if (_TheError != value) { _TheError = value; OnPropertyChanged(nameof(TheError)); } } }

        private string? _TheStatus;
        public string? TheStatus { get => _TheStatus; set { if (_TheStatus != value) { _TheStatus = value; OnPropertyChanged(nameof(TheStatus)); } } }

        private bool? _TheSuccess;
        public bool? TheSuccess { get => _TheSuccess; set { if (_TheSuccess != value) { _TheSuccess = value; OnPropertyChanged(nameof(TheSuccess)); } } }

        private string? _TheWarning;
        public string? TheWarning { get => _TheWarning; set { if (_TheWarning != value) { _TheWarning = value; OnPropertyChanged(nameof(TheWarning)); } } }

        private Int64? _Indatim_Sec;
        public Int64? Indatim_Sec { get => _Indatim_Sec; set { if (_Indatim_Sec != value) { _Indatim_Sec = value; OnPropertyChanged(nameof(Indatim_Sec)); } } }

        private Int64? _Indati2m_Sec;
        public Int64? Indati2m_Sec { get => _Indati2m_Sec; set { if (_Indati2m_Sec != value) { _Indati2m_Sec = value; OnPropertyChanged(nameof(Indati2m_Sec)); } } }

        private double? _NUMBER;
        public double? NUMBER { get => _NUMBER; set { if (_NUMBER != value) { _NUMBER = value; OnPropertyChanged(nameof(NUMBER)); } } }

        private double? _TAG;
        public double? TAG { get => _TAG; set { if (_TAG != value) { _TAG = value; OnPropertyChanged(nameof(TAG)); } } }

        private int? _DATE_N;
        public int? DATE_N { get => _DATE_N; set { if (_DATE_N != value) { _DATE_N = value; OnPropertyChanged(nameof(DATE_N)); } } }

        private long? _ROWNUMBER;
        public long? ROWNUMBER
        {
            get => _ROWNUMBER;
            set
            {
                if (_ROWNUMBER != value)
                {
                    _ROWNUMBER = value;
                    OnPropertyChanged(nameof(ROWNUMBER));
                }
            }
        }



        private string? _PersianCRT;
        public string? PersianCRT
        {
            get
            { 
                return _PersianCRT;
            }
            set
            {
                if (_PersianCRT != value)
                {
                    _PersianCRT = value;
                    OnPropertyChanged(nameof(PersianCRT));
                }
            }
        }

        private string? _NAME_VAHED;
        public string? NAME_VAHED { get => _NAME_VAHED; set { if (_NAME_VAHED != value) { _NAME_VAHED = value; OnPropertyChanged(nameof(NAME_VAHED)); } } }

        private string? _SentTaxMemory;
        public string? SentTaxMemory { get => _SentTaxMemory; set { if (_SentTaxMemory != value) { _SentTaxMemory = value; OnPropertyChanged(nameof(SentTaxMemory)); } } }

        private bool? _ApiTypeSent;
        public bool? ApiTypeSent { get => _ApiTypeSent; set { if (_ApiTypeSent != value) { _ApiTypeSent = value; OnPropertyChanged(nameof(ApiTypeSent)); } } }


        private TAXDTL? _backupCopy;
        private bool _inEdit;

        // ========== IEditableObject ==========
        public void BeginEdit()
        {
            if (!_inEdit)
            {
                _backupCopy = (TAXDTL)Clone();
                _inEdit = true;
            }
        }

        public void CancelEdit()
        {
            if (_inEdit && _backupCopy != null)
            {
                CopyFrom(_backupCopy);
                _inEdit = false;
            }
        }

        public void EndEdit()
        {
            if (_inEdit)
            {
                _backupCopy = null;
                _inEdit = false;
            }
        }

        // ========== ICloneable ==========
        public object Clone() => MemberwiseClone();

        private void CopyFrom(TAXDTL other)
        {
            foreach (var prop in typeof(TAXDTL).GetProperties())
            {
                if (prop.CanWrite)
                {
                    prop.SetValue(this, prop.GetValue(other));
                }
            }
        }

    }
}
