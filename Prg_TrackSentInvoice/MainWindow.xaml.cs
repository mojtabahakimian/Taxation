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
                var _msger = CER.ExpecMsgEr(er);

                if (!string.IsNullOrEmpty(_msger)) //Known Message
                {
                    new Msgwin(false, _msger).ShowDialog();//نمایش خطای شناخته شده
                    System.Environment.Exit(0);
                }
                else
                {
                    new Msgwin(false, "خطا در انجام عملیات , Unknown").ShowDialog();
                    CL_Generaly.DoWritePRGLOG("Unknown Error in Send Estelam Invoce : \n", er);
                    System.Environment.Exit(0);
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
                        FactorManagement_WIN FM_WIN = new FactorManagement_WIN();
                        FM_WIN.TAXID = _msgitem.Taxid;
                        FM_WIN.ShowDialog();
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
    }
}
