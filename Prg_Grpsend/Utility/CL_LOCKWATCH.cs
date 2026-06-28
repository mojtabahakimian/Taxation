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
            "BD618EC8C63B53CF2A72396FCA729FCA", //CORRECT (mrcorrect....)
            "C1B24D1A2F74199C1391551849B865",

            "BF6C88CFCE305635AA5FB0685D5B5635", //Dena faraz new ghofl
            "59DC325C997EC02475F937146CE2655",  //Dena faraz
            "AE799CDDDF23432CB948A3714C484021", //Dena faraz
            "BD618EC8C63B533CAA50B16455505031", //Dena faraz

            "CDBAE05EDFBB33D7BA4821B25CE850D9",
            "DEA2F24BC6A323C7A95033A745F040C9" //CORRECT
        };

        private readonly HashSet<string> DenaFarazKeys = new HashSet<string>
        {
            "BF6C88CFCE305635AA5FB0685D5B5635",
            "59DC325C997EC02475F937146CE2655",
            "AE799CDDDF23432CB948A3714C484021",
            "BD618EC8C63B533CAA50B16455505031"
        };

        public bool IsDenaFarazKey(string key) => DenaFarazKeys.Contains(key);

        public void CheckIsDenafaraz()
        {
            foreach (var key in TheKeys)
            {
                if (IsDenaFarazKey(key))
                {
                    MainWindow.IsDenafaraz = true;
                    CL_Generaly.MrCorrect = "d";
                    LockLogger.Write("[IsDenafaraz] = True");
                    break;
                }
            }
        }

        public bool IsSpecial = false;

        // حداکثر زمان انتظار برای چک سرویس قفل (ثانیه)
        private const int LOCK_SERVICE_TIMEOUT_MS = 15_000;
        // حداکثر زمان کل برای امتحان همه کلیدها (ثانیه)
        private const int LOCK_KEYS_TIMEOUT_MS = 30_000;

        /// <summary>
        /// چک اتصال اولیه با timeout — اگر سرور جواب ندهد بعد از 15 ثانیه false برمی‌گرداند.
        /// </summary>
        private bool IsLockServiceAvailable()
        {
            bool result = false;
            // روی sub-STA-thread با timeout اجرا می‌شود تا اگر سرور کند/قطع بود، block نشویم
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    TINYLib.Tiny tiny = new TINYLib.Tiny();
                    tiny.ServerIP = Baseknow.SERVERNAM;
                    tiny.NetWorkINIT = true;
                    int err = (int)tiny.TinyErrCode;
                    LockLogger.Write($"[INIT] ServerIP={Baseknow.SERVERNAM} | ErrCode={err}");
                    result = err == 0;
                }
                catch (Exception ex)
                {
                    LockLogger.Write($"[INIT THREAD EX] {ex.GetType().Name}: {ex.Message}");
                    // result = false باقی می‌ماند
                }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

            bool completed = thread.Join(LOCK_SERVICE_TIMEOUT_MS);
            if (!completed)
                LockLogger.Write($"[INIT TIMEOUT] Lock service did not respond in {LOCK_SERVICE_TIMEOUT_MS / 1000}s");

            return result; // اگر timeout شد result=false می‌ماند → caller خطای "سرویس نیست" نشان می‌دهد
        }

        /// <summary>
        /// همه کلیدها را به‌صورت موازی (parallel) روی STA thread های جداگانه امتحان می‌کند.
        /// ویژگی‌ها:
        ///  - Early exit: به محض پیدا کردن اولین match، بقیه threads را رها می‌کند
        ///  - Timeout: حداکثر LOCK_KEYS_TIMEOUT_MS منتظر می‌ماند (مناسب برای شبکه‌های کند)
        ///  - threads باقیمانده IsBackground=true هستند → با بسته شدن app خودکار kill می‌شوند
        /// </summary>
        private bool TryMatchKeys()
        {
            bool matched = false;
            string foundData = null;
            object syncLock = new object();

            // matchFoundEvent: با پیدا شدن اولین match set می‌شود → WaitAny فوری برمی‌گردد
            // allDoneEvent: وقتی همه threads تمام شدند signal می‌دهد
            var matchFoundEvent = new System.Threading.ManualResetEventSlim(false);
            var allDoneEvent = new System.Threading.CountdownEvent(TheKeys.Length);

            foreach (var password in TheKeys)
            {
                var pw = password;
                var thread = new System.Threading.Thread(() =>
                {
                    try
                    {
                        // اگر قبلاً match پیدا شده، کار بیشتری نمی‌کنیم
                        if (matchFoundEvent.IsSet) return;

                        TINYLib.Tiny tiny = new TINYLib.Tiny();
                        tiny.ServerIP = Baseknow.SERVERNAM;
                        tiny.NetWorkINIT = true;
                        tiny.UserPassWord = pw;
                        tiny.ShowTinyInfo = true;

                        int err = (int)tiny.TinyErrCode;
                        string serial = tiny.SerialNumber as string ?? "";
                        string data = tiny.DataPartition as string ?? "";

                        LockLogger.Write($"[TRY KEY] {pw[..8]}... → ErrCode={err} | Serial={serial} | Data={data}");

                        bool dataValid = !string.IsNullOrEmpty(data)
                                         && data.Replace("0", "").Trim().Length > 0;

                        if (err == 0 && dataValid)
                        {
                            lock (syncLock)
                            {
                                if (!matched)
                                {
                                    matched = true;
                                    foundData = data;
                                    LockLogger.Write($"[MATCHED] Key={pw[..8]}... | Serial={serial} | Data={data}");
                                    matchFoundEvent.Set(); // ← سیگنال early exit به WaitAny
                                }
                            }
                        }
                        else if (err == 0 && !dataValid)
                        {
                            LockLogger.Write($"[SKIP] ErrCode=0 but Data invalid (false positive) — skipping");
                        }
                    }
                    catch (Exception ex)
                    {
                        // exception را داخل thread می‌بلعیم تا app crash نکند
                        LockLogger.Write($"[KEY THREAD EX] {pw[..8]}... → {ex.GetType().Name}: {ex.Message}");
                    }
                    finally
                    {
                        // حتی در صورت exception، countdown را کم می‌کنیم
                        try { allDoneEvent.Signal(); } catch { }
                    }
                });
                thread.SetApartmentState(System.Threading.ApartmentState.STA);
                thread.IsBackground = true; // اگر app بسته شد، این thread هم kill می‌شود
                thread.Start();
            }

            // صبر می‌کنیم تا یکی از سه حالت:
            // 1. اولین match پیدا شد (matchFoundEvent) → فوری برمی‌گردیم
            // 2. همه threads تمام کردند (allDoneEvent) → نتیجه هر چه باشد برمی‌گردیم
            // 3. Timeout (LOCK_KEYS_TIMEOUT_MS) → شبکه خیلی کند است، برمی‌گردیم
            int waitResult = System.Threading.WaitHandle.WaitAny(
                new[] { matchFoundEvent.WaitHandle, allDoneEvent.WaitHandle },
                LOCK_KEYS_TIMEOUT_MS);

            if (waitResult == System.Threading.WaitHandle.WaitTimeout)
                LockLogger.Write($"[KEYS TIMEOUT] No response within {LOCK_KEYS_TIMEOUT_MS / 1000}s");

            if (matched)
                Baseknow.tindata = foundData;

            return matched;
        }

        /// <summary>
        /// استفاده از lockok.xaml.cs — بدون Initialize
        /// </summary>
        public bool TryMatchValidLock(TINYLib.Tiny tiny, string password)
        {
            tiny.UserPassWord = password;
            tiny.ShowTinyInfo = true;

            int err = (int)tiny.TinyErrCode;
            string data = tiny.DataPartition as string ?? "";

            LockLogger.Write($"[lockok TRY] {password[..8]}... → ErrCode={err}");

            bool dataValid = !string.IsNullOrEmpty(data)
                             && data.Replace("0", "").Trim().Length > 0;

            if (err == 0 && dataValid)
                Baseknow.tindata = data;

            return err == 0 && dataValid;
        }

        public string LockReasonError(string ErrCode)
        {
            switch (ErrCode)
            {
                case "1": return "خطا شماره 1 - قفل شناسایی نـشد , قفل پشت سیستم نیست یا اتصال آن درست بر قرار نیست";
                case "101": return "خطا شماره 101 - قفل شناسایی نشد";
                case "2": return "خطا شماره 2 - قفل متعلق به این نرم افزار نیست.";
                case "102": return "خطا شماره 102 - قفل متعلق به این نرم افزار نیست.";
                case "3": return "خطا شماره 3 - سرویس قفل را در محل قفل بررسی کنید و شبکه را نیز چک کنید.";
                case "5": return "خطا شماره 5 - معمولا این مشکل زمانی رخ میدهد که از نام کامپیوتر به جای آدرس آی پی استفاده شده باشد.";
                case "6": return "خطا شماره 6 - احتمالا سرویس قفل غیر فعال است , آنرا بررسی کنید (پورت 5090 و 9051 را برای فایروال فعال کنید).";
                case "106": return "خطا شماره 106 - معتبر بودن IP و ارتباط شبکه را بررسی نمایید.";
                case "7": return "خطا شماره 7 - تعداد کاربران متصل بیش از حد مجاز است.";
                case "107": return "خطا شماره 107 - تعداد کاربران متصل بیش از حد مجاز است.";
                case "8": return "خطا شماره 8 - تنظیمات مربوط به قفل مجدداً انجام و بروز رسانی شود.";
                case "9": return "خطا شماره 9 - اکتیوایکس و سرویس را آپدیت کنید.";
                case "10": return "خطا شماره 10 - داده های مربوطه درست نیست.";
                case "11": return "خطا شماره 11 - تنظیمات قفل بروز شود.";
                default: return "داده های مربوطه به قفل صحیح نیست.";
            }
        }

        public bool GoCheck()
        {
            LockLogger.Clear();
            LockLogger.Write($"=== GoCheck Start | ServerIP: {Baseknow.SERVERNAM} ===");

            try
            {
                if (File.Exists(@"C:\mojmoh.txt"))
                {
                    LockLogger.Write("[BYPASS] mojmoh.txt found");
                    LoadTindataAnyway();
                    IsSpecial = true;
                    return true;
                }

                if (!IsTrialTimeEnded())
                {
                    LockLogger.Write("[TRIAL] Trial not ended - skipped");
                    return true;
                }

                LockLogger.Write("[TRIAL] Trial ended - checking hardware lock");

                try
                {
                    if (!IsLockServiceAvailable())
                    {
                        LockLogger.Write("[FAIL] Lock service not available.");
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            new Msgwin(false, LockReasonError("6")).ShowDialog();
                            ShowLockWin();
                        });
                        return false;
                    }
                    CheckIsDenafaraz();
                }
                catch (System.Runtime.InteropServices.COMException ex)
                    when (ex.ErrorCode == unchecked((int)0x80040154))
                {
                    LockLogger.Write($"[COM ERROR] TINYLib not registered: {ex.Message}");
                    Application.Current.Dispatcher.Invoke(() =>
                        new Msgwin(false, "فایل‌های مربوط به قفل (TINYLib) به درستی ثبت نشده‌اند.").ShowDialog());
                    return false;
                }
                catch (Exception ex)
                {
                    LockLogger.Write($"[INIT EXCEPTION] {ex.GetType().Name}: {ex.Message}");
                    Application.Current.Dispatcher.Invoke(() =>
                        new Msgwin(false, "تنظیمات قفل در دسترس نیست.").ShowDialog());
                    return false;
                }

                LockLogger.Write("[PRE-MATCH] Lock service OK - trying keys...");

                if (!TryMatchKeys())
                {
                    LockLogger.Write("[NO MATCH] No key matched.");
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        new Msgwin(false, "این قفل متعلق به این نرم افزار نیست!").ShowDialog();
                        new Msgwin(false, LockReasonError("2")).ShowDialog();
                    });
                    return false;
                }
            }
            catch (System.IO.FileNotFoundException ex)
                when (ex.HResult == unchecked((int)0x8007007E))
            {
                LockLogger.Write($"[FILE NOT FOUND] {ex.Message}");
                Application.Current.Dispatcher.Invoke(() =>
                {
                    new Msgwin(false, "فایل و تنظیمات مربوط به رجیستری قفل روی این سیستم انجام نشده!").ShowDialog();
                    Application.Current.Shutdown();
                });
                return false;
            }
            catch (Exception ex)
            {
                LockLogger.Write($"[OUTER EXCEPTION] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                if (IsTrialTimeEnded())
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        new Msgwin(false, "خطا در انجام عملیات , قفل قابل شناسایی نیست").ShowDialog();
                        ShowLockWin();
                    });
                }
                return false;
            }

            LockLogger.Write($"=== GoCheck SUCCESS | tindata={Baseknow.tindata} ===");
            return true;
        }

        private void LoadTindataAnyway()
        {
            // bypass path: بهترین تلاش در background — startup را block نمی‌کند
            _ = System.Threading.Tasks.Task.Run(() => { try { TryMatchKeys(); } catch { } });
        }

        private static void ShowLockWin()
        {
            lockok lockDialog = new lockok();
            lockDialog.ShowDialog();
        }

        public bool IsTrialTimeEnded()
        {
            var recordCount = dbms.DoGetDataSQL<int?>("SELECT COUNT(N_S) AS CN_S FROM DEED_HED").FirstOrDefault();
            return recordCount > 31;
        }
    }
}