namespace Prg_Moadian.SQLMODELS
{
    public class TRACK_TAXDTL
    {
        public long? ROWNUMBER { get; set; }
        public string? Taxid { get; set; }
        public string? Inno { get; set; }
        public int? Inty { get; set; }
        public int? Inp { get; set; }
        public int? Ins { get; set; }
        public int? IDD { get; set; }
        /// <summary>
        /// تعداد سطر فاکتور
        /// </summary>
        public int? LineCount { get; set; } //COUNT(*) AS LineCount From dbo.TAXDTL
        public DateTime? CRT { get; set; }
        public string? UID { get; set; }
        public string? RefrenceNumber { get; set; }
        public string? TheConfirmationReferenceId { get; set; }
        public string? TheError { get; set; }
        public string? TheStatus { get; set; }
        public bool? TheSuccess { get; set; }
        public bool? ApiTypeSent { get; set; }
        public string PersianCRT { get; set; }
        public string SentTaxMemory { get; set; }

        public double? NUMBER { get; set; }
        public double? TAG { get; set; }
        public int? DATE_N { get; set; }
    }
}
