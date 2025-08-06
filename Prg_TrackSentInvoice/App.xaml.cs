using Microsoft.Data.SqlClient;
using Prg_Graphicy.Wins;
using Prg_Moadian.CNNMANAGER;
using Prg_TrackSentInvoice.LMETHOD;
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace Prg_TrackSentInvoice
{
    public partial class App : Application
    {
        private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                CL_CCNNMANAGER dbms = new CL_CCNNMANAGER();
                dbms.DoExecuteSQL($@"DROP TABLE {CL_GENERAL.THE_RSDTL_TMP_NAME}");
            }
            catch { }

            if (e.Exception.InnerException is SqlException sqlEx) //Detectable Error
            {
                if (sqlEx.Number == 2 || sqlEx.Number == 232) // Server not available
                {
                    new Msgwin(false, "سرور در دسترس نیست!.").ShowDialog();
                }
                else if (sqlEx.Number == 4060) // Database not found
                {
                    new Msgwin(false, "دیتابیس در دسترس نیست!.").ShowDialog();
                }
                else if (sqlEx.Number == 233)
                {
                    //Console.WriteLine("Database or SQL server service state has been changed");
                    new Msgwin(false, "وضعیت پایگاه داده تغییر یافته , احتمالا سرویس پایگاه داده متوقف یا وضعیت دیتابیس تغییر کرده است !.").ShowDialog();
                }
                else
                {
                    new Msgwin(false, "ارتباط با پایگاه داده دچار مشکل شده است").ShowDialog();
                }
                if (sqlEx.Number == -2 || sqlEx.Number == 53 || sqlEx.Number == 26)
                {
                    new Msgwin(false, "اتصال به سرور پایگاه داده ممکن نیست. لطفاً اتصال شبکه و در دسترس بودن سرور خود را بررسی کنید , همچنین سرویس رو هم بررسی کنید که فعال باشد..").ShowDialog();
                }

                if (sqlEx.Number == 208) // Invalid object name
                {
                    new Msgwin(false, "بعضی از موجودیت های پایگاه داده وجود ندارد , با پشتیبانی در ارتباط باشید").ShowDialog();
                }

            }
            else
            {
                try
                {
                    new Msgwin(false, "“متأسفیم، به نظر می‌رسد که مشکل کوچکی پیش آمده است. برنامه به صورت خودکار بسته خواهد شد. ما از صبر و درک شما سپاسگزاریم.”").Show();
                }
                catch { }
            }
            string filePath = @"C:\Correct\TrackInquerErs.txt";
            try
            {
                using (StreamWriter writer = new StreamWriter(filePath, true))
                {
                    writer.WriteLine("-----------------------------------------------------------------------------");
                    writer.WriteLine("Date : " + DateTime.Now.ToString());
                    writer.WriteLine();
                    writer.WriteLine("=============Error Logging ===========");
                    writer.WriteLine("===========Start============= " + DateTime.Now);

                    var er = e.Exception;
                    string method_source = System.Reflection.MethodBase.GetCurrentMethod().Name;
                    string methodName = er.TargetSite.Name;
                    Exception baseException = er.GetBaseException();
                    IDictionary data = er.Data;
                    string helpLink = er.HelpLink;
                    writer.WriteLine("****DETAILS {****: ");


                    writer.WriteLine(
                        $"{er.Message} \n {er.InnerException} \n {er.StackTrace} \n {er.Source} \n method_source : {method_source}" +
                        $"\n Method Name: {er.TargetSite.Name} \n Base Exception: {er.GetBaseException().Message} \n Exception Data: {er.Data}" +
                        $"\n Help Link: {er.HelpLink} \n  ExceptionType: {er.GetType().FullName} \n" +
                        $"[[[ {CL_CCNNMANAGER.CONNECTION_STR} ]]]");

                    var stackTrace = new StackTrace(er, true);
                    var allFrames = stackTrace.GetFrames().ToList();
                    StringBuilder logmsg = new StringBuilder();
                    foreach (var frame in allFrames)
                    {
                        logmsg.AppendLine($"FileName : {frame.GetFileName()}");
                        logmsg.AppendLine($"LineNumber : {frame.GetFileLineNumber()}");
                        logmsg.AppendLine($"method : {frame.GetMethod()}");
                        logmsg.AppendLine($"method name : {frame.GetMethod().Name}");
                        logmsg.AppendLine($"ClassName : {frame.GetMethod().DeclaringType.ToString()}");
                        logmsg.AppendLine(); // for an extra line space
                    }
                    writer.WriteLine(logmsg.ToString());

                    writer.WriteLine("}****DETAILS****END: ");

                    writer.WriteLine(e.GetType().FullName);
                    writer.WriteLine("Message : " + e.Exception.Message);
                    writer.WriteLine("\nStackTrace : " + e.Exception.StackTrace);
                    writer.WriteLine("\nStackTrace : " + e.Exception.InnerException);

                    writer.WriteLine("===========End============= " + DateTime.Now);
                }
            }
            catch (Exception)
            {
                return;
            }
        }
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            //if (CL_FUNTIONS.IsApplicationRunning())
            //{
            //    Environment.Exit(0); return;
            //}

            //string[] args = e.Args;

            ////string[] defaultArgs = new string[] { "default_value" };
            ////args = args.Length > 0 ? args : defaultArgs;
            ////args[0] = "A2HEZ304D010021C3C5D15";

            //if (args.Length > 0)
            //{
            //    string input = args[0];
            //    if (!(input is null))
            //    {
            //        if (!string.IsNullOrEmpty(input) && input.Length != 0)
            //        {
            //            FactorManagement_WIN FM_WIN = new FactorManagement_WIN();
            //            FM_WIN.TAXID = input;
            //            FM_WIN.Show();
            //            return;
            //        }
            //        else
            //        {
            //            new Msgwin(false, "خطای 1 عدم بارگذاری صحیح").ShowDialog();
            //            System.Environment.Exit(0);
            //        }
            //    }
            //    else
            //    {
            //        new Msgwin(false, "خطای عدم بارگذاری صحیح").ShowDialog();
            //        System.Environment.Exit(0);
            //    }
            //}
            //else
            //{
            //    new Msgwin(false, "خطای 0 عدم بارگذاری صحیح").ShowDialog();
            //    System.Environment.Exit(0);
            //}
        }

        private void Application_DispatcherUnhandledException_1(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {

        }
    }
}
