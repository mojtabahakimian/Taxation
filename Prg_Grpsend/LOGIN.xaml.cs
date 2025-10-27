using Prg_Graphicy.LMethods;
using Prg_Graphicy.Wins;
using Prg_Grpsend.Utility;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.FUNCTIONS;
using Prg_Moadian.SQLMODELS;
using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace Prg_Grpsend
{
    public partial class LOGIN : Window
    {
        CL_CCNNMANAGER dbms;
        private bool IsDbSuccess = true;
        public LOGIN()
        {
            InitializeComponent();

            this.DataContext = this;
        }
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                dbms = new CL_CCNNMANAGER();

                var _Sazman_ = dbms.DoGetDataSQL<SAZMAN>("SELECT TOP 1 SERVERNAM,MEMORYID FROM dbo.SAZMAN").FirstOrDefault();
                Baseknow.SERVERNAM = _Sazman_?.SERVERNAM;
                Baseknow.MEMORYID = _Sazman_?.MEMORYID;

                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            }
            catch (Exception er)
            {
                IsDbSuccess = false;
                new Msgwin(false, "خطا در ارتباط با دیتابیس").ShowDialog();
                LogWriter.WriteLog($"\n[ Database Error, Expetion : Message: {er.Message}{Environment.NewLine} StackTrace: {er.StackTrace}{Environment.NewLine} \n {er.Data} \n {er.InnerException} \n" +
                     $" {er.Source} \n" +
                     $" {er.TargetSite} \n" +
                     $" {er.HResult} \n " +
                     $" {er.HelpLink} \n " +
                     $"End Log ]\n");

                Application.Current.Shutdown();
            }

#if DEBUG
            return;
#endif

            CL_LOCKWATCH Lockwatch = new CL_LOCKWATCH();

            if (Lockwatch.GoCheck() == false)
            {
                new Msgwin(false, "به دلیل عدم ارتباط معتبر با قفل نرم افزار بسته میشود.").ShowDialog();
                App.Current.Shutdown();
                return;
            }
            else if (!Lockwatch.IsSpecial)//IsSuccess
            {
                MoadianLockCheck();
            }

            CL_ScriptUpdateDB.Go();
        }

        private void MoadianLockCheck()
        {
            MoadianLockResult lockResult = MoadianLocker.CheckMoadianLock();

            if (!lockResult.IsValid)
            {
                string title = "مشکل در اشتراک سامانه مودیان";
                string message = $"وضعیت اشتراک سامانه مودیان: {MoadianLocker.TranslateMoadianStatusToPersian(lockResult.Status)}\n";
                message += $"تاریخ جاری سیستم: {MoadianLocker.FormatPersianNumericDate(lockResult.CurrentDateNumeric)}\n";

                switch (lockResult.Status)
                {
                    case MoadianLockStatus.SubscriptionExpired:
                        message += $"اشتراک شما برای این شرکت در تاریخ {MoadianLocker.FormatPersianNumericDate(lockResult.SubscriptionExpiryDateNumeric)} منقضی شده است. لطفاً نسبت به تمدید اشتراک خود اقدام فرمایید.";
                        break;
                    case MoadianLockStatus.NoSubscriptionFound:
                        message += "اشتراک فعالی برای این شرکت یافت نشد یا شناسه حافظه مالیاتی در لیست اشتراک‌ها موجود نیست. لطفاً از صحت شناسه و داشتن اشتراک معتبر اطمینان حاصل کنید.";
                        break;
                    case MoadianLockStatus.InvalidDataFormat:
                        message += "مشکلی در خواندن اطلاعات اشتراک وجود دارد (ممکن است فرمت اطلاعات صحیح نباشد).";
                        // lockResult.Details حاوی اطلاعات فنی است که بهتر است لاگ شود.
                        break;
                    case MoadianLockStatus.ConfigurationError:
                        message += "خطایی در پیکربندی سیستم رخ داده است (مثلاً شناسه حافظه مالیاتی تعریف نشده است). لطفاً تنظیمات برنامه را بررسی کرده.";
                        break;
                    case MoadianLockStatus.InternalError:
                        message += "یک خطای داخلی غیرمنتظره در سیستم بررسی اشتراک رخ داده است. لطفاً مجدداً تلاش کنید یا در صورت تکرار.";
                        break;
                    default:
                        message += "وضعیت نامشخصی برای اشتراک وجود دارد.";
                        break;
                }
                new Msgwin(false, $"{title}\n" + message).ShowDialog();

                new Msgwin(false, "نرم افزار بسته میشود").ShowDialog();

                System.Environment.Exit(0); return;
            }
        }

        private void SetDefaultFocus()
        {
            TxtUsername.Focus();
            TxtUsername?.SelectAll();
        }
        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // مثال برای نادیده گرفتن TextBox های چند خطی
            if (e.Key == Key.Enter)
            {
                if (Keyboard.FocusedElement is UIElement focusedElement)
                {
                    // یا برای Button ها، ممکن است بخواهید کلیک پیش‌فرض فعال شود
                    if (focusedElement is System.Windows.Controls.Button button && button.IsDefault)
                    {
                        // اجازه بدهید Button به صورت عادی Enter را پردازش کند (معمولاً کلیک می‌شود)
                        return;
                    }

                    focusedElement.SimulateTabKeyPress(); e.Handled = true;
                }
            }
        }

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            string? USERNAME = TxtUsername.Text.Trim();

            if (string.IsNullOrEmpty(USERNAME) || string.IsNullOrWhiteSpace(USERNAME))
            {
                new Msgwin(false, "نام کاربری نمی تواند خالی باشد").Show();
                return;
            }
            if (string.IsNullOrEmpty(TxtPassword.Password))
            {
                new Msgwin(false, "رمز عبور نمی تواند خالی باشد").Show();
                return;
            }

            var encodedUsernameParams = new
            {
                USN = CL_FUNTIONS.CODEUN(USERNAME)
            };

            var userRecord = dbms.DoGetDataSQL<SALA_DTL>("SELECT TOP 1 IDD, PSAL_NAME FROM SALA_DTL WHERE SAL_NAME = @USN", encodedUsernameParams).FirstOrDefault();
            if (userRecord == null) // اگر رکوردی با این نام کاربری رمزنگاری شده پیدا نشد
            {
                new Msgwin(false, "نام کاربری یا رمز عبور صحیح نیست").Show();
                return;
            }

            string storedPsAlName = userRecord.PSAL_NAME;
            // 2. بررسی صحت فرمت PSAL_NAME ذخیره شده و استخراج بخش اصلی آن
            if (string.IsNullOrEmpty(storedPsAlName) || storedPsAlName.Length < 6 || storedPsAlName[0] != 'p' || storedPsAlName[storedPsAlName.Length - 1] != 'z')
            {
                new Msgwin(false, "رمز عبور معتبر نیست.").Show();
                return;
            }

            string coreStoredEncodedPassword = storedPsAlName.Substring(3, storedPsAlName.Length - 6);

            // 3. رمز عبور وارد شده توسط کاربر را با استفاده از تابع EncodeCorePassword رمزنگاری کنید
            string? coreEnteredEncodedPassword = CL_FUNTIONS.CODEPS(TxtPassword.Password);
            if (coreEnteredEncodedPassword == null)
            {
                new Msgwin(false, "خطا در پردازش رمز عبور وارد شده. لطفاً از کاراکترهای مجاز استفاده کنید.").Show();
                return;
            }

            if (coreStoredEncodedPassword == coreEnteredEncodedPassword) //Successfully
            {
                Baseknow.USERCOD = userRecord.IDD.ToString();
                Baseknow.UUSER = USERNAME;



                MainWindow WINMAIN = new MainWindow();
                this.Close();
                WINMAIN.Show();
            }
            else
            {
                new Msgwin(false, "نام کاربری یا رمز عبور صحیح نیست").Show();
            }
        }

        private void Window_ContentRendered(object sender, EventArgs e)
        {
            SetDefaultFocus();
        }
    }
}
