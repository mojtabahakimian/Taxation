using Dapper;
using Newtonsoft.Json;
using NPOI.SS.UserModel;
using NPOI.Util;
using NPOI.XSSF.UserModel;
using Prg_Graphicy.LMethods;
using Prg_Graphicy.Wins;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.FUNCTIONS;
using Prg_Moadian.Generaly;
using Prg_Moadian.Service;
using Prg_Moadian.SQLMODELS;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using TaxCollectData.Library.Business;
using TaxCollectData.Library.Dto.Config;
using TaxCollectData.Library.Dto.Content;
using TaxCollectData.Library.Dto.Properties;
using TaxCollectData.Library.Enums;
using static Prg_Moadian.CNNMANAGER.TaxModel;
using static Prg_Moadian.Generaly.CL_Generaly;

namespace Prg_TrackSentInvoice
{
    public partial class MainWindow : Window
    {
        CL_CCNNMANAGER dbms = new CL_CCNNMANAGER();
        CustomExceptErMsg CER = new CustomExceptErMsg();
        CL_FUNTIONS Functions = new CL_FUNTIONS();
        CL_ESTELAM ESTELAM;
        BackgroundWorker BGWorker = new BackgroundWorker();
        private bool IsTheTimerStillWorking;
        public List<TRACK_TAXDTL>? TAXDTL_DATA { get; set; } = new List<TRACK_TAXDTL>();

        /// <summary>
        /// برای جلوگیری از تداخل کار های دیگر با تایمر
        /// </summary>
        public bool IsOtherProccessingNow { get; set; } = false;
        private bool NowIsReady { get; set; } = false;


        private string _taxurl;
        public string TaxURL
        {
            get
            {
                if ((bool)RD_MAINTAX.IsChecked) //اصلی
                {
                    _taxurl = "https://tp.tax.gov.ir/req/api/";
                }
                else if ((bool)RD_SANDBOX.IsChecked) //آزمایشی
                {
                    _taxurl = "https://sandboxrc.tax.gov.ir/req/api/";
                }
                return _taxurl;
            }
        }

        private bool _isTextfiltercorrected = false;
        public bool IsTextSearchCorrected
        {
            get
            {
                if (string.IsNullOrEmpty(SEARCHBOX.Text.Trim()))
                {
                    _isTextfiltercorrected = false;
                }
                else
                {
                    _isTextfiltercorrected = true;
                }
                return _isTextfiltercorrected;
            }
            //set { _isTextfiltercorrected = value; }
        }

        public MainWindow()
        {
            InitializeComponent();

            TimerEstelam.Tick += TimerEstelam_TICK;
            TimerEstelam.Interval = new TimeSpan(0, 0, 5, 0);

            BGWorker.DoWork += BGWorker_DoWork;
            BGWorker.ProgressChanged += BGWorker_ProgressChanged;
            BGWorker.RunWorkerCompleted += BGWorker_RunWorkerCompleted;  //Tell the user how the process went
            BGWorker.WorkerReportsProgress = true;
            BGWorker.WorkerSupportsCancellation = true;


            // Update Table Scripts
            CL_ScriptUpdateDB.Go();
        }

        private void BGWorker_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            //{Begin---------------------------------
            if (true) //Just for format
            {
                IsOtherProccessingNow = true;

                List<TRACK_TAXDTL> UndecidedData = TAXDTL_DATA.Where(item =>
                item.TheStatus == null ||
                item.TheStatus == "PENDING" ||
                item.TheStatus == "" ||
                item.TheStatus == "NULL").ToList();

                for (int i = 0; i < UndecidedData.Count; i++)
                {
                    try
                    {
                        TimeSpan difference = DateTime.Today - UndecidedData[i].CRT.Value;
                        if (difference.TotalDays > 31) // Check if the difference is greater than 31 days
                        {
                            dbms.DoExecuteSQL($"UPDATE dbo.TAXDTL SET TheStatus = N'EXPIRED' WHERE IDD = {UndecidedData[i].IDD}");
                        }
                        else
                        {
                            ESTELAM.GETESTELAM_REFCODE_UPDATE(UndecidedData[i].RefrenceNumber);
                        }
                        //RefGetData();
                    }
                    catch (Exception er)
                    {
                        Dispatcher.Invoke(new Action(() =>
                        {
                            if (er is Microsoft.Data.SqlClient.SqlException)
                            {
                                new Msgwin(false, "خطا در ارتباط با دیتابیس").ShowDialog();
                                System.Environment.Exit(0);
                            }
                            else
                            {
                                string _whichtype = "";
                                if (UndecidedData[i].ApiTypeSent is false)
                                    _whichtype = "سامانه آزمایشی";
                                else
                                    _whichtype = "سامانه اصلی";

                                //NotifyIcon.ShowBalloonTip("خطا", $"خطا در انجام عملیات استعلام {UndecidedData[i].RefrenceNumber} , از {_whichtype} ,  برای فاکتور (حواله) شماره : {Convert.ToInt64(UndecidedData[i].Inno)}", BalloonIcon.Error);


                                CL_Generaly.DoWritePRGLOG("GETESTELAM_REFCODE_UPDATE : \n" +
                                    $"خطا در انجام عملیات استعلام {UndecidedData[i].RefrenceNumber} , از {_whichtype} ,  برای فاکتور (حواله) شماره : {Convert.ToInt64(UndecidedData[i].Inno)}", er);
                            }
                        }));
                    }
                    //BGWorker.ReportProgress(i);
                    //Check if there is a request to cancel the process
                    if (BGWorker.CancellationPending)
                    {
                        e.Cancel = true;
                        BGWorker.ReportProgress(0);
                        return;
                    }
                }
                Dispatcher.Invoke(new Action(() =>
                {
                    RefGetData();
                }));
            }
            //End}-----------------------------------

            //Remember in the loop we set i < 100 so in theory the process will complete at 99%
            //BGWorker.ReportProgress(TAXDTL_DATA.Count);
        }
        private void BGWorker_ProgressChanged(object sender, System.ComponentModel.ProgressChangedEventArgs e)
        {
            //progressBar1.Maximum = StrRefList.Count;
            //progressBar1.Value = e.ProgressPercentage;
        }
        private void BGWorker_RunWorkerCompleted(object sender, System.ComponentModel.RunWorkerCompletedEventArgs e)
        {
            IsOtherProccessingNow = false;
            if (e.Cancelled)
            {
                //lblStatus.Text = "Process was cancelled";
            }
            else if (e.Error != null)
            {
                LogWriter.WriteLog($"\nBGWorker_RunWorkerCompleted with Error : {e.Error} , {e.Result}\n");
                //lblStatus.Text = "There was an error running the process. The thread aborted";
            }
            else
            {
                //lblStatus.Text = "Process was completed";
            }
        }

        System.Windows.Threading.DispatcherTimer TimerEstelam = new System.Windows.Threading.DispatcherTimer();
        private void TimerEstelam_TICK(object? sender, EventArgs e)
        {
            if (IsTheTimerStillWorking) return; // جلوگیری از اینکه تایمر هنوز در حال کار است.

            if (IsOtherProccessingNow is false) // تداخل با حالت دستی نداشته باشه 
            {
                IsTheTimerStillWorking = true;

                if (CL_Generaly.TokenLifeTime.IsExpired)
                {
                    try
                    {
                        InitiTaxConfig();
                    }
                    catch (Exception)
                    {
                        //new Msgwin(false, "زمان توکن پیگیری به پایان رسیده , برنامه را مجددا باز کنید.").ShowDialog();
                        //System.Environment.Exit(0);
                        LBL_WARN.Content = "زمان توکن پیگیری به پایان رسیده , برنامه را مجددا باز کنید";
                    }
                }
                else
                {
                    LBL_WARN.Content = null;
                }

                BGWorker.RunWorkerAsync();

                IsTheTimerStillWorking = false;
            }
        }
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            CL_PRC_LOADER.Start("Prg_TrackSentInvoice.ADDON.Preloader.exe");

            try
            {
                var dbms = new CL_CCNNMANAGER();
                var _YEA_ = dbms.DoGetDataSQL<string>("SELECT TOP 1 YEA FROM dbo.SAZMAN").FirstOrDefault();
                var _NAME_ = dbms.DoGetDataSQL<string>("SELECT TOP 1 NAME FROM dbo.SAZMAN").FirstOrDefault();

                this.Title = $"استعلام صورت حساب برای {_NAME_} سال مالی {_YEA_} ";
            }
            catch (Exception)
            {
                new Msgwin(false, "خطا در ارتباط با دیتابیس").ShowDialog();
                System.Environment.Exit(0);
            }

            #region DEBUGY
            //var _newsaz = dbms.DoGetDataSQL<SAZMAN>("SELECT MEMORYID,MEMORYIDsand,PRIVIATEKEY,Dcertificate FROM dbo.SAZMAN").FirstOrDefault();
            //var PrivateKeyTax = _newsaz.PRIVIATEKEY.Replace("-----BEGIN PRIVATE KEY-----\r\n", "").Replace("\r\n-----END PRIVATE KEY-----\r\n", "").Trim();
            //string MemoryTax = "";
            //if (TaxURL == "https://tp.tax.gov.ir/req/api/")
            //{
            //    MemoryTax = _newsaz.MEMORYID.Trim(); //حافظه مالیاتی اصلی
            //}
            //else
            //{
            //    MemoryTax = _newsaz.MEMORYIDsand.Trim(); //حافظه مالیاتی تستی سندباکس
            //}

            //TaxApiService.Instance.Init(MemoryTax, new SignatoryConfig(PrivateKeyTax, null), new NormalProperties(ClientType.SELF_TSP), TaxURL);
            //ServerInformationModel serverInformationModel = TaxApiService.Instance.TaxApis.GetServerInformation();
            //TokenModel tokenModel = TaxApiService.Instance.TaxApis.RequestToken();

            //var referenceCode = "d3056ae5-5c49-470d-b05b-934d7f1ec38c";

            //var inquiryResultModels =
            //    TaxApiService.Instance.TaxApis.InquiryByReferenceId(new() { "d3056ae5-5c49-470d-b05b-934d7f1ec38c" });


            //List<string> list = new List<string>();
            //list.Add(referenceCode);
            //List<InquiryResultModel> list2 = TaxApiService.Instance.TaxApis.InquiryByReferenceId(list);
            //TaxModel.InquiryByReferenceIdModel inquiryByReferenceIdModel = new TaxModel.InquiryByReferenceIdModel();
            //string value = list2.Select((InquiryResultModel x) => x.Data).FirstOrDefault()!.ToString();
            //TaxModel.InquiryByReferenceIdModel.Root root = JsonConvert.DeserializeObject<TaxModel.InquiryByReferenceIdModel.Root>(value);
            //root.status = list2[0].Status;

            #endregion

            ESTELAM = new CL_ESTELAM();
            InitiTaxConfig();

            RefGetData();

            TimerEstelam.Start();
            TimerEstelam_TICK(null, null);


            var res = dbms.DoGetDataSQL<decimal>("SELECT MOADINA_SCNUM FROM dbo.SAZMAN").FirstOrDefault();
            if (res > 0)
            {
                MOADINA_SCNUM.Tag = res.ToString();
                MOADINA_SCNUM.Text = res.ToString();
            }

