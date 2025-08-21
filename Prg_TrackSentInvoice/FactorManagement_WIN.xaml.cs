using Prg_Graphicy.LMethods;
using Prg_Graphicy.Wins;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.FUNCTIONS;
using Prg_Moadian.Generaly;
using Prg_Moadian.SQLMODELS;
using Prg_TrackSentInvoice.ADDON;
using Prg_TrackSentInvoice.LMETHOD;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Windows;
using System.Windows.Input;
using static Prg_Graphicy.Payewin;
using static Prg_Moadian.Generaly.CL_Generaly;
using System.Windows.Forms;
using System.Data.SqlClient;
using Dapper;
using System.Reflection;
using NPOI.SS.Formula.Functions;

namespace Prg_TrackSentInvoice
{
    public partial class FactorManagement_WIN : Window
    {
        #region LOCALMODEL
        public class VAHEDS
        {
            public int IDD { get; set; }
            public string? NAME_MO { get; set; }
        }
        #endregion
        public FactorManagement_WIN()
        {
            InitializeComponent();
            this.DataContext = this;
        }
        CL_CCNNMANAGER dbms;
        CL_FUNTIONS Functions = new CL_FUNTIONS();
        public List<FULL_TAXDTL> WHOLE_DATA_INVOICES { get; set; } = new List<FULL_TAXDTL>();

        public string TAXID { get; set; }// = "A2HEZ304D010021C3C5D15";
        public string RSDTL_TMP_NAME { get; set; }

        public ObservableCollection<FULL_TAXDTL> BODY_DATA_INVOICE { get; set; } = new ObservableCollection<FULL_TAXDTL>();
        //private string TaxURL { get; set; } = "https://tp.tax.gov.ir/req/api/";

        List<VAHEDS> VAHEDHA = new List<VAHEDS>();
        private bool NowIsReady = false;

        private bool searchtextexist;
        public bool IsSearchTextExist
        {
            get
            {
                var one = !string.IsNullOrEmpty(SEARCH_Taxid.Text.Trim());
                var two = !string.IsNullOrEmpty(SEARCH_Inno.Text.Trim());
                var three = !string.IsNullOrEmpty(SEARCH_Irtaxid.Text.Trim());

                if (one || two | three)
                {
                    searchtextexist = true;
                }
                else
                {
                    searchtextexist = false;
                }
                return searchtextexist;
            }
        }

        private string _searchwhereqre;
        public string Searchwhereqre
        {
            get
            {
                List<string> conditions = new List<string>();

                if (!string.IsNullOrEmpty(SEARCH_Taxid.Text.Trim()))
                {
                    conditions.Add($"Taxid = N'{SEARCH_Taxid.Text.Trim()}'");
                }
                if (!string.IsNullOrEmpty(SEARCH_Inno.Text.Trim()))
                {
                    conditions.Add($"Inno LIKE N'%{SEARCH_Inno.Text.Trim()}%'");
                }
                if (!string.IsNullOrEmpty(SEARCH_Irtaxid.Text.Trim()))
                {
                    conditions.Add($"Irtaxid = N'{SEARCH_Irtaxid.Text.Trim()}'");
                }

                if (conditions.Count == 1)
                {
                    _searchwhereqre = $" AND {conditions.FirstOrDefault()} ";
                }
                else
                {
                    var qre = string.Join(" AND ", conditions);
                    _searchwhereqre = " AND " + qre;
                }
                conditions.Clear();

                return _searchwhereqre;
            }
        }
        private void Window_ContentRendered(object sender, System.EventArgs e)
        {
            NowIsReady = true;
        }
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            dbms = new CL_CCNNMANAGER();
            //try
            //{
            //    dbms = new CL_CCNNMANAGER();
            //    var TestCNN = dbms.DoGetDataSQL<string>("SELECT TOP 1 YEA FROM dbo.SAZMAN").FirstOrDefault();
            //}
            //catch (Exception)
            //{
            //    new Msgwin(false, "خطا در ارتباط با دیتابیس").ShowDialog();
            //    System.Environment.Exit(0);
            //}

            VAHEDHA = dbms.DoGetDataSQL<VAHEDS>("SELECT IDD, NAME_MO FROM dbo.TCOD_VAHED_EXTENDED").ToList();

