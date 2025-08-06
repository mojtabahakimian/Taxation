using System.ComponentModel;

namespace Prg_Moadian.SQLMODELS
{
    public class HEAD_LST : INotifyPropertyChanged, ICloneable
    {
        public object Clone()
        {
            return this.MemberwiseClone();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        private bool _IsSelected = false;
        public bool IsSelected { get => _IsSelected; set { if (_IsSelected == value) return; _IsSelected = value; OnPropertyChanged("IsSelected"); } }
        public string? NAME { get; set; }
        public string? DEPNAME { get; set; }
        public string? SHNAME { get; set; }
        public string? CUSTKNAME { get; set; }
        public string? PEPNAME { get; set; }
        public string? PENAME { get; set; }
        public string? PPAME { get; set; }
        public int? inty { get; set; }
        public int? inp { get; set; }
        public int? ins { get; set; }
        public string? irtaxid { get; set; }
        public decimal? insp { get; set; }
        public decimal? cap { get; set; }
        public int? setm { get; set; }

        public double? NUMBER { get; set; }
        public double? TAG { get; set; }
        public int? ANBAR { get; set; }
        public double? NUMBER1 { get; set; }
        public long? DATE_N { get; set; }
        public string TAH { get; set; }
        public double? MAS { get; set; }
        public double? VAS { get; set; }
        public double? N_S { get; set; }
        public string CUST_NO { get; set; }
        public string MOLAH { get; set; }
        public double? M_NAGHD { get; set; }
        public double? MABL_VAR { get; set; }
        public string MOIN_VAR { get; set; }
        public double? MABL_HAV { get; set; }
        public string MOIN_HAV { get; set; }
        public double? MABL_HAZ { get; set; }
        public string MOIN_HAZ { get; set; }
        public double? TAKHFIF { get; set; }
        public string MOIN_KHF { get; set; }
        public int? ANBARF { get; set; }
        public double? FNUMCO { get; set; }
        public int? DEPATMAN { get; set; }
        public int? SHIFT { get; set; }
        public int? CUST_KIND { get; set; }
        public string USER_NAME { get; set; }
        public string SHARAYET { get; set; }
        public bool? SGN1 { get; set; }
        public bool? SGN2 { get; set; }
        public bool? SGN3 { get; set; }
        public bool? SGN4 { get; set; }
        public double? MBAA { get; set; }
        public string HMBAA { get; set; }
        public double? TAMIR { get; set; }
        public bool? TICMBAA { get; set; }
        public bool? TKHF { get; set; }
        public bool? OKF { get; set; }
        public byte SADER { get; set; }
        public double? ARZD { get; set; }
        public byte ARZKIND { get; set; }
        public long? CDDATE { get; set; }
        public int? CDTIME { get; set; }
        public long? OKDATE { get; set; }
        public int? OKTIME { get; set; }
        public bool? JAY { get; set; }
        public int? MODAT_PPID { get; set; }
        public int? PEPID { get; set; }
        public int? PEID { get; set; }
        public int? sgn1usid { get; set; }
        public int? sgn2usid { get; set; }
        public int? sgn3usid { get; set; }
        public DateTime? CRT { get; set; }
        public int? UID { get; set; }


    }
}
