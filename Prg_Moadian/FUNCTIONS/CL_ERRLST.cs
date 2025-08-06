namespace Prg_Moadian.FUNCTIONS
{
    public static class CL_ERRLST
    {
        static CL_ERRLST()
        {
            ERROR_MAIN_LST = new List<ERMODEL>
            {
                new ERMODEL { ER_ID = 1, ER_MSG = "کلید خصوصی یا حافظه مالیاتی شما صحیح نیست", ER_TYP = "خطای آغازین مربوط به کلید خصوصی یا حافظه مالیاتی" },
                new ERMODEL { ER_ID = 2, ER_MSG = "احراز هویت شما کامل نیست و باید برای دریافت گواهی الکترونیکی اقدام کنید", ER_TYP = "خطای احراز هویت مربوط به توکن" },
                new ERMODEL { ER_ID = 3, ER_MSG = "مقادیر صورت حساب صحیح نیست لطفا بررسی کنید", ER_TYP = "عدم رعایت قوائد صورت حساب" }
            };
        }
        //ERROR_LST.Add(new ERMODEL { ER_ID = 000, ER_MSG = "asd", ER_TYP = "asd" });
        public static List<ERMODEL> ERROR_MAIN_LST { get; set; } = new List<ERMODEL>();
        public static List<ER_BOD_MODEL> ERROR_BODY_LST { get; set; } = new List<ER_BOD_MODEL>();
        public class ERMODEL
        {
            public byte ER_ID { get; set; }
            public string? ER_MSG { get; set; }
            public string? ER_TYP { get; set; }
            public bool ER_HAPPENED { get; set; } = false;
        }
        public class ER_BOD_MODEL
        {
            public string CODE { get; set; }
            public string SSTID { get; set; }
            public string MU { get; set; }
        }
    }
}
