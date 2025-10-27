using Prg_Graphicy.Wins;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.Generaly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace Prg_Grpsend.Utility
{
    public class CL_LOCKWATCH
    {
        private readonly CL_CCNNMANAGER dbms = new CL_CCNNMANAGER();
        public readonly string[] TheKeys =
        {
            "D21336C4BBEF5189D2211240151A875",
            "BD618EC8C63B53CF2A72396FCA729FCA",  //CORRECT (mrcorrect....)
            "C1B24D1A2F74199C1391551849B865",

            "BF6C88CFCE305635AA5FB0685D5B5635", //Dena faraz new ghofl
            "59DC325C997EC02475F937146CE2655",  //Dena faraz
            "AE799CDDDF23432CB948A3714C484021", //Dena faraz
            "BD618EC8C63B533CAA50B16455505031", //Dena faraz

            "CDBAE05EDFBB33D7BA4821B25CE850D9",
            "DEA2F24BC6A323C7A95033A745F040C9" //CORRECT
        };
        private readonly HashSet<string> DenaFarazKeys = new HashSet<string> //Denafarz
        {
            "BF6C88CFCE305635AA5FB0685D5B5635",
            "59DC325C997EC02475F937146CE2655",
            "AE799CDDDF23432CB948A3714C484021",
            "BD618EC8C63B533CAA50B16455505031"
        };
        public bool IsDenaFarazKey(string key)
        {
            return DenaFarazKeys.Contains(key);
        }
        public void CheckIsDenafaraz()
        {
            foreach (var key in TheKeys)
            {
                if (IsDenaFarazKey(key))
                {
                    MainWindow.IsDenafaraz = true;
                    CL_Generaly.MrCorrect = "d";
                }
            }
        }

        public string LockReasonError(string ErrCode)
        {
            switch (ErrCode)
            {
                case "1": ErrCode = @"خطا شماره 1 - قفل شناسایی نـشد , قفل پشت سیستم نیست  یا اتصال آن درست بر قرار نیست"; break;
                case "101": ErrCode = "خطا شماره 101 -  قفل شناسایی نشد"; break;
                case "2": ErrCode = "خطا شماره 2 -  قفل مال متعلق به این نرم افزار نیست."; break;
                case "102": ErrCode = "خطا شماره 102 -  قفل متعلق به این نرم افزار نیست."; break;

                case "3": ErrCode = "خطا شماره 3 -  سرویس قفل را در محل قفل بررسی کنید و شبکه را نیز چک کنید."; break;
                case "5": ErrCode = "خطا شماره 5 -  معمولا این مشکل زمانی رخ میدهد که از نام کامپیوتر به جای آدرس آی پی استفاده شده باشد و یا آدرس آی پی اشتباه باشد."; break;
                case "6": ErrCode = @"خطا شماره 6 - احتمالا سرویس قفل غیر فعال است , آنرا بررسی کنید درضمن
                                 معتبر بودن IP و ارتباط شبکه را بررسی نمایید.(پورت 5090 و 9051 را برای فایروال فعال کنید)."; break;
                case "106": ErrCode = "معتبر بودن خطا شماره 106 -   IP و ارتباط شبکه را بررسی نمایید.(پورت 5090 و 9051 را برای فایروال فعال کنید)."; break;
                case "7": ErrCode = "خطا شماره 7 -  تعداد کاربران متصل بیش از حد مجاز است."; break;
                case "107": ErrCode = "خطا شماره 107 -  تعداد کاربران متصل بیش از حد مجاز است."; break;
                case "8": ErrCode = "خطا شماره 8 -  تنظیمات مربوط به قفل مجددانجام و بروز رسانی شود."; break;
                case "9": ErrCode = "خطا شماره 9 -  اکتیوایکس و سرویس را آپدیت کنید."; break;
                case "10": ErrCode = "خطا شماره 10 -  داده های مربوطه درست نیست."; break;
                case "11": ErrCode = "خطا شماره 11 -  تنظیمات قفل بروز شود."; break;
                default:
                    ErrCode = "داده های مربوطه به قفل صحیح نیست.";
                    break;
            }
            return ErrCode;
        }

        public bool IsSpecial = false;
        private AxTINYLib.AxTiny InitializeAxTiny()
        {
            //AxTINYLib.AxTiny axTiny1 = new AxTINYLib.AxTiny
            //{
            //    ServerIP = Baseknow.SERVERNAM,
            //    Enabled = true,
            //    Initialize = true,
            //    NetWorkINIT = true
            //};

            //axTiny1.CreateControl();

            AxTINYLib.AxTiny axTiny1 = new AxTINYLib.AxTiny();
            axTiny1.CreateControl();

            axTiny1.ServerIP = Baseknow.SERVERNAM;
            axTiny1.Enabled = true;
            axTiny1.Initialize = true;
            axTiny1.NetWorkINIT = true;

            return axTiny1;
        }
        public bool TryMatchValidLock(AxTINYLib.AxTiny axTiny, string password)
        {
            axTiny.UserPassWord = password;
            axTiny.ShowTinyInfo = true;
            Baseknow.tindata = axTiny.DataPartition;

            return axTiny.TinyErrCode == 0;
        }

        public delegate (bool, bool) DualBoolDelegate();
        /// <summary>
        /// Check Lock and additionaly return the status boolean
        /// </summary>
        /// <returns></returns>
        public bool GoCheck()
        {
            try
            {
                if (File.Exists(@"C:\mojmoh.txt"))
                {
                    LoadTindataAnyway();
                    //Ok
                    IsSpecial = true;
                }
                else
                {
                    if (IsTrialTimeEnded())
                    {
                        AxTINYLib.AxTiny axTiny1;

                        try
                        {
                            axTiny1 = InitializeAxTiny(); //Try to get regiestered files on system (Tiny.ocx)

                            CheckIsDenafaraz();
                        }
                        catch (System.Runtime.InteropServices.COMException ex) when (ex.ErrorCode == unchecked((int)0x80040154))
                        {
                            new Msgwin(false, "فایل‌های مربوط به قفل (Tiny x64) به درستی ثبت نشده‌اند. لطفاً با استفاده از Correct Installer، این فایل‌ها را بر روی سیستم قفل نصب کنید و سپس دوباره بررسی نمایید.").ShowDialog();
                            return false;
                        }
                        catch (Exception)
                        {
                            new Msgwin(false, "تنظیمات قفل به دسترسی انجام نشده , فایل های قفل به طور کلی در دسترس نیست").ShowDialog();
                            return false;
                        }

                        if (axTiny1.TinyErrCode != 0) //The First state of lock is ok
                        {
                            new Msgwin(false, LockReasonError(axTiny1.TinyErrCode.ToString())).ShowDialog();
                            ShowLockWin();
                            return false;
                        }

                        foreach (var password in TheKeys)
                        {
                            if (TryMatchValidLock(axTiny1, password))
                                break;
                        }

                        if (axTiny1.TinyErrCode != 0) //Still the lock is not match with lock the app needs
                        {
                            new Msgwin(false, "این قفل متعلق به این نرم افزار نیست!").ShowDialog();
                            new Msgwin(false, LockReasonError(axTiny1.TinyErrCode.ToString())).ShowDialog();
                            return false;
                        }

                        if (!string.IsNullOrEmpty(Baseknow.tindata))
                        {
                            ////1 دارا
                            //if (Strings.Mid(Baseknow.tindata, 9, 1) == "1")
                            //{
                            //    CL_HESABDARI.SETLEVEL(0);
                            //}
                            ////1 CORRECT
                            //if (Strings.Mid(System.Convert.ToString(Baseknow.tindata), 20, 7) != "CORRECT" || string.IsNullOrEmpty(Baseknow.tindata))
                            //{
                            //    CL_HESABDARI.SETLEVEL(1);
                            //}
                            //else
                            //{
                            //    //DoCmd.RunSQL "UPDATE dbo.SAL_CHEK Set RUN = 1 WHERE (OBJECT BETWEEN 368 AND 380) "
                            //}
                        }

                    }
                }
            }
            catch (System.IO.FileNotFoundException ex) when (ex.HResult == unchecked((int)0x8007007E))
            {
                new Msgwin(false, "فایل و تنظیمات مربوط به رجیستری قفل روی این سیستم انجام نشده !").ShowDialog();
                Application.Current.Shutdown();
                return false;
            }
            catch (Exception)
            {
                if (IsTrialTimeEnded())
                {
                    new Msgwin(false, "خطا در انجام عملیات , قفل قابل شناسایی نیست").ShowDialog();
                    ShowLockWin();
                }
                return false;
            }

            return true;
        }

        private void LoadTindataAnyway()
        {
            try
            {
                AxTINYLib.AxTiny axTiny1 = InitializeAxTiny(); //Try to get regiestered files on system (Tiny.ocx)

                foreach (var password in TheKeys)
                {
                    if (TryMatchValidLock(axTiny1, password))
                        break;
                }
            }
            catch { }
        }

        private static void ShowLockWin()
        {
            lockok lockDialog = new lockok();
            lockDialog.ShowDialog();
        }

        public bool IsTrialTimeEnded()
        {
            var recordCount = dbms.DoGetDataSQL<int?>("SELECT COUNT(N_S) AS CN_S FROM DEED_HED").FirstOrDefault();
            if (recordCount > 31)
            {
                return true;
            }
            return false;
        }
    }

}
