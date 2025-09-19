using Newtonsoft.Json;
using Prg_Graphicy.Wins;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.FUNCTIONS;
using Prg_Moadian.Generaly;
using Prg_Moadian.SQLMODELS;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.ComponentModel;
using Prg_Graphicy.LMethods;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using System.Windows.Input;
using System.Xml.Serialization;
using TaxCollectData.Library.Business;
using TaxCollectData.Library.Dto.Content;
using TaxCollectData.Library.Dto.Config;
using TaxCollectData.Library.Dto.Properties;
using TaxCollectData.Library.Enums;
using System.Windows.Documents;
using System.Threading.Tasks;
using Prg_Moadian.Service;
using static Prg_Moadian.CNNMANAGER.TaxModel;
using NPOI.Util;
using System.Globalization;
using System.Net.Sockets;
using System.Net;
using System.Data.SqlClient;
using Dapper;
using System.Windows.Media;

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
                                                                     ROW_NUMBER() OVER (ORDER BY MAX(CRT) DESC) AS RowNumber,
                                                                     Taxid,
                                                                     MAX(Inno) AS Inno,
                                                                     MAX(Inty) AS Inty,
                                                                 	MAX(Inp) AS Inp,
                                                                 	MAX(Ins) AS Ins,
                                                                 	COUNT(*) AS LineCount,
                                                                 	UID,
                                                                     RefrenceNumber,
                                                                     TheConfirmationReferenceId,
                                                                     SentTaxMemory,
                                                                     MAX(CRT) AS CRT,
                                                                     MAX(CAST(TheSuccess AS INT)) AS TheSuccess,
                                                                 	TheStatus,
                                                                    MAX(DATE_N) AS DATE_N,
                                                                    MAX(CAST(ApiTypeSent AS INT)) AS ApiTypeSent,
                                                                 	MAX(TheError) AS TheError
                                                                 FROM 
                                                                     dbo.TAXDTL
                                                                 WHERE 
                                                                     ApiTypeSent = {_apitypesent} 
                                                                     AND CRT >= DATEADD(DAY, -30, GETDATE())
                                                                 GROUP BY 
                                                                     Taxid,TheStatus, UID, RefrenceNumber, TheConfirmationReferenceId, SentTaxMemory
                                                                 ORDER BY 
                                                                     CRT DESC;").ToList();
            }
            else
            {
                TAXDTL_DATA = dbms.DoGetDataSQL<TRACK_TAXDTL>(@$"SELECT 
                                                                     ROW_NUMBER() OVER (ORDER BY MAX(CRT) DESC) AS RowNumber,
                                                                     Taxid,
                                                                     MAX(Inno) AS Inno,
                                                                     MAX(Inty) AS Inty,
                                                                 	MAX(Inp) AS Inp,
                                                                 	MAX(Ins) AS Ins,
                                                                 	COUNT(*) AS LineCount,
                                                                 	UID,
                                                                     RefrenceNumber,
                                                                     TheConfirmationReferenceId,
                                                                     SentTaxMemory,
                                                                     MAX(CRT) AS CRT,
                                                                     MAX(CAST(TheSuccess AS INT)) AS TheSuccess,
                                                                 	TheStatus,
                                                                    MAX(DATE_N) AS DATE_N,
                                                                     MAX(CAST(ApiTypeSent AS INT)) AS ApiTypeSent,
                                                                 	MAX(TheError) AS TheError
                                                                 FROM 
                                                                     dbo.TAXDTL
                                                                 WHERE 
                                                                     ApiTypeSent = {_apitypesent}
                                                                 GROUP BY 
                                                                     Taxid,TheStatus, UID, RefrenceNumber, TheConfirmationReferenceId, SentTaxMemory
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

            if (!string.IsNullOrEmpty(UID_TXB.Text.Trim()))
            {
                bool IsInProgressOrNotFound = false;

                try
                {
                    var _newsaz = dbms.DoGetDataSQL<SAZMAN>("SELECT MEMORYID,MEMORYIDsand,PRIVIATEKEY,Dcertificate FROM dbo.SAZMAN").FirstOrDefault();
                    var PrivateKeyTax = _newsaz.PRIVIATEKEY.Replace("-----BEGIN PRIVATE KEY-----\r\n", "").Replace("\r\n-----END PRIVATE KEY-----\r\n", "").Trim();
                    string MemoryTax = "";
                    if (TaxURL == "https://tp.tax.gov.ir/req/api/")
                    {
                        MemoryTax = _newsaz.MEMORYID.Trim(); //حافظه مالیاتی اصلی
                    }
                    else
                    {
                        MemoryTax = _newsaz.MEMORYIDsand.Trim(); //حافظه مالیاتی تستی سندباکس
                    }
                    TaxApiService.Instance.Init(MemoryTax, new SignatoryConfig(PrivateKeyTax, null), new NormalProperties(ClientType.SELF_TSP), TaxURL);
                    ServerInformationModel serverInformationModel = TaxApiService.Instance.TaxApis.GetServerInformation();
                    TokenModel tokenModel = TaxApiService.Instance.TaxApis.RequestToken();

                    var referenceCode = UID_TXB.Text.Trim();
                    var inquiryResultModels = TaxApiService.Instance.TaxApis.InquiryByReferenceId(new() { referenceCode });

                    List<string> list = new List<string>(); list.Add(referenceCode);
                    List<InquiryResultModel> list2 = TaxApiService.Instance.TaxApis.InquiryByReferenceId(list);

                    if (list2.FirstOrDefault()?.Status?.ToUpper() == "IN_PROGRESS")
                    {
                        IsInProgressOrNotFound = true;

                        new Msgwin(false, @"وضعیت این صورت حساب باتوجه به پاسخ سامانه ""در حال انجام"" (IN_PROGRESS) است , و هنوز نهایی نشده است.
                                    ولی گاهی به خاطر ترافیک سنگین سامانه صورت حساب میتواند در کارپوشه اضافه شده باشد ولی همچنان وضعیت مذکور را داشته باشد, پس در صورت نیاز دستی آنرا در کارپوشه با شماره مالیاتی جستجو کنید.").ShowDialog();
                    }
                    else if (list2.FirstOrDefault()?.Status?.ToUpper() == "NOT_FOUND")
                    {
                        IsInProgressOrNotFound = true;

                        new Msgwin(false, @"چنین صورت حساب یافت نشد , کد وضعیت : ""NOT_FOUND"" , این خطا زمانی رخ میدهد که این کد رهگیری صورت حساب متعلق به حافظه مالیاتی شما نباشد (این صورت حساب شما نیست)
                                   , در موارد خاصی به دلیل ترافیک بالای سامانه این خطا نیز میتواند رخ دهد , در صورتی که مطمئن هستید این کد صورت حساب متعلق به شماست , صبر کنید و ساعاتی بعد مجدد آنرا چک کنید.").ShowDialog();
                    }
                    else if (list2.FirstOrDefault()?.Status?.ToUpper() == "SUCCESS")
                    {
                        IsInProgressOrNotFound = true;

                        new Msgwin(false, @"صورت حساب با موفقیت در سامانه ثبت شده").ShowDialog();
                    }
                    else if (list2.FirstOrDefault()?.Status?.ToUpper() == "PENDING")
                    {
                        IsInProgressOrNotFound = true;
                        new Msgwin(false, "این صورت حساب هنوز در وضعیت \"در انتظار\" است , لطفا صبر کنید و مدتی بعد مجددا بررسی بفرمایید.").ShowDialog();
                    }
                    else if (list2.FirstOrDefault()?.Status?.ToUpper() == "FAILED")
                    {
                        IsInProgressOrNotFound = true;

                        TaxModel.InquiryByReferenceIdModel inquiryByReferenceIdModel = new TaxModel.InquiryByReferenceIdModel();
                        string value = list2.Select((InquiryResultModel x) => x.Data).FirstOrDefault()!.ToString();
                        TaxModel.InquiryByReferenceIdModel.Root manuallroot = JsonConvert.DeserializeObject<TaxModel.InquiryByReferenceIdModel.Root>(value);
                        manuallroot.status = list2[0].Status;

                        List<string>? _er_lst = new List<string>();

                        foreach (var item in manuallroot.error)
                            _er_lst.Add(item.code + " | " + item.message);

                        string? _msgitem_theError = (_er_lst != null && _er_lst.Count > 0) ? $"{string.Join(",", _er_lst)}" : null;

                        if (!string.IsNullOrEmpty(_msgitem_theError) && !string.IsNullOrWhiteSpace(_msgitem_theError))
                        {
                            new MsgListwin(false, Functions.GetNormilizedMsg(_msgitem_theError)).ShowDialog();
                        }
                    }
                }
                catch (Exception er)
                {
                    try
                    {
                        if (!IsInProgressOrNotFound) //if is not In Progress it has really error
                        {
                            if (er.InnerException is null)
                            {
                                new Msgwin(false, "\"پاسخی از سمت سامانه دریافت نشد\" , این پیغام یعنی صورت حساب هنوز در سامانه پردازش نـشده است , لطفا ساعاتی بعد مجددا بررسی بفرمایید.").ShowDialog();
                            }
                            else
                            {
                                new Msgwin(false, "خطا در انجام عملیات استعلام").ShowDialog();
                                CL_Generaly.DoWritePRGLOG("GETESTELAM_REFCODE_UPDATE : \n", er);
                            }
                        }
                    }
                    catch { }
                }
            }

            IsOtherProccessingNow = false;
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
                else if (_msgitem.TheStatus is "SUCCESS" && (_msgitem.Ins is 1)) //اصلی
                {
                    if (!string.IsNullOrEmpty(_msgitem?.Taxid))
                    {
                        //FactorManagement_WIN FM_WIN = new FactorManagement_WIN();
                        //FM_WIN.TAXID = _msgitem.Taxid;
                        //FM_WIN.ShowDialog();

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
            var DtNowBase = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, iranTZ);
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
    }
}
