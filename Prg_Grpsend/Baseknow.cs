namespace Prg_Grpsend
{
    public static class Baseknow
    {
        public static string USERCOD { get; set; } = "0";
        public static string UUSER { get; internal set; }
        public static string SERVERNAM { get; internal set; }

        private static string _tindata;
        public static string tindata
        {
            get
            {
                if (string.IsNullOrEmpty(_tindata))
                {
                    _tindata = "0000000000000000000000000000";
                }
                return _tindata;
            }
            set { _tindata = value; }
        }

        public static string MEMORYID { get; internal set; }
    }
}
