using Prg_Graphicy.LMethods;
using Prg_Graphicy.Wins;
using Prg_Moadian.CNNMANAGER;
using System;
using System.Globalization;
using System.Linq;

namespace Prg_Grpsend.Utility
{
    public static class Saftey
    {
        static readonly CL_CCNNMANAGER dbms = new CL_CCNNMANAGER();
        public static string SETSECURITY(string obj)
        {
            var RST = dbms.DoGetDataSQL<dynamic>("SELECT TFORMS.CAPTION, TFORMS.kind, SAL_CHEK.USERCO, SAL_CHEK.RUN, SAL_CHEK.SEE, SAL_CHEK.INP, SAL_CHEK.UPD, SAL_CHEK.DEL FROM TFORMS INNER JOIN (SALA_DTL INNER JOIN SAL_CHEK ON SALA_DTL.IDD = SAL_CHEK.USERCO) ON TFORMS.IDH = SAL_CHEK.OBJECT " +
                "WHERE  (dbo.TFORMS.FORMNAME = '" + obj + "') AND  (SAL_CHEK.USERCO = " + Baseknow.USERCOD + " )").FirstOrDefault();

            if (RST != null)
            {
                if (RST.SEE != true)
                {
                    return "شماره اجازه دسترسی به این بخش را ندارید !";
                }
                else if (RST.INP != true || RST.UPD != true)
                {
                    return "شما مجاز به ورود یا تغییر داده این بخش نیستید !";
                }
            }
            else
            {
                return "تنظیم پیاده سازی مربوط به دسترسی انجام نشده با پشتیبانی در ارتباط باشید";
            }

            return string.Empty;
        }
        public static bool LETSGO(string frm)
        {
            bool returnValue = false;

            var RST = dbms.DoGetDataSQL<dynamic>("SELECT   dbo.TFORMS.FORMNAME, dbo.SAL_CHEK.USERCO, dbo.SAL_CHEK.RUN, dbo.SAL_CHEK.SEE, dbo.SAL_CHEK.INP, dbo.SAL_CHEK.UPD, dbo.SAL_CHEK.DEL FROM  dbo.TFORMS INNER JOIN  dbo.SAL_CHEK ON dbo.TFORMS.IDH = dbo.SAL_CHEK.OBJECT INNER JOIN dbo.SALA_DTL ON dbo.SAL_CHEK.USERCO = dbo.SALA_DTL.IDD WHERE  " +
                "   (dbo.TFORMS.FORMNAME = '" + frm + "') AND (dbo.SAL_CHEK.USERCO = " + Baseknow.USERCOD + " )").FirstOrDefault();
            if (RST != null)
            {
                if ((bool)RST.RUN)
                {
                    returnValue = true;
                }
                else
                {
                    returnValue = false;
                }
            }
            else
            {
                returnValue = false;
            }

            return returnValue;
        }
    }
}