            CL_PRC_LOADER.Dispose();
        }
        private void Window_ContentRendered(object sender, EventArgs e)
        {
            NowIsReady = true;
        }

        private void InitiTaxConfig() //مقدار دهی سرویس
        {
            try
            {
                ESTELAM.GET_INIT_TAX(TaxURL);
            }
            catch (Exception er)
            {
                MyBoderStatus.Height = 30;
                MyBoderStatus.Background = new SolidColorBrush(Color.FromArgb(255, 178, 34, 34)); // Firebrick

                var _msger = CER.ExpecMsgEr(er);

                if (!string.IsNullOrEmpty(_msger)) //Known Message
                {
                    new Msgwin(false, _msger).ShowDialog();//نمایش خطای شناخته شده
                    //System.Environment.Exit(0);
                }
                else
                {
                    new Msgwin(false, "خطا در انجام عملیات , Unknown").ShowDialog();
                    CL_Generaly.DoWritePRGLOG("Unknown Error in Send Estelam Invoce : \n", er);
                    //System.Environment.Exit(0);
                }
            }
        }

        private void RefGetData()
        {
            //اگر چک روی سامانه اصلی است :
            byte _apitypesent = 0;
            // [1 | True] = Main
            // [0 | False] = SandBox Testy
            if (TaxURL is "https://tp.tax.gov.ir/req/api/") // اگر روی اصلیه
                _apitypesent = 1;
            else
                _apitypesent = 0;

            bool isOnlyLast30Days = Convert.ToBoolean(RD_LAST30.IsChecked);

            if (isOnlyLast30Days)
            {
                TAXDTL_DATA = dbms.DoGetDataSQL<TRACK_TAXDTL>(@$"SELECT 
                                                         ROW_NUMBER() OVER (ORDER BY MAX(T.CRT) DESC) AS RowNumber,
                                                         T.Taxid,
                                                         MAX(T.Inno) AS Inno,
                                                         MAX(T.Inty) AS Inty,
                                                         MAX(T.Inp) AS Inp,
                                                         MAX(T.Ins) AS Ins,
                                                         COUNT(*) AS LineCount,
                                                         T.UID,
                                                         T.RefrenceNumber,
                                                         T.TheConfirmationReferenceId,
                                                         T.SentTaxMemory,
                                                         MAX(T.CRT) AS CRT,
                                                         MAX(CAST(T.TheSuccess AS INT)) AS TheSuccess,
                                                         T.TheStatus,
                                                         MAX(T.DATE_N) AS DATE_N,
                                                         MAX(CAST(T.ApiTypeSent AS INT)) AS ApiTypeSent,
                                                         MAX(T.TheError) AS TheError,
                                                         MAX(H.CUST_NO) AS CUST_NO,
                                                         MAX(H.MOLAH) AS MOLAH,
                                                         MAX(H.SHARAYET) AS SHARAYET
                                                     FROM 
                                                         dbo.TAXDTL T
                                                     LEFT OUTER JOIN dbo.HEAD_LST H ON 
                                                         CASE 
                                                             WHEN ISNUMERIC(RIGHT(T.Inno, 6)) = 1 
                                                             THEN CAST(RIGHT(T.Inno, 6) AS float) 
                                                             ELSE NULL 
                                                         END = H.NUMBER 
                                                         AND H.TAG = 2
                                                     WHERE 
                                                         T.ApiTypeSent = {_apitypesent} 
                                                         AND T.CRT >= DATEADD(DAY, -30, GETDATE())
                                                     GROUP BY 
                                                         T.Taxid, T.TheStatus, T.UID, T.RefrenceNumber, T.TheConfirmationReferenceId, T.SentTaxMemory
                                                     ORDER BY 
                                                         CRT DESC;").ToList();
            }
            else
            {
                TAXDTL_DATA = dbms.DoGetDataSQL<TRACK_TAXDTL>(@$"SELECT 
                                                         ROW_NUMBER() OVER (ORDER BY MAX(T.CRT) DESC) AS RowNumber,
                                                         T.Taxid,
                                                         MAX(T.Inno) AS Inno,
                                                         MAX(T.Inty) AS Inty,
                                                         MAX(T.Inp) AS Inp,
                                                         MAX(T.Ins) AS Ins,
                                                         COUNT(*) AS LineCount,
                                                         T.UID,
                                                         T.RefrenceNumber,
                                                         T.TheConfirmationReferenceId,
                                                         T.SentTaxMemory,
                                                         MAX(T.CRT) AS CRT,
                                                         MAX(CAST(T.TheSuccess AS INT)) AS TheSuccess,
                                                         T.TheStatus,
                                                         MAX(T.DATE_N) AS DATE_N,
                                                         MAX(CAST(T.ApiTypeSent AS INT)) AS ApiTypeSent,
                                                         MAX(T.TheError) AS TheError,
                                                         MAX(H.CUST_NO) AS CUST_NO,
                                                         MAX(H.MOLAH) AS MOLAH,
                                                         MAX(H.SHARAYET) AS SHARAYET
                                                     FROM 
                                                         dbo.TAXDTL T
                                                     LEFT OUTER JOIN dbo.HEAD_LST H ON 
                                                         CASE 
                                                             WHEN ISNUMERIC(RIGHT(T.Inno, 6)) = 1 
                                                             THEN CAST(RIGHT(T.Inno, 6) AS float) 
                                                             ELSE NULL 
                                                         END = H.NUMBER 
                                                         AND H.TAG = 2
                                                     WHERE 
                                                         T.ApiTypeSent = {_apitypesent}
                                                     GROUP BY 
                                                         T.Taxid, T.TheStatus, T.UID, T.RefrenceNumber, T.TheConfirmationReferenceId, T.SentTaxMemory
                                                     ORDER BY 
                                                         CRT DESC;").ToList();
            }
            foreach (var item in TAXDTL_DATA)
            {


                item.Inno = Convert.ToString(item.Inno)?.TrimStart('0');
                item.PersianCRT = Functions.ConvertToPersianDate((DateTime)item.CRT);
            }

            if (IsTextSearchCorrected) //فیلتر بر اساس جستجو کاربر
            {
                FilterFreshDataGrid(TAXDTL_DATA);
            }
            else // همه
            {
                INVOCIE_DTGR.ItemsSource = TAXDTL_DATA;
            }
        }
        private void FilterFreshDataGrid(IEnumerable<TRACK_TAXDTL> data)
        {
            INVOCIE_DTGR.ItemsSource = null; // Unbind data
            INVOCIE_DTGR.ItemsSource = ApplyFilter(TAXDTL_DATA, SEARCHBOX.Text.Trim().ToLower()); // Update filtered data
        }
        private IEnumerable<TRACK_TAXDTL> ApplyFilter(IEnumerable<TRACK_TAXDTL> data, string searchText)
        {
            return data.Where(item =>
                (item.ROWNUMBER?.ToString().ToLower().Contains(searchText) ?? false) ||
                (item.Taxid?.ToLower().Contains(searchText) ?? false) ||
                (item.IDD?.ToString().ToLower().Contains(searchText) ?? false) ||
                (item.TheStatus?.ToLower().Contains(searchText) ?? false) ||
                (item.Inno?.ToLower().Contains(searchText) ?? false) ||
                (item.RefrenceNumber?.ToLower().Contains(searchText) ?? false) ||
                (item.TheConfirmationReferenceId?.ToLower().Contains(searchText) ?? false) ||
                (item.TheError?.ToLower().Contains(searchText) ?? false) ||
                (item.SentTaxMemory?.ToLower().Contains(searchText) ?? false) ||
                (item.TheSuccess?.ToString().ToLower().Contains(searchText) ?? false));
        }

        private void SEARCH_BUTTON_Click(object sender, RoutedEventArgs e)
        {
            if (IsTextSearchCorrected) //اگر توی متن باکس هم واقعا چیزی هم نوشته شده بود ایندفعه بیا سرچ کن
            {
                FilterFreshDataGrid(ApplyFilter(TAXDTL_DATA, SEARCHBOX.Text.ToLower()));
            }
            else
            {
                RefGetData();
            }
        }
        private void RD_SANDBOX_Checked(object sender, RoutedEventArgs e)
        {
            //if (NowIsReady)
            //{
            //    IsOtherProccessingNow = true;

            //    Properties.Settings.Default.MAINTAX_STATE = false;
            //    Properties.Settings.Default.SANDBOX_STATE = true; //Current Active
            //    Properties.Settings.Default.Save();
            //    ReStartCurrentCoreApp();

            //    IsOtherProccessingNow = false;
            //}
        }
        private void RD_MAINTAX_Checked(object sender, RoutedEventArgs e)
        {
            //if (NowIsReady)
            //{
            //    IsOtherProccessingNow = true;

            //    Properties.Settings.Default.SANDBOX_STATE = false;
            //    Properties.Settings.Default.MAINTAX_STATE = true; //Current Active
            //    Properties.Settings.Default.Save();
            //    ReStartCurrentCoreApp();

            //    IsOtherProccessingNow = false;
            //}
        }
        private void ReStartCurrentCoreApp()
        {
            var currentExecutablePath = Process.GetCurrentProcess().MainModule.FileName;
            Process.Start(currentExecutablePath);
            Application.Current.Shutdown();
            Environment.Exit(0);
        }
        private void ESTELAM_BTN_Click(object sender, RoutedEventArgs e)
        {
            IsOtherProccessingNow = true;

            try
            {
                // 1) ورودی
                var referenceCode = UID_TXB?.Text?.Trim();
                if (string.IsNullOrWhiteSpace(referenceCode))
                {
                    new Msgwin(false, "کد رهگیری (UID/Reference) وارد نشده است.").ShowDialog();
                    return;
                }

                // 2) بارگذاری تنظیمات سازمان/کلید
                var org = dbms.DoGetDataSQL<SAZMAN>("SELECT MEMORYID, MEMORYIDsand, PRIVIATEKEY, Dcertificate FROM dbo.SAZMAN").FirstOrDefault();

                if (org is null || string.IsNullOrWhiteSpace(org.PRIVIATEKEY))
                {
                    new Msgwin(false, "تنظیمات امضا/سازمان ناقص است.").ShowDialog();
                    return;
                }

                var privateKey = CleanPrivateKey(org.PRIVIATEKEY);
                var memoryTax = (TaxURL == "https://tp.tax.gov.ir/req/api/")
                                ? org.MEMORYID?.Trim()
                                : org.MEMORYIDsand?.Trim();

                if (string.IsNullOrWhiteSpace(memoryTax))
                {
                    new Msgwin(false, "شناسه یکتای حافظه مالیاتی (FiscalId) تنظیم نشده است.").ShowDialog();
                    return;
                }

                // 3) آماده‌سازی سرویس
                TaxApiService.Instance.Init(
                    memoryTax,
                    new SignatoryConfig(privateKey, null),
                    new NormalProperties(ClientType.SELF_TSP),
                    TaxURL
                );

                // اختیاری اما مفید برای Warm-up و بررسی دسترسی:
                _ = TaxApiService.Instance.TaxApis.GetServerInformation();
                _ = TaxApiService.Instance.TaxApis.RequestToken();

                //استعلام صورت حساب ها بر اساس تاریخ
                ////var inquiryResultModels = TaxApiService.Instance.TaxApis.InquiryByTime("14040623");
                ////var inquiryResultModels2 = TaxApiService.Instance.TaxApis.InquiryByTimeRange("14040630", "14040630");

                // 4) استعلام اولیه بر اساس ReferenceId
                var byRefResults = TaxApiService.Instance.TaxApis.InquiryByReferenceId(new List<string> { referenceCode });
                var result = byRefResults?.FirstOrDefault();

                if (result is null)
                {
                    new Msgwin(false, "پاسخی از سامانه دریافت نشد. کمی بعد دوباره تلاش کنید.").ShowDialog();
                    return;
                }

                var status = (result.Status ?? string.Empty).Trim().ToUpperInvariant();

                // 5) سوییچ وضعیت
                switch (status)
                {
                    case "IN_PROGRESS":
                        new Msgwin(false,
                            @"وضعیت این صورتحساب «در حال انجام» (IN_PROGRESS) است. ممکن است در کارپوشه اضافه شده باشد اما هنوز نهایی نشده؛ در صورت نیاز، با شماره مالیاتی در کارپوشه جستجو کنید.")
                            .ShowDialog();
                        break;

                    case "PENDING":
                        new Msgwin(false, @"این صورتحساب هنوز «در انتظار» (PENDING) است. لطفاً بعداً دوباره بررسی کنید.")
                            .ShowDialog();
                        break;

                    case "SUCCESS":
                        new Msgwin(false, @"صورت‌حساب با موفقیت در سامانه ثبت شده است.").ShowDialog();
                        break;

                    case "FAILED":
                        ShowFailedErrors(result);
                        break;

                    case "NOT_FOUND":
                        {
                            // --- بهبود مهم: استعلام مجدد با UID + FiscalId در صورت امکان ---
                            var rechecked = false;
                            if (INVOCIE_DTGR.SelectedItem is TRACK_TAXDTL row)
                            {
                                var _UID_REF_ = string.IsNullOrWhiteSpace(row.UID) ? referenceCode : row.UID;

                                var uidAndFiscalId = new UidAndFiscalId(_UID_REF_.Trim(), memoryTax);
                                var byUidResults = TaxApiService.Instance.TaxApis.InquiryByUidAndFiscalId(new List<UidAndFiscalId> { uidAndFiscalId });
                                var r2 = byUidResults?.FirstOrDefault();

                                if (r2 != null)
                                {
                                    rechecked = true;
                                    var status2 = (r2.Status ?? string.Empty).Trim().ToUpperInvariant();

                                    if (status2 == "SUCCESS")
                                    {
                                        new Msgwin(false, @"(استعلام ثانویه) صورت‌حساب شما با موفقیت ثبت شده است.").ShowDialog();
                                        break;
                                    }
                                    else if (status2 == "FAILED")
                                    {
                                        ShowFailedErrors(r2);
                                        break;
                                    }
                                    else if (status2 == "IN_PROGRESS")
                                    {
                                        new Msgwin(false,
                                            @"(استعلام ثانویه) وضعیت «در حال انجام» است. احتمال درج در کارپوشه وجود دارد؛ در صورت نیاز دستی بررسی کنید.")
                                            .ShowDialog();
                                        break;
                                    }
                                    else if (status2 == "PENDING")
                                    {
                                        new Msgwin(false, @"(استعلام ثانویه) هنوز «در انتظار» است؛ بعداً دوباره بررسی کنید.").ShowDialog();
                                        break;
                                    }
                                }
                            }

                            var tail = rechecked ? " (پس از استعلام ثانویه نیز یافت نشد.)" : string.Empty;
                            new Msgwin(false,
                                @"چنین صورت‌حسابی یافت نشد، کد وضعیت: ""NOT_FOUND"". این خطا معمولاً زمانی رخ می‌دهد که کد رهگیری متعلق به حافظه مالیاتی شما نباشد. 
در مواردی به دلیل ترافیک سامانه نیز ممکن است رخ دهد. اگر مطمئنید این کد متعلق به شماست، بعداً مجدداً بررسی کنید." + tail)
                                .ShowDialog();

                            break;
                        }

                    default:
                        new Msgwin(false, $"وضعیت ناشناخته از سامانه: \"{status}\". لطفاً بعداً دوباره بررسی کنید.").ShowDialog();
                        break;
                }
            }
            catch (Exception ex)
            {
                // اگر قبلاً وضعیت‌های قابل‌انتظار را گرفتیم، نیازی به پیام خطای سنگین نیست
                new Msgwin(false, "خطا در انجام عملیات استعلام.").ShowDialog();
                CL_Generaly.DoWritePRGLOG("GETESTELAM_REFCODE_UPDATE : \n", ex);
            }
            finally
            {
                IsOtherProccessingNow = false;
            }
        }
        /// <summary>
        /// حذف هدر/فوتر PEM و تمیزکردن کلید خصوصی
        /// </summary>
        private static string CleanPrivateKey(string pem)
        {
            return pem?
                .Replace("-----BEGIN PRIVATE KEY-----", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("-----END PRIVATE KEY-----", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("\r", string.Empty)
                .Replace("\n", string.Empty)
                .Trim();
        }
        private void ShowFailedErrors(InquiryResultModel result)
        {
            try
            {
                // Data معمولاً یک آبجکت یا رشتهٔ JSON است
                var dataJson = result?.Data?.ToString();
                if (string.IsNullOrWhiteSpace(dataJson))
                {
                    new Msgwin(false, "در حالت FAILED جزئیات خطا از سامانه ارسال نشد.").ShowDialog();
                    return;
                }

                // مدل خودت: TaxModel.InquiryByReferenceIdModel.Root
                var root = JsonConvert.DeserializeObject<TaxModel.InquiryByReferenceIdModel.Root>(dataJson);
                if (root != null) root.status = result!.Status;

                var errors = new List<string>();
                if (root?.error != null)
                {
                    foreach (var e in root.error)
                        errors.Add($"{e.code} | {e.message}");
                }

                var msg = (errors.Count > 0) ? string.Join(", ", errors) : "FAILED بدون شرح خطای قابل‌خواندن";
                new MsgListwin(false, Functions.GetNormilizedMsg(msg)).ShowDialog();
            }
            catch
            {
                new Msgwin(false, "خطا در پردازش جزئیات FAILED.").ShowDialog();
            }
        }

        private void INVOCIE_DTGR_PreviewMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (INVOCIE_DTGR.SelectedItem is not null)
            {
                IsOtherProccessingNow = true;

                var _msgitem = INVOCIE_DTGR.SelectedItem as TRACK_TAXDTL;
                if (!string.IsNullOrEmpty(_msgitem.TheError) && !string.IsNullOrWhiteSpace(_msgitem.TheError) && _msgitem.TheError is not "NULL")
                {
                    new MsgListwin(false, Functions.GetNormilizedMsg(_msgitem.TheError)).ShowDialog();
                }
                //else if ((_msgitem.TheStatus is "SUCCESS") && (_msgitem.Ins is 1)) //اصلی
                else if ((_msgitem.TheStatus is "SUCCESS") && (_msgitem.Ins != 3)) //ابطالی نباشه
                {
                    if (!string.IsNullOrEmpty(_msgitem?.Taxid))
                    {
                        byte _apitypesent = 0;
                        if (TaxURL is "https://tp.tax.gov.ir/req/api/") // اگر روی اصلیه
                            _apitypesent = 1;
                        else
                            _apitypesent = 0;

                        WIN_MODIFYINVOICE WINMODIFY = new WIN_MODIFYINVOICE();
                        WINMODIFY.TaxURL = TaxURL;
                        WINMODIFY.APITYPESENT_PARAM = _apitypesent.ToString();
                        WINMODIFY.TAXID_PARAM = _msgitem.Taxid;
                        WINMODIFY.ShowDialog();
                    }
                }
                IsOtherProccessingNow = false;
            }
        }
        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (INVOCIE_DTGR.SelectedItem is not null)
            {
                IsOtherProccessingNow = true;
                var _msgitem = INVOCIE_DTGR.SelectedItem as TRACK_TAXDTL;

                if (_msgitem?.TheStatus is "PENDING")
                {
                    return;
                }

                //if (_msgitem?.Ins is 1) //اصلی
                if (_msgitem?.Ins != 3)
                {
                    var IsFailed = _msgitem.TheStatus is "FAILED";
                    if (!string.IsNullOrEmpty(_msgitem?.Taxid))
                    {
                        if (IsFailed)
                        {
                            Msgwin MSG0 = new Msgwin(true, $"این صورت حساب {_msgitem.Inno} ناموفق بوده با این حال از ارسال ابطالی/برگشتی/اصلاحی آن اطمینان دارید؟");
                            MSG0.ShowDialog();
                            if (MSG0.DialogResult != true)
                            {
                                return;
                            }
                            else
                            {
                                #region LOG
                                try
                                {
                                    using (var db = new SqlConnection(CL_CCNNMANAGER.CONNECTION_STR))
                                    {
                                        db.Open();

                                        var windowsUser = Environment.UserName; // Windows username

                                        // Get local IPv4 address (skip loopback)
                                        string ipAddress = Dns.GetHostEntry(Dns.GetHostName())
                                            .AddressList
                                            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                                            ?.ToString() ?? "UnknownIP";

                                        // Combine into username field
                                        string username = $"{windowsUser} | {ipAddress}";

                                        string _FRM_ = this.GetType().Name;

                                        var sql = @"
                            INSERT INTO AMALIAT
                                (USERID, USERNAME, ADATE, AMALID)
                            VALUES
                                (@UserId, @Username, GETDATE(), @AmalId)";
                                        var parameters = new
                                        {
                                            UserId = 0,
                                            Username = TruncateString(username, 49),
                                            AmalId = TruncateString("ابطال فاکتور ناموفق", 49)
                                        };
                                        db.Execute(sql, parameters);
                                    }
                                }
                                catch { }
                                #endregion
                            }
                        }

                        byte _apitypesent = 0;
                        if (TaxURL is "https://tp.tax.gov.ir/req/api/") // اگر روی اصلیه
                            _apitypesent = 1;
                        else
                            _apitypesent = 0;

                        WIN_MODIFYINVOICE WINMODIFY = new WIN_MODIFYINVOICE();
                        if (IsFailed)
                        {
                            WINMODIFY.IsSpecialF = true;
                        }
                        WINMODIFY.TaxURL = TaxURL;
                        WINMODIFY.APITYPESENT_PARAM = _apitypesent.ToString();
                        WINMODIFY.TAXID_PARAM = _msgitem.Taxid;
                        WINMODIFY.ShowDialog();
                    }
                }
                IsOtherProccessingNow = false;
            }
        }

        private void UID_TXB_TextChanged(object sender, TextChangedEventArgs e)
        {
            IsOtherProccessingNow = true;
        }

        private void UID_TXB_PreviewLostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            IsOtherProccessingNow = false;
        }

        #region EXCELY
        private void ExportToExcelBtn_Click(object sender, RoutedEventArgs e)
        {
            ExportAllRowDetail();
        }
        private void ExportAllRowDetail()
        {
            Msgwin msgwin = new Msgwin(true, "آیا میخواهید خروجی اکسل بگیرید ؟");
            msgwin.ShowDialog();
            if (msgwin.DialogResult is true)
            {
                try
                {
                    // Create a new Excel workbook and worksheet
                    IWorkbook workbook = new XSSFWorkbook();
                    ISheet worksheet = workbook.CreateSheet("InvoiceData");

                    // Create a header row
                    IRow headerRow = worksheet.CreateRow(0);

                    // Add headers to the worksheet
                    //for (int i = 0; i < INVOCIE_DTGR.Columns.Count; i++)
                    //{
                    //    headerRow.CreateCell(i).SetCellValue(INVOCIE_DTGR.Columns[i].Header.ToString());
                    //}

                    headerRow.CreateCell(0).SetCellValue("ردیف");
                    headerRow.CreateCell(1).SetCellValue("شماره فاکتور منحصر به فرد مالیاتی");
                    headerRow.CreateCell(2).SetCellValue("شماره فاکتور داخلی");
                    headerRow.CreateCell(3).SetCellValue("نوع صورت حساب");
                    headerRow.CreateCell(4).SetCellValue("الگوی صورتحساب");
                    headerRow.CreateCell(5).SetCellValue("موضوع صورت حساب");
                    headerRow.CreateCell(6).SetCellValue("شناسه");
                    headerRow.CreateCell(7).SetCellValue("وضعیت");
                    headerRow.CreateCell(8).SetCellValue("ارسال شده به سرور");


                    headerRow.CreateCell(9).SetCellValue("شناسه صورت حساب (کد رهگیری)");
                    headerRow.CreateCell(10).SetCellValue("شناسه تایید");
                    headerRow.CreateCell(11).SetCellValue("پیغام های خطا");
                    headerRow.CreateCell(12).SetCellValue("موفقیت در ارسال");
                    headerRow.CreateCell(13).SetCellValue("ارسال شده با حافظه مالیاتی");
                    headerRow.CreateCell(14).SetCellValue("تاریخ ارسال به میلادی");
                    headerRow.CreateCell(15).SetCellValue("تاریخ ارسال به شمسی");

                    headerRow.CreateCell(16).SetCellValue("تعداد سطر فاکتور");


                    // Add data from the DataGrid to the worksheet
                    for (int row = 0; row < INVOCIE_DTGR.Items.Count; row++)
                    {
                        IRow dataRow = worksheet.CreateRow(row + 1);
                        var item = (TRACK_TAXDTL)INVOCIE_DTGR.Items[row];

                        dataRow.CreateCell(0).SetCellValue(item.ROWNUMBER.ToString());
                        dataRow.CreateCell(1).SetCellValue(item.Taxid);
                        dataRow.CreateCell(2).SetCellValue(item.Inno);

                        string intyText = item.Inty switch
                        {
                            1 => "نوع اول",
                            2 => "نوع دوم",
                            3 => "نوع سوم",
                            _ => ""
                        };
                        dataRow.CreateCell(3).SetCellValue(intyText);

                        string inpText = item.Inp switch
                        {
                            1 => "فروش",
                            2 => "فروش ارزی",
                            3 => "صورت حساب طلا , جواهر و پلاتین",
                            4 => "پیمانکاری",
                            5 => "قبوض خدماتی",
                            6 => "هواپیما",
                            _ => ""
                        };
                        dataRow.CreateCell(4).SetCellValue(inpText);

                        string insText = item.Ins switch
                        {
                            1 => "اصلی (فروش)",
                            2 => "اصلاحی",
                            3 => "ابطالی",
                            4 => "برگشت فروش",
                            _ => ""
                        };
                        dataRow.CreateCell(5).SetCellValue(insText);

                        dataRow.CreateCell(6).SetCellValue(item.IDD.ToString());

                        string theStatusText = item.TheStatus switch
                        {
                            "FAILED" => "ناموفق",
                            "PENDING" => "در انتظار",
                            "SUCCESS" => "موفق",
                            "EXPIRED" => "منقضی شده",
                            _ => ""
                        };
                        dataRow.CreateCell(7).SetCellValue(theStatusText);

                        string apiTypeSentText = item.ApiTypeSent switch
                        {
                            false => "آزمایشی",
                            true => "اصلی",
                            _ => ""
                        };
                        dataRow.CreateCell(8).SetCellValue(apiTypeSentText);

                        dataRow.CreateCell(9).SetCellValue(item.RefrenceNumber);
                        dataRow.CreateCell(10).SetCellValue(item.TheConfirmationReferenceId);
                        dataRow.CreateCell(11).SetCellValue(item.TheError);
                        dataRow.CreateCell(12).SetCellValue(item.TheSuccess.ToString());
                        dataRow.CreateCell(13).SetCellValue(item.SentTaxMemory);
                        dataRow.CreateCell(14).SetCellValue(item.CRT.ToString());
                        dataRow.CreateCell(15).SetCellValue(item.PersianCRT);
                        dataRow.CreateCell(16).SetCellValue((double)item.LineCount);
                    }

                    // Generate a file name with date and time
                    string fileName = DateTime.Now.ToString("yyyyMMddHHmmss") + "_" + new Random().Next(1000) + ".xlsx";

                    // Get the user's Documents folder or another preferred location
                    string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                    // Combine the folder path and file name
                    string filePath = Path.Combine(documentsPath, fileName);

                    // Save the workbook to the file
                    using (FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                    {
                        workbook.Write(fs);
                    }
                    new Msgwin(false, $"خروجی اکسل در این مسیر : {filePath} ساخته شد.").ShowDialog();

                    try
                    {
                        Process.Start(new ProcessStartInfo { FileName = filePath, UseShellExecute = true });
                        workbook?.Dispose();
                    }
                    catch { }

                }
                catch (Exception ex)
                {
                    new Msgwin(false, $"خطا در انجام عملیات").ShowDialog();
                }
            }
        }

        private void ExportToExcelBtnGroup_Click(object sender, RoutedEventArgs e)
        {
            EportAsGroupyExcelRow();
        }
        private void EportAsGroupyExcelRow()
        {
            Msgwin msgwin = new Msgwin(true, "آیا میخواهید خروجی اکسل بگیرید ؟");
            msgwin.ShowDialog();
            if (msgwin.DialogResult is true)
            {
                try
                {
                    // Create a new Excel workbook and worksheet
                    IWorkbook workbook = new XSSFWorkbook();
                    ISheet worksheet = workbook.CreateSheet("InvoiceData");

                    // Create a header row
                    IRow headerRow = worksheet.CreateRow(0);

                    headerRow.CreateCell(0).SetCellValue("شماره فاکتور");
                    headerRow.CreateCell(1).SetCellValue("شماره فاکتور داخلی");
                    headerRow.CreateCell(2).SetCellValue("نوع صورتحساب");
                    headerRow.CreateCell(3).SetCellValue("الگوی صورتحساب");
                    headerRow.CreateCell(4).SetCellValue("موضوع صورتحساب");
                    headerRow.CreateCell(5).SetCellValue("شناسه");
                    headerRow.CreateCell(6).SetCellValue("وضعیت");
                    headerRow.CreateCell(7).SetCellValue("ارسال شده به سرور");
                    headerRow.CreateCell(8).SetCellValue("موفقیت");
                    headerRow.CreateCell(9).SetCellValue("تاریخ ارسال");

                    int currentRow = 1;

                    // Group invoices by Taxid
                    var groupedInvoices = INVOCIE_DTGR.Items.Cast<TRACK_TAXDTL>()
                        .GroupBy(i => i.Taxid)
                        .ToList();

                    foreach (var invoiceGroup in groupedInvoices)
                    {
                        // Master Row (Summary of Invoice)
                        IRow masterRow = worksheet.CreateRow(currentRow);

                        var firstInvoice = invoiceGroup.First();
                        masterRow.CreateCell(0).SetCellValue(firstInvoice.Taxid);
                        masterRow.CreateCell(1).SetCellValue(firstInvoice.Inno);
                        masterRow.CreateCell(2).SetCellValue(GetIntyText(firstInvoice.Inty));
                        masterRow.CreateCell(3).SetCellValue(GetInpText(firstInvoice.Inp));
                        masterRow.CreateCell(4).SetCellValue(GetInsText(firstInvoice.Ins));
                        masterRow.CreateCell(5).SetCellValue(firstInvoice.IDD.ToString());
                        masterRow.CreateCell(6).SetCellValue(GetTheStatusText(firstInvoice.TheStatus));
                        masterRow.CreateCell(7).SetCellValue(firstInvoice.ApiTypeSent.ToString());
                        masterRow.CreateCell(8).SetCellValue(firstInvoice.TheSuccess.ToString());
                        masterRow.CreateCell(9).SetCellValue(firstInvoice.CRT.ToString());

                        // Detail Rows (Details of Items)
                        foreach (var invoice in invoiceGroup)
                        {
                            currentRow++;
                            IRow detailRow = worksheet.CreateRow(currentRow);

                            // Shift detail cells by one to visually represent hierarchy
                            detailRow.CreateCell(1).SetCellValue(invoice.Inno);
                            detailRow.CreateCell(2).SetCellValue(invoice.RefrenceNumber);
                            detailRow.CreateCell(3).SetCellValue(invoice.TheConfirmationReferenceId);
                            detailRow.CreateCell(4).SetCellValue(invoice.TheError);
                            detailRow.CreateCell(5).SetCellValue(invoice.TheSuccess.ToString());
                            detailRow.CreateCell(6).SetCellValue(invoice.SentTaxMemory);
                            detailRow.CreateCell(7).SetCellValue(invoice.CRT.ToString());
                        }

                        // Grouping Detail Rows under the Master Row
                        worksheet.GroupRow(currentRow - invoiceGroup.Count() + 1, currentRow);
                        worksheet.SetRowGroupCollapsed(currentRow - invoiceGroup.Count() + 1, true);

                        // Move to the next row for the next invoice group
                        currentRow++;
                    }

                    // Generate a file name with date and time
                    string fileName = DateTime.Now.ToString("yyyyMMddHHmmss") + "_" + new Random().Next(1000) + ".xlsx";

                    // Get the user's Documents folder or another preferred location
                    string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                    // Combine the folder path and file name
                    string filePath = Path.Combine(documentsPath, fileName);

                    // Save the workbook to the file
                    using (FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                    {
                        workbook.Write(fs);
                    }

                    new Msgwin(false, $"خروجی اکسل در این مسیر : {filePath} ساخته شد.").ShowDialog();

                    try
                    {
                        Process.Start(new ProcessStartInfo { FileName = filePath, UseShellExecute = true });
                        workbook?.Dispose();
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    new Msgwin(false, $"خطا در انجام عملیات").ShowDialog();
                }
            }
        }
        private string GetIntyText(int? inty)
        {
            return inty switch
            {
                1 => "نوع اول",
                2 => "نوع دوم",
                3 => "نوع سوم",
                _ => ""
            };
        }
        private string GetInpText(int? inp)
        {
            return inp switch
            {
                1 => "فروش",
                2 => "فروش ارزی",
                3 => "صورت حساب طلا , جواهر و پلاتین",
                4 => "پیمانکاری",
                5 => "قبوض خدماتی",
                6 => "هواپیما",
                _ => ""
            };
        }
        private string GetInsText(int? ins)
        {
            return ins switch
            {
                1 => "اصلی (فروش)",
                2 => "اصلاحی",
                3 => "ابطالی",
                4 => "برگشت فروش",
                _ => ""
            };
        }
        private string GetTheStatusText(string theStatus)
        {
            return theStatus switch
            {
                "FAILED" => "ناموفق",
                "PENDING" => "در انتظار",
                "SUCCESS" => "موفق",
                "EXPIRED" => "منقضی شده",
                _ => ""
            };
        }
        #endregion


        private void CopyData_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (INVOCIE_DTGR.SelectedItems.Count > 0)
                {
                    DataGridClipboardCopyMode copyMode = INVOCIE_DTGR.ClipboardCopyMode;
                    INVOCIE_DTGR.ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader;
                    ApplicationCommands.Copy.Execute(null, INVOCIE_DTGR);
                    INVOCIE_DTGR.ClipboardCopyMode = copyMode;
                }
            }
            catch { }
        }
        private void ExportToXml_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (INVOCIE_DTGR.SelectedItems.Count > 0)
                {
                    var selectedItems = new List<TRACK_TAXDTL>(); // Replace YourDataType with the actual type of your data items.
                    foreach (var selectedItem in INVOCIE_DTGR.SelectedItems)
                    {
                        selectedItems.Add((TRACK_TAXDTL)selectedItem);
                    }

                    string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                    string randomSuffix = new Random().Next(1000, 9999).ToString(); // Generate a random 4-digit number.


                    string fileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), $"data_{timestamp}_{randomSuffix}.xml");

                    var serializer = new XmlSerializer(typeof(List<TRACK_TAXDTL>));
                    using (var writer = new StreamWriter(fileName))
                    {
                        serializer.Serialize(writer, selectedItems);
                    }

                    try { Process.Start(new ProcessStartInfo { FileName = fileName, UseShellExecute = true }); }
                    catch { }

                }
            }
            catch (Exception)
            {
                new Msgwin(false, $"خطا در انجام عملیات").ShowDialog();
            }
        }
        private void ExportToJson_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (INVOCIE_DTGR.SelectedItems.Count > 0)
                {
                    var selectedItems = new List<TRACK_TAXDTL>();
                    foreach (var selectedItem in INVOCIE_DTGR.SelectedItems)
                    {
                        selectedItems.Add((TRACK_TAXDTL)selectedItem);
                    }

                    string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                    string randomSuffix = new Random().Next(1000, 9999).ToString();

                    string fileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), $"data_{timestamp}_{randomSuffix}.json");

                    var json = JsonConvert.SerializeObject(selectedItems);
                    File.WriteAllText(fileName, json);

                    try { Process.Start(new ProcessStartInfo { FileName = fileName, UseShellExecute = true }); }
                    catch { }
                }
            }
            catch (Exception)
            {
                new Msgwin(false, $"خطا در انجام عملیات").ShowDialog();
            }
        }

        private void TAXId_TextBox_PreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            TAXId_TextBox.SelectAll();
        }

        private void RD_LAST30_Click(object sender, RoutedEventArgs e)
        {
            RefGetData();
        }

        private void RD_ALL_TIME_Click(object sender, RoutedEventArgs e)
        {
            RefGetData();
        }

        private void LBL_MOADINA_SCNUM_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            MOADINA_SCNUM.IsReadOnly = false;
        }

        private void BTN_SAVE_MOADINA_SCNUM_Click(object sender, RoutedEventArgs e)
        {
            MOADINA_SCNUM.Text = MOADINA_SCNUM.Text.Trim();

            var MCNUM = MOADINA_SCNUM.Text;

            if (string.IsNullOrEmpty(MCNUM) || string.IsNullOrWhiteSpace(MCNUM))
            {
                MOADINA_SCNUM.Text = MOADINA_SCNUM.Tag.ToString();

                new Msgwin(false, "مقدار شماره شروع فاکتور نمیتواند خالی باشد").ShowDialog(); return;
            }
            else if (!decimal.TryParse(MCNUM, out _))
            {
                MOADINA_SCNUM.Text = MOADINA_SCNUM.Tag.ToString();

                new Msgwin(false, "مقدار شماره شروع فاکتور حتما باید از نوع عددی باشد").ShowDialog(); return;
            }
            else if (Convert.ToDecimal(MCNUM) <= 0)
            {
                MOADINA_SCNUM.Text = MOADINA_SCNUM.Tag.ToString();

                new Msgwin(false, "مقدار شماره شروع فاکتور حتما باید بزرگتر از صفر باشد").ShowDialog(); return;
            }
            else
            {
                dbms.DoExecuteSQL($"UPDATE [dbo].[SAZMAN] SET [MOADINA_SCNUM] = {MCNUM}");
                MOADINA_SCNUM.Tag = MCNUM;
                new Msgwin(false, "مقدار شماره شروع فاکتور ذخیره شد.").ShowDialog();
            }
        }

        #region ReSend
        private string TruncateString(string input, int maxLength)
        {
            if (string.IsNullOrEmpty(input) || maxLength <= 0)
                return string.Empty;

            // Check if the string needs truncation
            if (input.Length > maxLength)
            {
                return input.Substring(0, maxLength);
            }

            // No truncation needed
            return input;
        }
        private void INVOCIE_DTGR_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!NowIsReady || RESEND_BTN == null) return;

            if (INVOCIE_DTGR.SelectedItems.Count <= 0) { return; }

            var selectedItems = INVOCIE_DTGR.SelectedItems.Cast<TRACK_TAXDTL>().ToList();

            if (selectedItems.Count > 0)
            {
                bool allPendingOrInProgress = selectedItems.All(item =>
                    item.TheStatus?.ToUpper() == "PENDING" || item.TheStatus?.ToUpper() == "IN_PROGRESS");

                RESEND_BTN.IsEnabled = allPendingOrInProgress;
            }
            else
            {
                RESEND_BTN.IsEnabled = false;
            }
        }
        private void RESEND_BTN_Click(object sender, RoutedEventArgs e)
        {
            if (!RESEND_BTN.IsEnabled) return;

            var selectedItems = INVOCIE_DTGR.SelectedItems.Cast<TRACK_TAXDTL>().ToList();
            if (!selectedItems.Any())
            {
                new Msgwin(false, "لطفا ابتدا یک یا چند فاکتور را برای ارسال مجدد انتخاب کنید.").ShowDialog();
                return;
            }

            var uniqueTaxids = selectedItems.Select(i => i.Taxid).Distinct().ToList();
            Msgwin msgwin = new Msgwin(true,
                $"⚠️ توجه: تاریخ فاکتورهای ارسالی مجدد، تاریخ امروز در نظر گرفته خواهد شد.\n\n" +
                $"✅ تعداد {uniqueTaxids.Count} فاکتور برای ارسال مجدد انتخاب شده است.\n\n" +
                $"ℹ️ نکته مهم: اگر وضعیت صورتحساب‌های شما همچنان «در انتظار» یا «در حال انجام» است، " +
                $"پیشنهاد می‌شود قبل از ادامه، کارپوشه خود را بررسی کنید و از عدم وجود آن صورتحساب‌ها مطمئن شوید.\n\n" +
                $"آیا از ارسال مجدد اطمینان دارید؟");
            msgwin.Height = msgwin.Height + 50;
            msgwin.ShowDialog();

            if (msgwin.DialogResult != true)
            {
                return;
            }

            #region LOG
            try
            {
                using (var db = new SqlConnection(CL_CCNNMANAGER.CONNECTION_STR))
                {
                    db.Open();

                    var windowsUser = Environment.UserName; // Windows username

                    // Get local IPv4 address (skip loopback)
                    string ipAddress = Dns.GetHostEntry(Dns.GetHostName())
                        .AddressList
                        .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                        ?.ToString() ?? "UnknownIP";

                    // Combine into username field
                    string username = $"{windowsUser} | {ipAddress}";

                    string _FRM_ = this.GetType().Name;

                    var sql = @"
                            INSERT INTO AMALIAT
                                (USERID, USERNAME, ADATE, AMALID)
                            VALUES
                                (@UserId, @Username, GETDATE(), @AmalId)";
                    var parameters = new
                    {
                        UserId = 0,
                        Username = TruncateString(username, 49),
                        AmalId = TruncateString(_FRM_, 49)
                    };
                    db.Execute(sql, parameters);
                }
            }
            catch { }
            #endregion

            STATUS_LABEL.Content = "در حال آماده سازی برای ارسال مجدد...";
            STATUS_LABEL.Visibility = Visibility.Visible;
            IsOtherProccessingNow = true;
            int successCount = 0;
            int failCount = 0;

            try
            {
                var sazmanInfo = dbms.DoGetDataSQL<SAZMAN>("SELECT MEMORYID, MEMORYIDsand, PRIVIATEKEY FROM dbo.SAZMAN").FirstOrDefault();
                if (sazmanInfo == null)
                {
                    throw new Exception("اطلاعات سازمان در دیتابیس یافت نشد.");
                }

                var privateKey = sazmanInfo.PRIVIATEKEY.Replace("-----BEGIN PRIVATE KEY-----\r\n", "").Replace("\r\n-----END PRIVATE KEY-----\r\n", "").Trim();
                string memoryId;
                bool isMainApi;

                if (RD_MAINTAX.IsChecked ?? false)
                {
                    memoryId = sazmanInfo.MEMORYID.Trim();
                    isMainApi = true;
                }
                else
                {
                    memoryId = sazmanInfo.MEMORYIDsand.Trim();
                    isMainApi = false;
                }

                var taxService = new TaxService(memoryId, privateKey, TaxURL);
                taxService.RequestToken();

                foreach (var taxid in uniqueTaxids)
                {
                    try
                    {
                        Dispatcher.Invoke(() => STATUS_LABEL.Content = $"در حال پردازش فاکتور با شماره مالیاتی: {taxid}");

                        // ۱) UID نسخهٔ پایه (اولین ارسالِ غیر ResendDuplicate) را بگیر
                        var baseUid = dbms.DoGetDataSQL<string>(@"
                                                SELECT TOP (1) Uid
                                                FROM dbo.TAXDTL
                                                WHERE Taxid = @taxid AND ApiTypeSent = @api
                                                  AND ISNULL(REMARKS,'') <> 'ResendDuplicate'
                                                ORDER BY CRT ASC
                                            ", new { taxid, api = Convert.ToInt32(isMainApi) }).FirstOrDefault();

                        // اگر به هر دلیل چیزی برنگشت، بیفت روی اولین UID موجود (fallback بی‌خطر)
                        if (string.IsNullOrEmpty(baseUid))
                        {
                            baseUid = dbms.DoGetDataSQL<string>(@"
                                                    SELECT TOP (1) Uid
                                                    FROM dbo.TAXDTL
                                                    WHERE Taxid = @taxid AND ApiTypeSent = @api
                                                    ORDER BY CRT ASC
                                                ", new { taxid, api = Convert.ToInt32(isMainApi) }).FirstOrDefault();
                        }

                        // ۲) کل ردیف‌های همان گروه را بگیر (و ترتیب ثابت بده)
                        var originalInvoiceRows = dbms.DoGetDataSQL<FULL_TAXDTL>(@"
                                                SELECT *
                                                FROM dbo.TAXDTL
                                                WHERE Taxid = @taxid AND ApiTypeSent = @api AND Uid = @baseUid
                                                ORDER BY CRT, IDD
                                            ", new { taxid, api = Convert.ToInt32(isMainApi), baseUid });


                        if (!originalInvoiceRows.Any())
                        {
                            CL_Generaly.DoWritePRGLOG($"Skipping TaxID {taxid} as no records were found in TAXDTL.", null);
                            failCount++;
                            continue;
                        }

                        var header = CreateHeaderFromFullTaxDtl(originalInvoiceRows.First());
                        var bodies = CreateBodyListFromFullTaxDtl((List<FULL_TAXDTL>)originalInvoiceRows);
                        var payments = new List<InvoiceModel.Payment>();

                        #region Cleaning_RestoreValiding
                        if (header is InvoiceModel.Header TheHead)
                        {
                            //Matter {
                            if (TheHead?.Bpn != null) //شماره گذرنامه خریدار
                            {
                                if (string.IsNullOrWhiteSpace(TheHead?.Bpn) || TheHead?.Bpn == "0")
                                {
                                    TheHead.Bpn = null;
                                }
                            }
                            if (TheHead?.Scc != null) //کد گمرک محل اظهار فروشنده
                            {
                                if (string.IsNullOrWhiteSpace(TheHead?.Scc) || TheHead?.Scc == "0")
                                {
                                    TheHead.Scc = null;
                                }
                            }
                            //Matter }

                            if (string.IsNullOrEmpty(TheHead?.Crn) || TheHead?.Crn == "0")
                            {
                                if (TheHead?.Crn != null)
                                {
                                    TheHead.Crn = null; //شناسه یکتای ثبت قرار داد فروشنده
                                }
                            }
                            if (TheHead?.Irtaxid != null) //جلوگیری از مقدار خالی یا Space
                            {
                                if (string.IsNullOrWhiteSpace(TheHead?.Irtaxid))
                                {
                                    if (TheHead?.Inp != 7) //الگوی صورتحساب => صادرات نیست
                                    {
                                        TheHead.Irtaxid = null;
                                    }
                                }
                            }
                        }
                        foreach (var item in bodies)
                        {
                            if (item?.Cut != null) //نوع ارز
                            {
                                if (string.IsNullOrWhiteSpace(item?.Cut))
                                {
                                    item.Cut = null;
                                }
                            }
                            if (item?.Cfee == null)
                            {
                                item.Cfee = 0; //میزان ارز
                            }
                            if (string.IsNullOrWhiteSpace(item?.Odt))
                            {
                                item.Odt = "0"; //موضوع سایر مالیات و عوارض
                            }
                            if (item?.Odr == null)
                            {
                                item.Odr = 0; //نرخ سایر مالیات و عوارض
                            }
                            if (item?.Odam == null)
                            {
                                item.Odam = 0; //مبلغ سایر مالیات و عوارض
                            }
                            // --- سایر وجوه قانونی ---
                            if (string.IsNullOrWhiteSpace(item?.Olt))
                            {
                                item.Olt = "0";  //موضوع سایر وجوه قانونی
                            }
                            if (item?.Olr == null)
                            {
                                item.Olr = 0; //نرخ سایر وجوه قانونی
                            }
                            if (item?.Olam == null)
                            {
                                item.Olam = 0; //مبلغ سایر وجوه قانونی
                            }
                            // --- هزینه‌ها و سود ---
                            if (item?.Consfee == null)
                            {
                                item.Consfee = 0; //اجرت ساخت
                            }
                            if (item?.Spro == null)
                            {
                                item.Spro = 0; ////سود فروشنده
                            }
                            if (item?.Bros == null)
                            {
                                item.Bros = 0; //حق العمل
                            }
                            if (item?.Tcpbs == null)
                            {
                                item.Tcpbs = 0; //جمع کل اجرت , حق العمل و سود
                            }
                            if (item?.Cop == null)
                            {
                                item.Cop = 0; //سهم نقدی از پرداخت
                            }
                            if (item?.Vop == null) //سهم ارزش افزوده از پرداخت
                            {
                                item.Vop = 0;
                            }
                            if (string.IsNullOrWhiteSpace(item?.Bsrn))
                            {
                                item.Bsrn = null; //شناسه یکتای ثبت قرارداد حق العملکاری
                            }
                            if (item?.Nw == null || item.Nw == 0)
                            {
                                item.Nw = 0; //وزن خالص
                            }
                            if (item?.Ssrv == null || item.Ssrv == 0)
                            {
                                item.Ssrv = 0;  //ارزش ریالی کالا
                            }
                            if (item?.Sscv == null || item.Sscv == 0)
                            {
                                item.Sscv = 0; //ارزش ارزی کالا
                            }
                        }
                        #endregion

                        #region JSON_LOG
                        try
                        {
                            string directoryPath = @"C:\CORRECT\SENTS";
                            if (!Directory.Exists(directoryPath))
                            {
                                Directory.CreateDirectory(directoryPath);
                            }
                            // Create a custom JSON object to hold header, bodies, and payments
                            var jsonObject = new
                            {
                                Header = header,
                                Bodies = bodies,
                                Payments = payments
                            };

                            // Serialize the custom JSON object to JSON
                            string jsonData = System.Text.Json.JsonSerializer.Serialize(jsonObject);
                            // Define the file path for the combined JSON file
                            string combinedFilePath = Path.Combine(directoryPath, $"{DateTime.Now.ToString("yyyy-mm-dd-ss-fff")}-Combined.json");

                            File.WriteAllText(combinedFilePath, jsonData);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Error: " + ex.Message);
                        }
                        #endregion

                        var sendResult = taxService.SendInvoices(header, bodies, payments);

                        foreach (var originalRow in originalInvoiceRows)
                        {
                            var newLogRow = (FULL_TAXDTL)originalRow.Clone();
                            newLogRow.IDD = Functions.GetNewIDD();
                            newLogRow.RefrenceNumber = sendResult.ReferenceNumber;
                            newLogRow.UID = sendResult.Uid;
                            newLogRow.CRT = DateTime.Now;
                            newLogRow.TheStatus = "PENDING";
                            newLogRow.ApiTypeSent = isMainApi;
                            newLogRow.TheError = null;
                            newLogRow.TheConfirmationReferenceId = null;
                            newLogRow.TheSuccess = false;
                            newLogRow.REMARKS = "ResendDuplicate";

                            InsertNewTaxDtlRecord(newLogRow);
                        }
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        CL_Generaly.DoWritePRGLOG($"Failed to resend invoice with TaxID {taxid}", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                new Msgwin(false, $"خطای کلی در فرآیند ارسال مجدد: {ex.Message}").ShowDialog();
                CL_Generaly.DoWritePRGLOG("General error in ResendButton_Click", ex);
            }
            finally
            {
                STATUS_LABEL.Visibility = Visibility.Hidden;
                IsOtherProccessingNow = false;
                RefGetData();
                new Msgwin(false, $"عملیات ارسال مجدد تکمیل شد.\nموفق: {successCount}\nناموفق: {failCount}").ShowDialog();
            }
        }

        private void InsertNewTaxDtlRecord(FULL_TAXDTL src_item)
        {
            const string insertSql = @"INSERT INTO dbo.TAXDTL (
            Taxid, Indatim_Sec, Indati2m_Sec, Inty, Inno, Irtaxid, Inp, Ins, Tins, Tob, Bid, Tinb, Sbc, Bpc, Ft, Bpn, Scln, Scc, Crn, Billid, Tprdis, Tdis, Tadis, Tvam, Todam, Tbill, Setm, Cap, Insp, Tvop, Tax17, Cdcd, Tonw, Torv, Tocv, Sstid, Sstt, Mu, Am, Fee, Cfee, Cut, Exr, Prdis, Dis, Adis, Vra, Vam, Odt, Odr, Odam, Olt, Olr, Olam, Consfee, Spro, Bros, Tcpbs, Cop, Vop, Bsrn, Tsstam, Nw, Ssrv, Sscv, IDD, UID, RefrenceNumber, TheStatus, ApiTypeSent, SentTaxMemory,DATE_N, REMARKS, CRT)
            VALUES (@Taxid, @Indatim_Sec, @Indati2m_Sec, @Inty, @Inno, @Irtaxid, @Inp, @Ins, @Tins, @Tob, @Bid, @Tinb, @Sbc, @Bpc, @Ft, @Bpn, @Scln, @Scc, @Crn, @Billid, @Tprdis, @Tdis, @Tadis, @Tvam, @Todam, @Tbill, @Setm, @Cap, @Insp, @Tvop, @Tax17, @Cdcd, @Tonw, @Torv, @Tocv, @Sstid, @Sstt, @Mu, @Am, @Fee, @Cfee, @Cut, @Exr, @Prdis, @Dis, @Adis, @Vra, @Vam, @Odt, @Odr, @Odam, @Olt, @Olr, @Olam, @Consfee, @Spro, @Bros, @Tcpbs, @Cop, @Vop, @Bsrn, @Tsstam, @Nw, @Ssrv, @Sscv, @IDD, @UID, @RefrenceNumber, @TheStatus, @ApiTypeSent, @SentTaxMemory, @DATE_N , @REMARKS ,@CRT);";

            var p = new
            {
                src_item.Taxid,
                src_item.Indatim_Sec,
                src_item.Indati2m_Sec,
                src_item.Inty,
                src_item.Inno,
                src_item.Irtaxid,
                src_item.Inp,
                src_item.Ins,
                src_item.Tins,
                src_item.Tob,
                src_item.Bid,
                src_item.Tinb,
                src_item.Sbc,
                src_item.Bpc,
                src_item.Ft,
                src_item.Bpn,
                src_item.Scln,
                src_item.Scc,
                src_item.Crn,
                src_item.Billid,
                src_item.Tprdis,
                src_item.Tdis,
                src_item.Tadis,
                src_item.Tvam,
                src_item.Todam,
                src_item.Tbill,
                src_item.Setm,
                src_item.Cap,
                src_item.Insp,
                src_item.Tvop,
                src_item.Tax17,
                src_item.Cdcd,
                src_item.Tonw,
                src_item.Torv,
                src_item.Tocv,
                src_item.Sstid,
                src_item.Sstt,
                src_item.Mu,
                src_item.Am,
                src_item.Fee,
                src_item.Cfee,
                src_item.Cut,
                src_item.Exr,
                src_item.Prdis,
                src_item.Dis,
                src_item.Adis,
                src_item.Vra,
                src_item.Vam,
                src_item.Odt,
                src_item.Odr,
                src_item.Odam,
                src_item.Olt,
                src_item.Olr,
                src_item.Olam,
                src_item.Consfee,
                src_item.Spro,
                src_item.Bros,
                src_item.Tcpbs,
                src_item.Cop,
                src_item.Vop,
                src_item.Bsrn,
                src_item.Tsstam,
                src_item.Nw,
                src_item.Ssrv,
                src_item.Sscv,
                src_item.IDD,
                src_item.UID,
                src_item.RefrenceNumber,
                src_item.TheStatus,
                src_item.ApiTypeSent,
                src_item.SentTaxMemory,
                src_item.DATE_N,
                src_item.REMARKS,
                src_item.CRT
            };
            dbms.DoExecuteSQL(insertSql, p);
        }
        private long GetCurrentDateLong()
        {
            var iranTZ = TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time");
            var DtNowBase = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow + TokenLifeTime.ServerClockSkew, iranTZ);
            return TaxService.ConvertDateToLong(DtNowBase);
        }
        public InvoiceModel.Header CreateHeaderFromFullTaxDtl(FULL_TAXDTL row)
        {
            var h = new InvoiceModel.Header();

            h.Taxid = row.Taxid; //شماره منحصر به فرد مالیاتی
            h.Indatim = (long)row.Indatim_Sec; //تاریخ و زمان صدور صورت حساب - میلادی
            h.Indati2m = (long)row.Indati2m_Sec; //تاریخ و زمان ایجاد صورتحساب - میلادی

            if (row.Inty != null) h.Inty = row.Inty.Value; //نوع صورت حساب // نوع اول , دوم , سوم
            if (row.Inno != null) h.Inno = row.Inno; // سریال صورت حساب
            if (row.Irtaxid != null) h.Irtaxid = row.Irtaxid; //شماره منحصر به فرد مالیاتی صورتحساب مرجع - برای اصلاح , ابطال , برگشت
            if (row.Inp != null) h.Inp = row.Inp.Value; //الگوی صورتحساب // الگوی:1 فروش الگوی:2 فروش ارزی الگوی:3 صورتحساب طلا، جواهر و پالتین
            if (row.Ins != null) h.Ins = row.Ins.Value; //موضوع صورتحساب //ابطالی , اصلاحی 
            if (row.Tins != null) h.Tins = row.Tins;/*"10840014242"*/ //WAS "MCODE" BEFORE //شماره اقتصادی فروشنده //توی نرم افزار شماره ثبت هم در درباره تهیه کنندگان زده
            if (row.Tob != null) h.Tob = row.Tob.Value; //نوع شخص خریدار
            if (row.Bid != null) h.Bid = row.Bid;/*item.MCODEM*/ //شناسه ملی/ شماره ملی/ شناسه مشارکت مدنی/ کد فراگیر اتباع غیر ایرانی خریدار
            if (row.Tinb != null) h.Tinb = row.Tinb;/*item.MCODEM*/ // شماره اقتصادی خریدار
            if (row.Sbc != null) h.Sbc = row.Sbc; //کد شعبه فروشنده
            if (!string.IsNullOrEmpty(row.Bbc)) h.Bbc = row.Bbc; //کد شعبه خریدار
            if (!string.IsNullOrEmpty(row.Bpc)) h.Bpc = row.Bpc; //کد پستی خریدار
            if (row.Ft != null) h.Ft = row.Ft.Value; //نوع پرواز
            if (row.Bpn != null) h.Bpn = row.Bpn; //شماره گذرنامه خریدار
            if (row.Scln != null) h.Scln = row.Scln; //شماره پروانه گمرکی
            if (row.Scc != null) h.Scc = row.Scc; //کد گمرک محل اظهار فروشنده
            if (row.Crn != null) h.Crn = row.Crn; //شناسه یکتای ثبت قرار داد فروشنده
            if (row.Billid != null) h.Billid = row.Billid; //شماره اشتراک/ شناسه قبض بهرهبردار
            if (row.Tprdis != null) h.Tprdis = row.Tprdis.Value; //مجموع مبلغ قبل از کسر تخفیف
            if (row.Tdis != null) h.Tdis = row.Tdis.Value; //مجموع تخفیفات
            if (row.Tadis != null) h.Tadis = row.Tadis.Value; // مجموع مبلغ پس از کسر تخفیف
            if (row.Tvam != null) h.Tvam = row.Tvam.Value; // مجموع مالیات بر ارزش افزوده
            if (row.Todam != null) h.Todam = row.Todam.Value; // مجموع سایر مالیات، عوارض و وجوه قانونی
            if (row.Tbill != null) h.Tbill = row.Tbill.Value; //مجموع صورتحساب
            if (row.Setm != null) h.Setm = (int)row.Setm.Value; //روش تسویه
            if (row.Cap != null) h.Cap = row.Cap.Value; //مبلغ پرداختی نقدی
            if (row.Insp != null) h.Insp = row.Insp.Value; //مبلغ نسیه
            if (row.Tvop != null) h.Tvop = row.Tvop.Value; // مجموع سهم مالیات بر ارزش افزوده از پرداخت
            if (row.Tax17 != null) h.Tax17 = row.Tax17.Value;

            if (row.Cdcd.HasValue) h.Cdcd = row.Cdcd.Value; // تاریخ کوتاژ اظهارنامه گمرکی
            if (row.Tonw.HasValue) h.Tonw = row.Tonw.Value; //مجموع وزن خالص
            if (row.Torv.HasValue) h.Torv = row.Torv.Value; //مجموع ارزش ریالی
            if (row.Tocv.HasValue) h.Tocv = row.Tocv.Value; //مجموع ارزش ارزی

            return h;
        }
        public List<InvoiceModel.Body> CreateBodyListFromFullTaxDtl(List<FULL_TAXDTL> rows)
        {
            var bodies = new List<InvoiceModel.Body>();

            foreach (var row in rows)
            {
                var b = new InvoiceModel.Body();

                if (row.Sstid != null) b.Sstid = row.Sstid; //شناسه کالا/خدمت //CODE	STUF_DEF      
                if (row.Sstt != null) b.Sstt = row.Sstt;  //شرح کالا/خدمت //NAME	STUF_DEF

                if (!string.IsNullOrEmpty(row.Mu))
                    b.Mu = decimal.Truncate(decimal.Parse(row.Mu, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture); //واحد اندازه گیری //VNAMES	TCOD_VAHEDS

                if (row.Am != null) b.Am = row.Am.Value;   //تعداد/مقدار //MEGH	INVO_LST
                if (row.Fee != null) b.Fee = row.Fee.Value;  //مبلغ واحد //MABL	INVO_LST
                b.Cfee = 0; //میزان ارز
                if (row.Prdis != null) b.Prdis = row.Prdis.Value; //مبلغ قبل از تخفیف //MABL_K	INVO_LST
                if (row.Dis != null) b.Dis = row.Dis.Value;  //مبلغ تخفیف //N_MOIN	INVO_LST
                if (row.Adis != null) b.Adis = row.Adis.Value; //مبلغ بعد از تخفیف //Sum(INVO_LST.MABL_K - INVO_LST.N_MOIN AS mabkbt)	INVO_LST
                if (row.Vra != null) b.Vra = row.Vra.Value;  //نرخ مالیات بر ارزش افزوده
                if (row.Vam != null) b.Vam = row.Vam.Value;  //مبلغ مالیات بر ارزش افزوده //IMBAA	 INVO_LST
                b.Odt = "0"; //موضوع سایر مالیات و عوارض
                b.Odr = 0; //نرخ سایر مالیات و عوارض
                b.Odam = 0; //مبلغ سایر مالیات و عوارض
                b.Olt = "0"; //موضوع سایر وجوه قانونی
                b.Olr = 0; //نرخ سایر وجوه قانونی
                b.Olam = 0; //مبلغ سایر وجوه قانونی
                b.Consfee = 0; //اجرت ساخت
                b.Spro = 0; //سود فروشنده
                b.Bros = 0; //حق العمل
                b.Tcpbs = 0; //جمع کل اجرت , حق العمل و سود
                b.Cop = 0; //سهم نقدی از پرداخت
                b.Vop = 0; //سهم ارزش افزوده از پرداخت
                b.Bsrn = null; //شناسه یکتای ثبت قرارداد حق العملکاری
                if (row.Tsstam != null) b.Tsstam = row.Tsstam.Value; //مبلغ کل کالا/خدمت //MABL_K	INVO_LST
                b.Nw = 0; //وزن خالص
                b.Ssrv = 0; //ارزش ریالی کالا
                b.Sscv = 0; //ارزش ارزی کالا

                if (row.Exr.HasValue) b.Exr = row.Exr.Value; //نرخ برابری ارز با ریال

                bodies.Add(b);
            }

            return bodies;
        }
        #endregion

        private void DG_SUB_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            DataGrid dataGrid = sender as DataGrid;
            if (dataGrid == null) return;

            if (dataGrid.SelectedItems.Count > 0)
            {
                return;
            }

            // Find the row under the mouse
            DependencyObject dep = (DependencyObject)e.OriginalSource;
            while (dep != null && !(dep is DataGridRow))
            {
                dep = VisualTreeHelper.GetParent(dep);
            }

            DataGridRow row = dep as DataGridRow;
            if (row != null && row.Item != null && row.Item != CollectionView.NewItemPlaceholder)
            {
                // Select the row under the mouse
                dataGrid.SelectedItem = row.Item;

                // Show the context menu
                dataGrid.ContextMenu.IsOpen = true;

                // Mark the event as handled to prevent the default context menu behavior
                e.Handled = true;
            }
            else
            {
                // No valid row, don't show context menu
                e.Handled = true;
            }
        }

        private void SEARCH_BUTTON1_Click(object sender, RoutedEventArgs e)
        {
            // لیست آیتم‌های قابل‌بایند به DataGrid شما
            var rows = new List<object>();
            int r = 1;

            // کمک‌تابع ساخت سطر
            void AddRow(string code, string text) => rows.Add(new { ROW_U = r++, CODE_U = code, MessageText_U = text });

            // نگاشت وضعیت‌ها به فارسی
            string MapStatus(string? s) => (s ?? "").ToUpperInvariant() switch
            {
                "ACTIVE" => "فعال",
                "INACTIVE" => "غیرفعال",
                "SUSPENDED" => "معلق",
                "BLOCKED" => "مسدود",
                _ => "نامشخص"
            };

            // هندل نال/خالی
            string OrDash(string? s) => string.IsNullOrWhiteSpace(s) ? "——" : s!.Trim();

            // 1) اعتبارسنجی ورودی
            var ecode = EGCODE.Text?.Trim();
            if (string.IsNullOrWhiteSpace(ecode))
            {
                AddRow("", "❗ کد اقتصادی را وارد کنید.");
                new MsgListwin(false, rows, "#FFFF0000").ShowDialog(); // قرمز برای خطا
                return;
            }

            // 2) استعلام اطلاعات کد اقتصادی
            EconomicCodeModel? info = null;
            try
            {
                info = TaxApiService.Instance.TaxApis.GetEconomicCodeInformation(ecode);
            }
            catch (Exception)
            {
                AddRow("", "خطا در برقراری ارتباط با سرویس استعلام کد اقتصادی. لطفاً دوباره تلاش کنید.");
                new MsgListwin(false, rows, "#FFFF0000").ShowDialog();
                return;
            }

            if (info == null || string.IsNullOrWhiteSpace(info.NationalId))
            {
                AddRow("", "نتیجه‌ای یافت نشد. کد اقتصادی نامعتبر است یا در سامانه موجود نیست.");
                new MsgListwin(false, rows, "#FFFF0000").ShowDialog();
                return;
            }

            // 3) نمایش نتایج کاربرپسند برای کد اقتصادی
            //AddRow("", "✅ اطلاعات مؤدی یافت شد:");
            AddRow("نام/عنوان", OrDash(info.NameTrade));
            AddRow("شماره/شناسه اقتصادی", OrDash(info.NationalId));
            AddRow("وضعیت مؤدی", MapStatus(info.TaxpayerStatus));
            AddRow("نوع مؤدی", OrDash(info.TaxpayerType));
            AddRow("کد پستی", OrDash(info.PostalcodeTaxpayer));
            AddRow("نشانی", OrDash(info.AddressTaxpayer));

            // خط جداکننده‌ی بصری
            AddRow("", "──────────────────────────");

            // 4) اگر کاربر ردیفی از گرید فاکتور را انتخاب کرده باشد، استعلام حافظه مالیاتی نیز انجام شود
            if (INVOCIE_DTGR.SelectedItem is TRACK_TAXDTL selRow)
            {
                try
                {
                    var fiscal = TaxApiService.Instance.TaxApis.GetFiscalInformation(selRow.SentTaxMemory);

                    if (fiscal == null)
                    {
                        AddRow("", "نتیجه‌ای برای حافظه مالیاتی یافت نشد.");
                    }
                    else
                    {
                        AddRow("", "ℹ️ اطلاعات حافظه مالیاتی:");
                        AddRow("نام/عنوان", OrDash(fiscal.NameTrade));

                        // اگر EconomicCode خالی بود، NationalId را جایگزین کن
                        var econOrNat = string.IsNullOrWhiteSpace(fiscal.EconomicCode)
                                        ? fiscal.NationalId
                                        : fiscal.EconomicCode;
                        AddRow("کد/شناسه اقتصادی", OrDash(econOrNat));

                        AddRow("وضعیت حافظه", MapStatus(fiscal.FiscalStatus.ToString()));
                    }
                }
                catch (Exception)
                {
                    AddRow("", "خطا در استعلام حافظه مالیاتی. لطفاً دوباره تلاش کنید.");
                }
            }

            // 5) نمایش پیام‌ها (رنگ پیش‌فرض)
            new MsgListwin(false, rows).ShowDialog();
        }

        private void Label_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (INVOCIE_DTGR.SelectedItem is not null)
            {
                IsOtherProccessingNow = true;
                try
                {
                    var selectedItem = INVOCIE_DTGR.SelectedItem as TRACK_TAXDTL;
                    string taxid = selectedItem.Taxid;
                    int isMainApi = (bool)selectedItem.ApiTypeSent ? 1 : 0;
                    string baseUid = selectedItem.UID;

                    // 1. دریافت اطلاعات کامل از دیتابیس
                    // مشابه لاجیک Resend، باید ردیف‌های کامل را بگیریم
                    var originalInvoiceRows = dbms.DoGetDataSQL<FULL_TAXDTL>(@"
                                                SELECT *
                                                FROM dbo.TAXDTL
                                                WHERE Taxid = @taxid AND ApiTypeSent = @api AND Uid = @baseUid
                                                ORDER BY CRT, IDD
                                            ", new { taxid, api = isMainApi, baseUid });

                    if (!originalInvoiceRows.Any())
                    {
                        // Fallback: شاید UID تغییر کرده یا کاربر روی ردیفی کلیک کرده که UID دقیق ندارد (مثلا ردیف خلاصه نیست)
                        // سعی میکنیم با TaxId بگیریم
                        originalInvoiceRows = dbms.DoGetDataSQL<FULL_TAXDTL>(@"
                                                SELECT *
                                                FROM dbo.TAXDTL
                                                WHERE Taxid = @taxid AND ApiTypeSent = @api
                                                ORDER BY CRT DESC, IDD
                                            ", new { taxid, api = isMainApi });
                    }

                    if (!originalInvoiceRows.Any())
                    {
                        new Msgwin(false, "اطلاعات جزئیات صورتحساب یافت نشد.").ShowDialog();
                        return;
                    }

                    // چون ممکن است چندین تلاش ارسال با یک تکس آی دی باشد (که نباید باشد ولی محض اطمینان)،
                    // ما آخرین تلاش (بر اساس CRT) را در نظر میگیریم یا همان که کاربر انتخاب کرده.
                    // اگر کاربر روی گرید کلیک کرده، ما UID آن ردیف را داریم.
                    // پس فیلتر روی UID که در بالا انجام دادیم صحیح است.

                    var header = CreateHeaderFromFullTaxDtl(originalInvoiceRows.First());
                    var bodies = CreateBodyListFromFullTaxDtl(originalInvoiceRows.ToList());

                    // 2. فراخوانی متد اعتبارسنجی
                    var result = InvoiceValidator.Validate(header, bodies);

                    // 3. نمایش نتایج
                    var rows = new List<object>();
                    int r = 1;

                    // افزودن خطاها
                    foreach (var err in result.Errors)
                    {
                        rows.Add(new { ROW_U = r++, CODE_U = "⛔", MessageText_U = err });
                    }

                    // افزودن هشدارها
                    foreach (var warn in result.Warnings)
                    {
                        rows.Add(new { ROW_U = r++, CODE_U = "⚠️", MessageText_U = warn });
                    }

                    if (result.IsValid && !result.Warnings.Any())
                    {
                        rows.Add(new { ROW_U = r++, CODE_U = "✅", MessageText_U = "تایید نهایی: تمامی بررسی‌های محاسباتی و ساختاری با موفقیت انجام شد." });
                    }

                    string color = result.IsValid ? "#FF00AA00" : "#FFFF0000"; // سبز یا قرمز
                    // اگر فقط هشدار باشد، رنگ نارنجی شاید بهتر باشد، اما سبز هم بد نیست چون معتبر است.

                    if (result.IsValid && result.Warnings.Any()) color = "#DD000000"; 

                    new MsgListwin(false, rows, color).ShowDialog();

                }
                catch (Exception ex)
                {
                    CL_Generaly.DoWritePRGLOG("Error in ReValidation", ex);
                    new Msgwin(false, "خطا در انجام عملیات اعتبارسنجی").ShowDialog();
                }
                finally
                {
                    IsOtherProccessingNow = false;
                }
            }
        }

        private bool Obsolete_ReValidate()
        {
            try
            {
                if (INVOCIE_DTGR.SelectedItem is not TRACK_TAXDTL selected)
                {
                    new Msgwin(false, "هیچ صورتحسابی انتخاب نشده است.").ShowDialog();
                    return false;
                }

                if (string.IsNullOrWhiteSpace(selected.Taxid))
                {
                    new Msgwin(false, "شناسه مالیاتی صورتحساب خالی است و امکان بررسی وجود ندارد.").ShowDialog();
                    return false;
                }

                var details = dbms.DoGetDataSQL<TAXDTL>($"SELECT * FROM dbo.TAXDTL WHERE Taxid = N'{selected.Taxid}'").ToList();
                if (details is null || details.Count == 0)
                {
                    new Msgwin(false, "هیچ سطری برای این صورتحساب در پایگاه داده پیدا نشد.").ShowDialog();
                    return false;
                }

                var header = details.First();
                decimal SumOrZero(Func<TAXDTL, decimal?> selector) => details.Sum(d => selector(d) ?? 0m);

                var report = new StringBuilder();
                var warnings = new List<string>();
                var oks = new List<string>();

                static bool IsValidNumeric(string? value, int length)
                {
                    return !string.IsNullOrWhiteSpace(value)
                           && value.Length == length
                           && value.All(char.IsDigit);
                }

                void Check(bool condition, string okMessage, string warningMessage)
                {
                    if (condition)
                    {
                        oks.Add(okMessage);
                    }
                    else
                    {
                        warnings.Add(warningMessage);
                    }
                }

                // تعداد سطرها
                if (selected.LineCount.HasValue && selected.LineCount != details.Count)
                {
                    warnings.Add($"تعداد سطرهای ثبت شده ({details.Count}) با تعداد ذخیره شده ({selected.LineCount}) همخوانی ندارد.");
                }
                else
                {
                    oks.Add($"تعداد سطرها ({details.Count}) با رکورد انتخابی هماهنگ است.");
                }

                // مغایرت جمع مبالغ مهم با سربرگ
                void CheckTotal(string title, decimal expected, decimal actual)
                {
                    if (Math.Round(expected, 2) != Math.Round(actual, 2))
                    {
                        warnings.Add($"جمع {title} سطرها ({actual:N0}) با مقدار سربرگ ({expected:N0}) برابر نیست.");
                    }
                    else
                    {
                        oks.Add($"جمع {title} سطرها ({actual:N0}) با سربرگ برابر است.");
                    }
                }

                CheckTotal("مبلغ قبل از تخفیف (Prdis)", header.Tprdis ?? 0m, SumOrZero(d => d.Prdis));
                CheckTotal("تخفیف (Dis)", header.Tdis ?? 0m, SumOrZero(d => d.Dis));
                CheckTotal("مبلغ پس از تخفیف (Adis)", header.Tadis ?? 0m, SumOrZero(d => d.Adis));
                CheckTotal("مالیات/عوارض (Vam)", header.Tvam ?? 0m, SumOrZero(d => d.Vam));
                CheckTotal("جمع صورتحساب (Tbill)", header.Tbill ?? 0m, SumOrZero(d => d.Tsstam));

                var headerNet = header.Tadis ?? 0m;
                var headerVat = header.Tvam ?? 0m;
                var headerOtherDuties = header.Todam ?? 0m;
                var recomputedHeaderBill = Math.Round(headerNet + headerVat + headerOtherDuties, 2);
                if (Math.Round(header.Tbill ?? 0m, 2) != recomputedHeaderBill)
                {
                    warnings.Add($"جمع نهایی سربرگ (Tbill={header.Tbill:N0}) با مجموع خالص+مالیات+عوارض ({recomputedHeaderBill:N0}) برابر نیست.");
                }
                else
                {
                    oks.Add("جمع نهایی سربرگ با اجزای آن همخوانی دارد.");
                }

                Check(IsValidNumeric(header.Tins, 11), "کد اقتصادی فروشنده (Tins) معتبر است.", "کد اقتصادی فروشنده (Tins) خالی یا ۱۱ رقمی نیست.");
                if (!string.IsNullOrWhiteSpace(header.Tinb))
                {
                    Check(IsValidNumeric(header.Tinb, 11), "کد اقتصادی خریدار (Tinb) صحیح به نظر می‌رسد.", "کد اقتصادی خریدار (Tinb) باید ۱۱ رقمی و فقط عدد باشد.");
                }
                else if (string.IsNullOrWhiteSpace(header.Bpn))
                {
                    warnings.Add("هیچ شناسه‌ای برای خریدار (Tinb/Bpn) ثبت نشده است.");
                }
                else
                {
                    Check(IsValidNumeric(header.Bpn, 10), "کد ملی/شناسه خریدار (Bpn) وارد شده است.", "کد ملی/شناسه خریدار (Bpn) باید ۱۰ رقمی باشد.");
                }

                if (header.Ins is 2 or 3 or 4)
                {
                    Check(!string.IsNullOrWhiteSpace(header.Irtaxid), "شناسه مرجع برای صورتحساب اصلاحی/ابطالی درج شده است.", "برای صورتحساب‌های غیر اصلی، شناسه مرجع (Irtaxid) الزامی است.");
                }

                Check(!string.IsNullOrWhiteSpace(header.Inno), "شماره فاکتور داخلی ثبت شده است.", "شماره فاکتور داخلی (Inno) خالی است.");
                Check(header.Inty is >= 1 and <= 3, "نوع صورتحساب (Inty) در بازه مجاز است.", "نوع صورتحساب (Inty) خارج از بازه مجاز ۱ تا ۳ است.");
                Check(header.Inp is >= 1 and <= 7, "الگوی صورتحساب (Inp) صحیح است.", "الگوی صورتحساب (Inp) خارج از بازه مجاز است.");
                Check(header.Ins is >= 1 and <= 4, "موضوع صورتحساب (Ins) معتبر است.", "موضوع صورتحساب (Ins) خارج از بازه مجاز است.");

                foreach (var line in details.Select((d, index) => (d, index)))
                {
                    var row = line.index + 1;
                    var d = line.d;

                    if (string.IsNullOrWhiteSpace(d.Sstid))
                    {
                        warnings.Add($"ردیف {row}: کد کالا/خدمت (Sstid) خالی است.");
                    }

                    if ((d.Am ?? 0) <= 0 || (d.Fee ?? 0) <= 0)
                    {
                        warnings.Add($"ردیف {row}: مقدار یا فی صفر/منفی است.");
                    }

                    var prdis = d.Prdis ?? 0m;
                    var dis = d.Dis ?? 0m;
                    var adis = d.Adis ?? 0m;
                    var vra = d.Vra ?? 0m;
                    var vam = d.Vam ?? 0m;
                    var total = d.Tsstam ?? 0m;

                    if (Math.Round(prdis - dis, 2) != Math.Round(adis, 2))
                    {
                        warnings.Add($"ردیف {row}: مبلغ پس از تخفیف (Adis) با مبلغ قبل از تخفیف منهای تخفیف برابر نیست.");
                    }

                    if (dis > prdis)
                    {
                        warnings.Add($"ردیف {row}: تخفیف (Dis) از مبلغ قبل از تخفیف (Prdis) بیشتر است.");
                    }

                    var expectedVat = Math.Round(adis * vra / 100m, 2);
                    if (Math.Abs(expectedVat - Math.Round(vam, 2)) > 1)
                    {
                        warnings.Add($"ردیف {row}: مبلغ مالیات/عوارض (Vam) با نرخ اعمال‌شده ({vra}٪) سازگار نیست (انتظار {expectedVat:N0}).");
                    }

                    var expectedTotal = Math.Round(adis + vam, 2);
                    if (Math.Abs(expectedTotal - Math.Round(total, 2)) > 1)
                    {
                        warnings.Add($"ردیف {row}: جمع ردیف (Tsstam) با مبلغ پس از تخفیف و مالیات هم‌خوان نیست.");
                    }

                    if (vra == 0 && vam != 0)
                    {
                        warnings.Add($"ردیف {row}: نرخ مالیات صفر است ولی مبلغ مالیات/عوارض ثبت شده است.");
                    }
                }

                var referenceText = selected.RefrenceNumber ?? string.Empty;
                report.AppendLine($"بازاعتبارسنجی کامل برای صورت‌حساب {selected.Taxid} ({referenceText}):\n");

                if (warnings.Count == 0)
                {
                    report.AppendLine("هیچ مغایرتی در جمع مبالغ، شناسه‌ها و اقلام دیده نشد. اگر کارپوشه هنوز خالی است، زمان بیشتری برای نهایی شدن صورتحساب نیاز است یا سامانه را بعداً دوباره بررسی کنید.");
                }
                else
                {
                    report.AppendLine("هشدارها:");
                    foreach (var w in warnings)
                    {
                        report.AppendLine($"- {w}");
                    }
                }

                if (oks.Count > 0)
                {
                    report.AppendLine("\nبررسی‌های پاس شده:");
                    foreach (var ok in oks)
                    {
                        report.AppendLine($"- {ok}");
                    }
                }

                report.AppendLine("\nیادآوری: حتی با پاسخ SUCCESS ممکن است در زمان قطعی یا تأخیر سامانه، نمایش صورتحساب در کارپوشه به تعویق بیفتد.");

                Msgwin msgwin = new Msgwin(false, report.ToString());
                msgwin.Height = 500;
                msgwin.ShowDialog();
            }
            catch (Exception ex)
            {
                new Msgwin(false, "خطا در بازاعتبارسنجی.").ShowDialog();
                CL_Generaly.DoWritePRGLOG("AnalyzeInvoice_Click", ex);
            }

            return true;
        }

    }
}