            var RowData = dbms.DoGetDataSQL<string?>($"SELECT Taxid FROM dbo.TAXDTL WHERE TheStatus=N'SUCCESS' AND Taxid=N'{TAXID}'").ToList();
            if (RowData.Count > 0) //Found Then
            {
                RSDTL_TMP_NAME = "RSDTL" + DateTime.Now.ToString("yyyy_MM_dd__HH_mm_ss");

                dbms.DoExecuteSQL($@"SELECT * INTO {RSDTL_TMP_NAME} FROM TAXDTL WHERE TheStatus=N'SUCCESS' AND Taxid=N'{TAXID}'");
            }
            else
            {
                this.Close();
            }

            HEADER_ReGetData();

            LMETHOD.CL_GENERAL.THE_RSDTL_TMP_NAME = RSDTL_TMP_NAME;
        }

        private void HEADER_ReGetData()
        {
            //byte _apitypesent = 0;
            //if (TaxURL is "https://tp.tax.gov.ir/req/api/") // اگر روی اصلیه
            //    _apitypesent = 1; //Main
            //else
            //    _apitypesent = 0; //SandBox

            List<FULL_TAXDTL> headers = null;
            //string MainHeadQuery = $"SELECT DISTINCT Taxid, ROW_NUMBER() OVER (ORDER BY IDD) AS ROWNUMBER,Taxid,Inno,Inty,Inp,Ins,Irtaxid,IDD,UID,RefrenceNumber,TheError,TheStatus,TheSuccess,ApiTypeSent,SentTaxMemory,CRT FROM {RSDTL_TMP_NAME} WHERE Ins = 4 AND TheSuccess = 1 AND Taxid = N'{TAXID}' ";
            string MainHeadQuery = $"SELECT Taxid, Inno, Inty, Inp, Ins, Irtaxid, MIN(IDD) AS IDD, UID, RefrenceNumber, TheError, TheStatus, TheSuccess, ApiTypeSent, SentTaxMemory, MAX(CRT) AS CRT FROM {RSDTL_TMP_NAME} GROUP BY Taxid, Inno, Inty, Inp, Ins, Irtaxid, UID, RefrenceNumber, TheError, TheStatus, TheSuccess, ApiTypeSent, SentTaxMemory ";

            //if (IsSearchTextExist) //Currently is Disabled ----------------------------------------
            //{
            //    headers = dbms.DoGetDataSQL<FULL_TAXDTL>($"{MainHeadQuery} {Searchwhereqre} ").ToList();
            //    foreach (var item in headers)
            //    {
            //        item.Inno = Convert.ToString(item.Inno)?.TrimStart('0');
            //        item.PersianCRT = Functions.ConvertToPersianDate((DateTime)item.CRT);
            //    }
            //}
            //else
            //{
            headers = dbms.DoGetDataSQL<FULL_TAXDTL>($"{MainHeadQuery}").ToList();
            foreach (var item in headers)
            {
                item.Inno = Convert.ToString(item.Inno)?.TrimStart('0');
                item.PersianCRT = Functions.ConvertToPersianDate((DateTime)item.CRT);
            }
            //}
            INVOICE_HEADER.ItemsSource = headers;

            BodyreGetData();
        }
        private void BodyreGetData()
        {
            var body = dbms.DoGetDataSQL<FULL_TAXDTL>($"SELECT Sbc, Bpc, Ft, Bpn, Scln, Scc, Crn, Billid, Tprdis, Tdis, Tadis, Tvam, Todam, Tbill, Setm, Cap, Insp, Tvop, Tax17, Sstid, Sstt, Mu, Am, Fee, Cfee, Cut, Exr, Prdis, Dis, Adis, Vra, Vam, Odt, Odr, Odam, Olt, Olr, Olam, Consfee, Spro, Bros, Tcpbs, Cop, Vop, Bsrn, Tsstam, Iinn, Acn, Trmn, Trn, Pcn, Pid, Pdt, Cdcn, Cdcd, Tonw, Torv, Tocv, Nw, Ssrv, Sscv, Pmt, PV, IDD FROM {RSDTL_TMP_NAME} WHERE TheStatus=N'SUCCESS' AND Taxid = N'{TAXID}' ORDER BY IDD").ToList();
            //پر کردن واحد ها
            for (int i = 0; i < body.Count; i++)
            {
                int _VAHED_ = 0;
                if (body[i]?.Mu != null)
                {
                    double tempDouble = Convert.ToDouble(body[i].Mu);
                    _VAHED_ = Convert.ToInt32(tempDouble);
                }

                body[i].NAME_VAHED = VAHEDHA.Where(vi => vi.IDD == _VAHED_).FirstOrDefault().NAME_MO;
            }

            BODY_DATA_INVOICE?.Clear();
            foreach (var item in body)
            {
                BODY_DATA_INVOICE.Add(item);
            }
        }
        private void INVOICE_BODY_PreviewMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!NowIsReady || INVOICE_BODY is null || INVOICE_BODY.Items.Count < 1 || INVOICE_BODY.SelectedItem is null) { return; }

            var Row = INVOICE_BODY.SelectedItem as FULL_TAXDTL;

            FactorEditor_WIN FEWIN = new FactorEditor_WIN(Row, VAHEDHA, RSDTL_TMP_NAME);
            FEWIN.Owner = this;
            FEWIN.ShowDialog();
        }

        #region Currently_Disabled
        private void INVOICE_HEADER_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            return; //Currently Disabled
            if (!NowIsReady || INVOICE_HEADER is null || INVOICE_HEADER.Items.Count < 1) { return; }

            if (INVOICE_HEADER.SelectedItem is not null)
            {
                var THEROW = INVOICE_HEADER.SelectedItem as FULL_TAXDTL;

                //var body = dbms.DoGetDataSQL<FULL_TAXDTL>($"SELECT Sbc, Bpc, Ft, Bpn, Scln, Scc, Crn, Billid, Tprdis, Tdis, Tadis, Tvam, Todam, Tbill, Setm, Cap, Insp, Tvop, Tax17, Sstid, Sstt, Mu, Am, Fee, Cfee, Cut, Exr, Prdis, Dis, Adis, Vra, Vam, Odt, Odr, Odam, Olt, Olr, Olam, Consfee, Spro, Bros, Tcpbs, Cop, Vop, Bsrn, Tsstam, Iinn, Acn, Trmn, Trn, Pcn, Pid, Pdt, Cdcn, Cdcd, Tonw, Torv, Tocv, Nw, Ssrv, Sscv, Pmt, PV, IDD FROM dbo.TAXDTL WHERE Taxid = N'{THEROW.Taxid}' ORDER BY IDD").ToList();
                var body = dbms.DoGetDataSQL<FULL_TAXDTL>($"SELECT Sbc, Bpc, Ft, Bpn, Scln, Scc, Crn, Billid, Tprdis, Tdis, Tadis, Tvam, Todam, Tbill, Setm, Cap, Insp, Tvop, Tax17, Sstid, Sstt, Mu, Am, Fee, Cfee, Cut, Exr, Prdis, Dis, Adis, Vra, Vam, Odt, Odr, Odam, Olt, Olr, Olam, Consfee, Spro, Bros, Tcpbs, Cop, Vop, Bsrn, Tsstam, Iinn, Acn, Trmn, Trn, Pcn, Pid, Pdt, Cdcn, Cdcd, Tonw, Torv, Tocv, Nw, Ssrv, Sscv, Pmt, PV, IDD FROM dbo.TAXDTL WHERE  Ins = 4 AND TheSuccess = 1 AND Taxid = N'{TAXID}' ORDER BY IDD").ToList();
                //پر کردن واحد ها
                for (int i = 0; i < body.Count; i++)
                {
                    body[i].NAME_VAHED = VAHEDHA.Where(vi => vi.IDD.ToString() == body[i].Mu.ToString()).FirstOrDefault().NAME_MO;
                }

                BODY_DATA_INVOICE?.Clear();
                foreach (var item in body)
                {
                    BODY_DATA_INVOICE.Add(item);
                }
                //INVOICE_BODY.ItemsSource = body;
            }
        }
        private void SEARCH_BTN_Click(object sender, RoutedEventArgs e)
        {
            HEADER_ReGetData();
        }
        private void CLEAR_BTN_Click(object sender, RoutedEventArgs e)
        {
            SEARCH_Taxid.Clear(); SEARCH_Inno.Clear(); SEARCH_Irtaxid.Clear();
            HEADER_ReGetData();
        }
        private void INVOICE_BODY_LostFocus(object sender, RoutedEventArgs e)
        {
            // Check if the DataGrid still has the keyboard focus
            if (Keyboard.FocusedElement is DependencyObject depObj)
            {
                // Check if the focused element is not a child of the DataGrid
                if (!INVOICE_BODY.IsAncestorOf(depObj))
                {
                    // The focus is completely out of DataGrid scope.
                }
            }
        }
        #endregion

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            ClearTempTable();
        }

        private void ClearTempTable()
        {
            try { dbms.DoExecuteSQL($@"DROP TABLE {RSDTL_TMP_NAME}"); } catch { }
        }
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
        private void SendToMoadian_BTN_Click(object sender, RoutedEventArgs e)
        {
            if (!BODY_DATA_INVOICE.Any() || !BODY_DATA_INVOICE.Any(x => x?.Am > 0))
            {
                new Msgwin(false, "آیتمی برای ارسال وجود ندارد").Show();
                return;
            }

            try
            {
                using (var db = new SqlConnection(CL_CCNNMANAGER.CONNECTION_STR))
                {
                    db.Open();

                    // Windows username
                    var windowsUser = Environment.UserName;

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
            catch
            {
                // ignore
            }

            var _IsSuccess = true;

            Msgwin msgwin0 = new Msgwin(true, $"انتخاب کنید که به کدام سامانه میخواهید ارسال کنید ", null, false, true, "سامانه تستی", "سامانه اصلی");
            msgwin0.ShowDialog();
            if (msgwin0.DialogResult == false)
            {
                //MAIN_API
                CL_MOADIAN.TaxURL = "https://tp.tax.gov.ir/req/api/"; //درصورت عدم تایید به سامانه اصلی ارسال میشود.
            }
            if (msgwin0.ClosedByUser) //اگر کاربر پنجره رو بست و نخواست ادامه بده
            {
                return;
            }

            CustomExceptErMsg CER = new CustomExceptErMsg();
            CL_FUNTIONS TheFunctions = new CL_FUNTIONS();
            try
            {
                CL_INDICATOR.Start();

                var _Returny_ = dbms.DoGetDataSQL<string>($"SELECT TheStatus FROM dbo.TAXDTL WHERE ApiTypeSent = 1 AND Ins = 4 AND Taxid = N'{TAXID}' ").ToList(); //4 = برگشتی
                if (_Returny_.Contains("PENDING") && CL_MOADIAN.TaxURL == "https://tp.tax.gov.ir/req/api/")
                {
                    Msgwin msgwinv = new Msgwin(true, $"قبلا ارسال شده , اما وضیعت آن هنوز *درانتظار* است, آیا میخواهید مجددا ارسال کنید ؟");
                    msgwinv.ShowDialog();
                    if (msgwinv.DialogResult is false)
                    {
                        return;
                    }
                }
                else if (_Returny_.Contains("SUCCESS") && CL_MOADIAN.TaxURL == "https://tp.tax.gov.ir/req/api/")
                {
                    Msgwin msgwinv = new Msgwin(true, $"قبلا با موفقیت ارسال و در سامانه ثبت شده , آیا میخواهید مجددا ارسال کنید ؟");
                    msgwinv.ShowDialog();
                    if (msgwinv.DialogResult is false)
                    {
                        return;
                    }
                }

                CL_EXTRA_MOADIAN_RS.DoSendInvoice(RSDTL_TMP_NAME);
            }
            catch (Exception er)
            {
                _IsSuccess = false;

                var _KnowMsg = CER.ExpecMsgEr(er);
                if (!string.IsNullOrEmpty(_KnowMsg)) //خطا های شناخته شده
                {
                    new Msgwin(false, _KnowMsg).ShowDialog();
                }
                else
                {
                    Msgwin msgwin = new Msgwin(false, "خطا در انجام عملیات , ارتباط با سامانه مقدور نمی باشد."); //Unknown
                    msgwin.ShowDialog();
                    LogWriter.WriteLog($"\n[ Unkown Error, Expetion : Message: {er.Message}{Environment.NewLine} StackTrace: {er.StackTrace}{Environment.NewLine} \n {er.Data} \n {er.InnerException} \n" +
                        $" {er.Source} \n" +
                        $" {er.TargetSite} \n" +
                        $" {er.HResult} \n " +
                        $" {er.HelpLink} \n " +
                        $"End Log ]\n");
                }
            }
            finally
            {
                if (_IsSuccess)
                {
                    if (!string.IsNullOrEmpty(FactorInfoSent.ReferenceNumber))
                    {
                        Msgwin msgwin = new Msgwin(false, $"به کد دهگیری :  {FactorInfoSent.ReferenceNumber}  در صف ارسال قرار گرفت , لطفا مدتی بعد بررسی بفرمایید .");
                        msgwin.ShowDialog();

                        //سعی بر استعلام
                        #region JustTryToEstelam
                        try
                        {
                            string _msger = null;
                            //var _qre0 = dbms.DoGetDataSQL<ES1>($"SELECT TheError,TheStatus FROM dbo.TAXDTL WHERE TheStatus <> N'PENDING' AND TheError <> N'' AND RefrenceNumber = N'{FactorInfoSent.ReferenceNumber}' ").ToList();
                            var _qre0 = dbms.DoGetDataSQL<ES1>($"SELECT TheError,TheStatus FROM dbo.TAXDTL WHERE RefrenceNumber = N'{FactorInfoSent.ReferenceNumber}' ").ToList();
                            foreach (var item in _qre0)
                            {
                                if (!string.IsNullOrEmpty(item.TheError) && !string.IsNullOrWhiteSpace(item.TheError))
                                    _msger = item.TheError;
                                if (!string.IsNullOrEmpty(item.TheStatus) && !string.IsNullOrWhiteSpace(item.TheStatus))
                                {
                                    if (item.TheStatus != "SUCCESS")
                                    {
                                        _msger = "_NOT_OK_";
                                    }
                                }
                            }

                            if (!string.IsNullOrEmpty(_msger) && !string.IsNullOrWhiteSpace(_msger) && _msger != "NULL")
                            {
                                if (_msger != "_NOT_OK_")
                                {
                                    new MsgListwin(false, TheFunctions.GetNormilizedMsg(_msger)).ShowDialog();
                                }
                            }
                            else //اگر در استعلام خطایی یافت نشد
                            {
                                new Msgwin(false, "صورت حساب با موفقیت در سامانه ثبت شد.").ShowDialog();
                            }
                        }
                        catch (Exception) { }
                        #endregion
                    }
                }

                CL_INDICATOR.Dispose();
                ClearTempTable();
                System.Environment.Exit(0);
            }
        }

        private void INVOICE_BODY_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (BODY_DATA_INVOICE.Count > 0)
            {
                if (e.Key == Key.Delete)
                {
                    e.Handled = true;

                    if (!(INVOICE_BODY.SelectedItems is null))
                    {
                        Msgwin msgwin = new Msgwin(true, "آیا مایل به حذف هستید ؟"); msgwin.ShowDialog();
                        if (msgwin.DialogResult == true)
                        {
                            for (int i = 0; i < INVOICE_BODY.SelectedItems.Count; i++)
                            {
                                var item = INVOICE_BODY.SelectedItems[i];

                                var _IDD_ = item.GetType().GetProperty("IDD").GetValue(item);

                                if (_IDD_ != null)
                                {
                                    dbms.DoExecuteSQL($@"DELETE FROM {RSDTL_TMP_NAME} WHERE IDD = {_IDD_}");
                                }
                            }

                            BodyreGetData();
                        }
                        else
                        {
                            e.Handled = true; //اجازه نده از دیتاگرید چیزی حذف بشه
                        }
                    }
                }


            }
        }

        //private void SEND_BTN_Click(object sender, RoutedEventArgs e)
        //{
        //    if (sender is Button btn)
        //    {
        //        var SATR_AUTOMASION = (FULL_TAXDTL)(sender as Button).Tag;
        //        if (SATR_AUTOMASION is not null)
        //        {
        //        }
        //    }
        //}
    }
}
