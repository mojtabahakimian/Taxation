using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Prg_Moadian.SQLMODELS
{
    public class FULL_TAXDTL : INotifyPropertyChanged, ICloneable
    {
        public object Clone()
        {
            return this.MemberwiseClone();
        }

        private string? _taxid;
        public string? Taxid { get => _taxid; set { if (_taxid == value) return; _taxid = value; OnPropertyChanged("Taxid"); } }

        private long? _indatim;
        public long? Indatim { get => _indatim; set { if (_indatim == value) return; _indatim = value; OnPropertyChanged("Indatim"); } }

        private long? _indati2m;
        public long? Indati2m { get => _indati2m; set { if (_indati2m == value) return; _indati2m = value; OnPropertyChanged("Indati2m"); } }


        private long? _Indatim_Sec;
        public long? Indatim_Sec { get => _Indatim_Sec; set { if (_Indatim_Sec == value) return; _Indatim_Sec = value; OnPropertyChanged("Indatim_Sec"); } }

        private long? _Indati2m_Sec;
        public long? Indati2m_Sec { get => _Indati2m_Sec; set { if (_Indati2m_Sec == value) return; _Indati2m_Sec = value; OnPropertyChanged("Indati2m_Sec"); } }

        
        private int? _inty;
        public int? Inty { get => _inty; set { if (_inty == value) return; _inty = value; OnPropertyChanged("Inty"); } }

        private string? _inno;
        public string? Inno { get => _inno; set { if (_inno == value) return; _inno = value; OnPropertyChanged("Inno"); } }

        private string? _irtaxid;
        public string? Irtaxid { get => _irtaxid; set { if (_irtaxid == value) return; _irtaxid = value; OnPropertyChanged("Irtaxid"); } }

        private int? _inp;
        public int? Inp { get => _inp; set { if (_inp == value) return; _inp = value; OnPropertyChanged("Inp"); } }

        private int? _ins;
        public int? Ins { get => _ins; set { if (_ins == value) return; _ins = value; OnPropertyChanged("Ins"); } }

        private string? _tins;
        public string? Tins { get => _tins; set { if (_tins == value) return; _tins = value; OnPropertyChanged("Tins"); } }

        private string? _bbc;
        public string? Bbc { get => _bbc; set { if (_bbc == value) return; _bbc = value; OnPropertyChanged("Bbc"); } }

        private int? _tob;
        public int? Tob { get => _tob; set { if (_tob == value) return; _tob = value; OnPropertyChanged("Tob"); } }

        private string? _bid;
        public string? Bid { get => _bid; set { if (_bid == value) return; _bid = value; OnPropertyChanged("Bid"); } }

        private string? _tinb;
        public string? Tinb { get => _tinb; set { if (_tinb == value) return; _tinb = value; OnPropertyChanged("Tinb"); } }

        private string? _sbc;
        public string? Sbc { get => _sbc; set { if (_sbc == value) return; _sbc = value; OnPropertyChanged("Sbc"); } }

        private string? _bpc;
        public string? Bpc { get => _bpc; set { if (_bpc == value) return; _bpc = value; OnPropertyChanged("Bpc"); } }

        private int? _ft;
        public int? Ft { get => _ft; set { if (_ft == value) return; _ft = value; OnPropertyChanged("Ft"); } }

        private string? _bpn;
        public string? Bpn { get => _bpn; set { if (_bpn == value) return; _bpn = value; OnPropertyChanged("Bpn"); } }

        private string? _scln;
        public string? Scln { get => _scln; set { if (_scln == value) return; _scln = value; OnPropertyChanged("Scln"); } }

        private string? _scc;
        public string? Scc { get => _scc; set { if (_scc == value) return; _scc = value; OnPropertyChanged("Scc"); } }

        private string? _crn;
        public string? Crn { get => _crn; set { if (_crn == value) return; _crn = value; OnPropertyChanged("Crn"); } }

        private string? _billid;
        public string? Billid { get => _billid; set { if (_billid == value) return; _billid = value; OnPropertyChanged("Billid"); } }

        private decimal? _tprdis;
        public decimal? Tprdis { get => _tprdis; set { if (_tprdis == value) return; _tprdis = value; OnPropertyChanged("Tprdis"); } }

        private decimal? _tdis;
        public decimal? Tdis { get => _tdis; set { if (_tdis == value) return; _tdis = value; OnPropertyChanged("Tdis"); } }

        private decimal? _tadis;
        public decimal? Tadis { get => _tadis; set { if (_tadis == value) return; _tadis = value; OnPropertyChanged("Tadis"); } }

        private decimal? _tvam;
        public decimal? Tvam { get => _tvam; set { if (_tvam == value) return; _tvam = value; OnPropertyChanged("Tvam"); } }

        private decimal? _todam;
        public decimal? Todam { get => _todam; set { if (_todam == value) return; _todam = value; OnPropertyChanged("Todam"); } }

        private decimal? _tbill;
        public decimal? Tbill { get => _tbill; set { if (_tbill == value) return; _tbill = value; OnPropertyChanged("Tbill"); } }

        private decimal? _setm;
        public decimal? Setm { get => _setm; set { if (_setm == value) return; _setm = value; OnPropertyChanged("Setm"); } }

        private decimal? _cap;
        public decimal? Cap { get => _cap; set { if (_cap == value) return; _cap = value; OnPropertyChanged("Cap"); } }

        private decimal? _insp;
        public decimal? Insp { get => _insp; set { if (_insp == value) return; _insp = value; OnPropertyChanged("Insp"); } }

        private decimal? _tvop;
        public decimal? Tvop { get => _tvop; set { if (_tvop == value) return; _tvop = value; OnPropertyChanged("Tvop"); } }

        private decimal? _tax17;
        public decimal? Tax17 { get => _tax17; set { if (_tax17 == value) return; _tax17 = value; OnPropertyChanged("Tax17"); } }

        private string? _sstid;
        public string? Sstid { get => _sstid; set { if (_sstid == value) return; _sstid = value; OnPropertyChanged("Sstid"); } }

        private string? _sstt;
        public string? Sstt { get => _sstt; set { if (_sstt == value) return; _sstt = value; OnPropertyChanged("Sstt"); } }

        private string? _mu;
        public string? Mu { get => _mu; set { if (_mu == value) return; _mu = value; OnPropertyChanged("Mu"); } }

        private decimal? _am;
        public decimal? Am { get => _am; set { if (_am == value) return; _am = value; OnPropertyChanged("Am"); } }

        private decimal? _fee;
        public decimal? Fee { get => _fee; set { if (_fee == value) return; _fee = value; OnPropertyChanged("Fee"); } }

        private decimal? _cfee;
        public decimal? Cfee { get => _cfee; set { if (_cfee == value) return; _cfee = value; OnPropertyChanged("Cfee"); } }

        private string? _cut;
        public string? Cut { get => _cut; set { if (_cut == value) return; _cut = value; OnPropertyChanged("Cut"); } }

        private decimal? _exr;
        public decimal? Exr { get => _exr; set { if (_exr == value) return; _exr = value; OnPropertyChanged("Exr"); } }

        private decimal? _prdis;
        public decimal? Prdis { get => _prdis; set { if (_prdis == value) return; _prdis = value; OnPropertyChanged("Prdis"); } }

        private decimal? _dis;
        public decimal? Dis { get => _dis; set { if (_dis == value) return; _dis = value; OnPropertyChanged("Dis"); } }

        private decimal? _adis;
        public decimal? Adis { get => _adis; set { if (_adis == value) return; _adis = value; OnPropertyChanged("Adis"); } }

        private decimal? _vra;
        public decimal? Vra { get => _vra; set { if (_vra == value) return; _vra = value; OnPropertyChanged("Vra"); } }

        private decimal? _vam;
        public decimal? Vam { get => _vam; set { if (_vam == value) return; _vam = value; OnPropertyChanged("Vam"); } }

        private string? _odt;
        public string? Odt { get => _odt; set { if (_odt == value) return; _odt = value; OnPropertyChanged("Odt"); } }

        private decimal? _odr;
        public decimal? Odr { get => _odr; set { if (_odr == value) return; _odr = value; OnPropertyChanged("Odr"); } }

        private decimal? _odam;
        public decimal? Odam { get => _odam; set { if (_odam == value) return; _odam = value; OnPropertyChanged("Odam"); } }

        private string? _olt;
        public string? Olt { get => _olt; set { if (_olt == value) return; _olt = value; OnPropertyChanged("Olt"); } }

        private decimal? _olr;
        public decimal? Olr { get => _olr; set { if (_olr == value) return; _olr = value; OnPropertyChanged("Olr"); } }

        private decimal? _olam;
        public decimal? Olam { get => _olam; set { if (_olam == value) return; _olam = value; OnPropertyChanged("Olam"); } }

        private decimal? _consfee;
        public decimal? Consfee { get => _consfee; set { if (_consfee == value) return; _consfee = value; OnPropertyChanged("Consfee"); } }

        private decimal? _spro;
        public decimal? Spro { get => _spro; set { if (_spro == value) return; _spro = value; OnPropertyChanged("Spro"); } }

        private decimal? _bros;
        public decimal? Bros { get => _bros; set { if (_bros == value) return; _bros = value; OnPropertyChanged("Bros"); } }

        private decimal? _tcpbs;
        public decimal? Tcpbs { get => _tcpbs; set { if (_tcpbs == value) return; _tcpbs = value; OnPropertyChanged("Tcpbs"); } }

        private decimal? _cop;
        public decimal? Cop { get => _cop; set { if (_cop == value) return; _cop = value; OnPropertyChanged("Cop"); } }

        private decimal? _vop;
        public decimal? Vop { get => _vop; set { if (_vop == value) return; _vop = value; OnPropertyChanged("Vop"); } }

        private string? _bsrn;
        public string? Bsrn { get => _bsrn; set { if (_bsrn == value) return; _bsrn = value; OnPropertyChanged("Bsrn"); } }

        private decimal? _tsstam;
        public decimal? Tsstam { get => _tsstam; set { if (_tsstam == value) return; _tsstam = value; OnPropertyChanged("Tsstam"); } }

        private string? _iinn;
        public string? Iinn { get => _iinn; set { if (_iinn == value) return; _iinn = value; OnPropertyChanged("Iinn"); } }

        private string? _acn;
        public string? Acn { get => _acn; set { if (_acn == value) return; _acn = value; OnPropertyChanged("Acn"); } }

        private string? _trmn;
        public string? Trmn { get => _trmn; set { if (_trmn == value) return; _trmn = value; OnPropertyChanged("Trmn"); } }

        private string? _trn;
        public string? Trn { get => _trn; set { if (_trn == value) return; _trn = value; OnPropertyChanged("Trn"); } }

        private string? _pcn;
        public string? Pcn { get => _pcn; set { if (_pcn == value) return; _pcn = value; OnPropertyChanged("Pcn"); } }

        private string? _pid;
        public string? Pid { get => _pid; set { if (_pid == value) return; _pid = value; OnPropertyChanged("Pid"); } }

        private decimal? _pdt;
        public decimal? Pdt { get => _pdt; set { if (_pdt == value) return; _pdt = value; OnPropertyChanged("Pdt"); } }

        private string? _cdcn;
        public string? Cdcn { get => _cdcn; set { if (_cdcn == value) return; _cdcn = value; OnPropertyChanged("Cdcn"); } }

        private int? _cdcd;
        public int? Cdcd { get => _cdcd; set { if (_cdcd == value) return; _cdcd = value; OnPropertyChanged("Cdcd"); } }

        private decimal? _tonw;
        public decimal? Tonw { get => _tonw; set { if (_tonw == value) return; _tonw = value; OnPropertyChanged("Tonw"); } }

        private decimal? _torv;
        public decimal? Torv { get => _torv; set { if (_torv == value) return; _torv = value; OnPropertyChanged("Torv"); } }

        private decimal? _tocv;
        public decimal? Tocv { get => _tocv; set { if (_tocv == value) return; _tocv = value; OnPropertyChanged("Tocv"); } }

        private decimal? _nw;
        public decimal? Nw { get => _nw; set { if (_nw == value) return; _nw = value; OnPropertyChanged("Nw"); } }

        private decimal? _ssrv;
        public decimal? Ssrv { get => _ssrv; set { if (_ssrv == value) return; _ssrv = value; OnPropertyChanged("Ssrv"); } }

        private decimal? _sscv;
        public decimal? Sscv { get => _sscv; set { if (_sscv == value) return; _sscv = value; OnPropertyChanged("Sscv"); } }

        private int? _pmt;
        public int? Pmt { get => _pmt; set { if (_pmt == value) return; _pmt = value; OnPropertyChanged("Pmt"); } }

        private decimal? _pv;
        public decimal? PV { get => _pv; set { if (_pv == value) return; _pv = value; OnPropertyChanged("PV"); } }

        private int? _idd;
        public int? IDD { get => _idd; set { if (_idd == value) return; _idd = value; OnPropertyChanged("IDD"); } }

        private DateTime? _crt;
        public DateTime? CRT { get => _crt; set { if (_crt == value) return; _crt = value; OnPropertyChanged("CRT"); } }

        private string? _uid;
        public string? UID { get => _uid; set { if (_uid == value) return; _uid = value; OnPropertyChanged("UID"); } }

        private string? _refrencenumber;
        public string? RefrenceNumber { get => _refrencenumber; set { if (_refrencenumber == value) return; _refrencenumber = value; OnPropertyChanged("RefrenceNumber"); } }

        private string? _theconfirmationreferenceid;
        public string? TheConfirmationReferenceId { get => _theconfirmationreferenceid; set { if (_theconfirmationreferenceid == value) return; _theconfirmationreferenceid = value; OnPropertyChanged("TheConfirmationReferenceId"); } }

        private string? _theerror;
        public string? TheError { get => _theerror; set { if (_theerror == value) return; _theerror = value; OnPropertyChanged("TheError"); } }

        private string? _thestatus;
        public string? TheStatus { get => _thestatus; set { if (_thestatus == value) return; _thestatus = value; OnPropertyChanged("TheStatus"); } }

        private bool? _thesuccess;
        public bool? TheSuccess { get => _thesuccess; set { if (_thesuccess == value) return; _thesuccess = value; OnPropertyChanged("TheSuccess"); } }

        private string? _thewarning;
        public string? TheWarning { get => _thewarning; set { if (_thewarning == value) return; _thewarning = value; OnPropertyChanged("TheWarning"); } }

        private bool? _apitypesent;
        public bool? ApiTypeSent { get => _apitypesent; set { if (_apitypesent == value) return; _apitypesent = value; OnPropertyChanged("ApiTypeSent"); } }

        private string? _senttaxmemory;
        public string? SentTaxMemory { get => _senttaxmemory; set { if (_senttaxmemory == value) return; _senttaxmemory = value; OnPropertyChanged("SentTaxMemory"); } }

        private string? _NAME_VAHED;
        public string? NAME_VAHED { get => _NAME_VAHED; set { if (_NAME_VAHED == value) return; _NAME_VAHED = value; OnPropertyChanged("NAME_VAHED"); } }
        public long? ROWNUMBER { get; set; }
        public string PersianCRT { get; set; }

        public long? DATE_N { get; set; }
        public string REMARKS { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged(string strCaller = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(strCaller));
        }
    }
}
