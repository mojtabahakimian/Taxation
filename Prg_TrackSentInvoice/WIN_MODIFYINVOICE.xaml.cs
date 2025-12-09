using Dapper;
using Prg_Graphicy.LMethods;
using Prg_Graphicy.Wins;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.FUNCTIONS;
using Prg_Moadian.Generaly;
using Prg_Moadian.Service;
using Prg_Moadian.SQLMODELS;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TaxCollectData.Library.Business;
using static Prg_Graphicy.Payewin;
using static Prg_Moadian.Generaly.CL_Generaly;

namespace Prg_TrackSentInvoice
{
    public partial class WIN_MODIFYINVOICE : Window
    {
        CL_CCNNMANAGER dbms = new CL_CCNNMANAGER();
        CustomExceptErMsg CER = new CustomExceptErMsg();
        static CL_FUNTIONS TheFunctions = new CL_FUNTIONS();
        public ObservableCollection<TAXDTL> TAXDTL_DATA { get; set; } = new ObservableCollection<TAXDTL>();
        public List<VAHEDS> VAHEDHA { get; private set; }
        public string TAXID_PARAM { get; set; } // = "A2HGPP04F5E00227BB8965";
        public string IRTAXID_PARAM { get; set; }
        public string APITYPESENT_PARAM { get; set; } = "0";

        public bool SendHappenned { get; set; } = false;

        public string TaxURL { get; set; } = "https://sandboxrc.tax.gov.ir/req/api/";
        private string _memoryId = null;
        private string _privateKey = null;

        public bool IsSpecialF { get; set; } = false;

        public WIN_MODIFYINVOICE()
        {
            InitializeComponent();

            this.DataContext = this;
        }

        #region LOCAL_MODEL
        public class VAHEDS
        {
            public int IDD { get; set; }
            public string? NAME_MO { get; set; }
        }
        public class COMBOYMODEL
        {
            public string NAME { get; set; }
            public int ID { get; set; }
        }
        public class TobItem
        {
            public int CODE { get; set; }
            public string NAMES { get; set; }
        }
        #endregion

        #region Tools
        static class TaxMath
        {
            // رندینگ پولی به ریال: نیمه‌بالا (استاندارد مالی)
            public static decimal RoundIrr(decimal v) => Math.Round(v, 0); ////MidpointRounding.AwayFromZero

            // رندینگ تعداد/مقدار (طبق فیلدها تا 4 اعشار در UI شما)
            public static decimal RoundQty(decimal v) => Math.Round(v, 4);
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

        #region Audit
        private readonly Dictionary<string, Dictionary<string, string>> _snapshots = new(); // کلون مقدار اولیه هر سطر: PK->(Col->Val)
        private const string _auditTableName = "dbo.AuditTrail"; // اگر جدول دیگری می‌خواهی اینجا عوض کن
        private const string _auditTableHeader = "WIN_MODIFYINVOICE_HEADER";
        private const string _auditTableBody = "WIN_MODIFYINVOICE_BODY";
        private const string _auditTableSend = "WIN_MODIFYINVOICE_SEND";

        private static string _hostName = Dns.GetHostName();
        private static string _userName = Environment.UserName;
        private static string _module = "WIN_MODIFYINVOICE";

        // کمک‌متد: ساخت PK متنی واحد برای هر ردیف
        private static string MakePk(object idd) => $"IDD={idd}";

        // تبدیل مقدار به رشته کوتاه برای لاگ (NVARCHAR(4000))
        private static string ToScalar(object v)
        {
            if (v == null) return null;
            if (v is DateTime dt) return dt.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);
            if (v is decimal dec) return dec.ToString(CultureInfo.InvariantCulture);
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }
        private static string Trunc4000(string s) => string.IsNullOrEmpty(s) ? s : (s.Length <= 4000 ? s : s.Substring(0, 4000));

        // اسنپ‌شات از یک مدل TAXDTL (ستون‌ها=public props ساده، باینری/آرایه رد می‌شود)
        private static Dictionary<string, string> TakeSnapshot(TAXDTL r)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in typeof(TAXDTL).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead) continue;
                if (p.PropertyType == typeof(byte[])) continue;
                map[p.Name] = ToScalar(p.GetValue(r));
            }
            return map;
        }

        // درج یک رکورد در جدول لاگ (سطح برنامه)
        private void LogAudit(string table, string pk, char op, string col, string oldV, string newV, string context = null)
        {
            try
            {
                using (var conn = new SqlConnection(CL_CCNNMANAGER.CONNECTION_STR))
                {
                    conn.Open();
                    var ctx = context ?? $"{_userName}|{_hostName}|{_module}";
                    // جدول AuditTrail ستونی به نام AppContext دارد
                    const string sql = @"
INSERT INTO dbo.AuditTrail (SchemaName, TableName, PK, Operation, ColumnName, OldValue, NewValue, AppContext)
VALUES (N'APP', @TableName, @PK, @Operation, @ColumnName, @OldValue, @NewValue, @AppContext);";
                    conn.Execute(sql, new
                    {
                        TableName = table,
                        PK = pk,
                        Operation = op.ToString(),
                        ColumnName = col,
                        OldValue = (object)Trunc4000(oldV) ?? DBNull.Value,
                        NewValue = (object)Trunc4000(newV) ?? DBNull.Value,
                        AppContext = Trunc4000(ctx)
                    });
                }
            }
            catch { /* لاگ فایل اگر خواستی */ }
        }

        // محاسبه تفاوت‌ها و لاگِ U برای یک ردیف
        private void WriteAuditForRow(TAXDTL row, string table)
        {
            if (row == null) return;
            var key = MakePk(row.IDD);
            var now = TakeSnapshot(row);

            if (_snapshots.TryGetValue(key, out var old))
            {
                foreach (var kv in now)
                {
                    old.TryGetValue(kv.Key, out var ov);
                    var nv = kv.Value;
                    if ((ov ?? "§NULL§") != (nv ?? "§NULL§"))
                    {
                        LogAudit(table, key, 'U', kv.Key, ov, nv, $"{_module}|EDIT");
                    }
                }
            }
            else
            {
                // اگر قبلاً اسنپ‌شات نداشتیم، ورود اولیه فرض می‌شود
                foreach (var kv in now)
                    LogAudit(table, key, 'I', kv.Key, null, kv.Value, $"{_module}|LOAD");
            }

            _snapshots[key] = now; // به‌روزرسانی اسنپ‌شات
        }
        // لاگ حذف سطر بدنه فاکتور (Operation = 'D')
        private void WriteAuditDeleteRow(TAXDTL row)
        {
            if (row == null) return;

            // PK منطقی برای گزارش
            var pk = $"IDD={row.IDD}";
            var ctx = $"{Environment.UserName}|{Dns.GetHostName()}|WIN_MODIFYINVOICE|DELETE";

            // محتویات سطر قبل از حذف را به‌صورت JSON برای OldValue ذخیره کن (در حد 4000 کاراکتر)
            var json = JsonSerializer.Serialize(row, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
            if (json != null && json.Length > 4000) json = json.Substring(0, 4000);

            const string sql = @"
INSERT INTO dbo.AuditTrail
    (SchemaName, TableName, PK, Operation, ColumnName, OldValue, NewValue, AppContext)
VALUES
    (N'APP', N'WIN_MODIFYINVOICE_BODY', @PK, N'D', N'ALL', @OldValue, NULL, @AppContext);";

            try
            {
                using var conn = new SqlConnection(CL_CCNNMANAGER.CONNECTION_STR);
                conn.Open();
                conn.Execute(sql, new
                {
                    PK = pk,
                    OldValue = (object)json ?? DBNull.Value,
                    AppContext = ctx
                });
            }
            catch
            {
                // اگر خواستی اینجا لاگ فایل هم بنویس
            }
        }

        #endregion

        #endregion

        public decimal SumTprdis => TAXDTL_DATA.Sum(d => d.Prdis ?? 0);
        public decimal SumTdis => TAXDTL_DATA.Sum(d => d.Dis ?? 0);
        public decimal SumTadis => TAXDTL_DATA.Sum(d => d.Adis ?? 0);
        public decimal SumTvam => TAXDTL_DATA.Sum(d => d.Vam ?? 0);
        public decimal SumTbill => TAXDTL_DATA.Sum(d => d.Tsstam ?? 0);

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            AMALIAT();

            FILL_ALL_COMBOBOXES();

            ReGetData();
        }

        private void AMALIAT()
        {
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
            catch { /*ignore*/ }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                var IsFocusinsideDataGrids = DG_HEAD_INVOICE.IsKeyboardFocusWithin || DG_BODY_INVOICE.IsKeyboardFocusWithin;
                if (IsFocusinsideDataGrids)
                {
                    if (e.Key is Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
                    {
                        e.Handled = true;
                        CL_TOOLS.Send(Key.Tab);
                    }
                }
            }
            catch { }
        }
        private void FILL_ALL_COMBOBOXES()
        {
            VAHEDHA = dbms.DoGetDataSQL<VAHEDS>("SELECT IDD, NAME_MO FROM dbo.TCOD_VAHED_EXTENDED").ToList();

            //موضوع صورت حساب:
            INS_COLUMN.ItemsSource = new List<COMBOYMODEL>
            {
                new COMBOYMODEL { ID = 1, NAME = "اصلی" },
                new COMBOYMODEL { ID = 2, NAME = "اصلاحی" },
                new COMBOYMODEL { ID = 3, NAME = "ابطالی" },
                new COMBOYMODEL { ID = 4, NAME = "برگشت فروش" }
            };

            //روش تسویه:
            SETM_COLUMN.ItemsSource = new List<COMBOYMODEL>
            {
                new COMBOYMODEL { ID = 1, NAME = "نقد" },
                new COMBOYMODEL { ID = 2, NAME = "نسیه" },
                new COMBOYMODEL { ID = 3, NAME = "نقد/نسیه" }
            };

            //نوع شخص
            TOB_COLUMN.ItemsSource = new List<TobItem>
            {
                new TobItem { CODE = 1, NAMES = "حقیقی" },
                new TobItem { CODE = 2, NAMES = "حقوقی" },
                new TobItem { CODE = 3, NAMES = "مشارکت مدنی" },
                new TobItem { CODE = 4, NAMES = "اتباع غیر ایرانی" }
            };
        }
        private void ReGetData()
        {
            string SQLTEXT = $"SELECT Taxid, Inty, Inno, Irtaxid, Inp, Ins, Tins, Tob, Bid, Tinb, Sbc, Bpc, Ft, Bpn, Scln, Scc, Crn, Billid, Tprdis, Tdis, Tadis, Tvam, Todam, Tbill, Setm, Cap, Insp, Tvop, Tax17, Sstid, Sstt, Mu, Am, Fee, Cfee, Cut, Exr, Prdis, Dis, Adis, Vra, Vam, Odt, Odr, Odam, Olt, Olr, Olam, Consfee, Spro, Bros, Tcpbs, Cop, Vop, Bsrn, Tsstam, Iinn, Acn, Trmn, Trn, Pcn, Pid, Pdt, Cdcn, Cdcd, Tonw, Torv, Tocv, Nw, Ssrv, Sscv, Pmt, PV, IDD, RefrenceNumber, TheConfirmationReferenceId, TheError, TheStatus, TheSuccess, CRT, UID, SentTaxMemory, ApiTypeSent, Indatim_Sec, Indati2m_Sec, NUMBER, TAG, DATE_N, REMARKS FROM dbo.TAXDTL " +
                $"WHERE TheSuccess = {(IsSpecialF ? 0 : 1)} AND ApiTypeSent = {APITYPESENT_PARAM} AND Taxid = N'{TAXID_PARAM}'";

            if (IsSpecialF)
            {
                string? HeaderText = HEADER_LABEL?.Content?.ToString();
                HEADER_LABEL.Content = HeaderText + " - حالت ویژه فعال است ";
            }

            TAXDTL_DATA?.Clear();
            var RST = dbms.DoGetDataSQL<TAXDTL>(SQLTEXT).ToList();
            TAXID_LABEL.Text = " شماره مالیاتی صورت حساب اولیه ارسال شده " + RST.FirstOrDefault()?.Taxid;

            foreach (var item in RST)
            {
                TAXDTL_DATA?.Add(item);

                if (item?.Mu != null)
                {
                    item.Mu = decimal.Truncate(decimal.Parse(item.Mu, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
                    item.NAME_VAHED = VAHEDHA.Where(vi => vi.IDD == Convert.ToDouble(item.Mu)).FirstOrDefault()?.NAME_MO;
                }

                item.Irtaxid = item.Taxid;

                item.Taxid = null; //make it null to avoid any conflict and also let the app to get new one Taxid

                _snapshots[MakePk(item.IDD)] = TakeSnapshot(item);
            }

            GetHeaderSum();
        }

        //Head
        private void DG_HEAD_INVOICE_CANCEL_EDIT(DataGridEditingUnit? _RC_ = null)
        {
            DG_HEAD_INVOICE.Dispatcher.Invoke(() =>
            {
                //DG_BODY_INVOICE.CellEditEnding -= INVO_LST_SUB_CellEditEnding;
                DG_HEAD_INVOICE.RowEditEnding -= DG_HEAD_INVOICE_RowEditEnding;
                if (_RC_ is null)
                {
                    DG_HEAD_INVOICE.CancelEdit();
                }
                else
                {
                    DG_HEAD_INVOICE.CancelEdit((DataGridEditingUnit)_RC_);
                }
                DG_HEAD_INVOICE.RowEditEnding += DG_HEAD_INVOICE_RowEditEnding;
                //DG_BODY_INVOICE.CellEditEnding += INVO_LST_SUB_CellEditEnding;
            });
        }
        private void DG_HEAD_INVOICE_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Cancel) { return; }
            if (Keyboard.IsKeyDown(Key.Escape)) { return; }
            if (e.Row.Item == null) { return; }

            if (e.Row.Item is TAXDTL ROW)
            {
                if (ROW?.Inno?.Length < 10)
                {
                    ////TheFunctions.InnoAddZeroes();
                    new Msgwin(false, "سریال صورت حساب باید حداقل 10 رقم داشته باشد , من صفر به ابتدای آن اضافه کردم تا این مشکل رفع شود").Show();
                    DG_HEAD_INVOICE_CANCEL_EDIT();
                }

                if (ROW?.Ins == 4) //برگشتی
                {
                    var originalTaxDtl = dbms.DoGetDataSQL<TAXDTL>($"SELECT TOP 1 Am FROM TAXDTL WHERE IDD = {ROW.IDD} AND Sstid = '{ROW.Sstid}'").FirstOrDefault();

                    if (ROW.Am > originalTaxDtl.Am)
                    {
                        new Msgwin(false, "مقدار در صورتحساب برگشتی نمی‌تواند از مقدار اصلی بیشتر باشد").Show();
                        DG_HEAD_INVOICE_CANCEL_EDIT();
                        return;
                    }
                }

                WriteAuditForRow(ROW, _auditTableHeader);
            }
        }

        //Body
        private void DG_BODY_INVOICE_CANCEL_EDIT(DataGridEditingUnit? _RC_ = null)
        {
            DG_BODY_INVOICE.Dispatcher.Invoke(() =>
            {
                //DG_BODY_INVOICE.CellEditEnding -= INVO_LST_SUB_CellEditEnding;
                DG_BODY_INVOICE.RowEditEnding -= DG_BODY_INVOICE_RowEditEnding;
                if (_RC_ is null)
                {
                    DG_BODY_INVOICE.CancelEdit();
                }
                else
                {
                    DG_BODY_INVOICE.CancelEdit((DataGridEditingUnit)_RC_);
                }
                DG_BODY_INVOICE.RowEditEnding += DG_BODY_INVOICE_RowEditEnding;
                //DG_BODY_INVOICE.CellEditEnding += INVO_LST_SUB_CellEditEnding;
            });
        }
        private void DG_BODY_INVOICE_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key is Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
            {
                try
                {
                    if (e.OriginalSource is TextBox textBox && !textBox.IsReadOnly)
                    {
                        return;
                    }
                }
                catch { }

                if (DG_BODY_INVOICE.SelectedItem is TAXDTL SelectedBodyRow)
                {
                    Msgwin msgwin = new Msgwin(true, "آیا از حذف این سطر مطمئن هستید ؟");
                    msgwin.ShowDialog();
                    if (msgwin.DialogResult != true)
                    {
                        e.Handled = true;
                        return;
                    }

                    WriteAuditDeleteRow(SelectedBodyRow);

                    ReCalculateTotals();
                }
            }
        }
        private void DG_BODY_INVOICE_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Cancel) { return; }
            if (Keyboard.IsKeyDown(Key.Escape)) { return; }
            if (e.Row.Item == null) { return; }

            ReCalculateTotals();


            if (e.Row.Item is TAXDTL bodyRow)
            {
                WriteAuditForRow(bodyRow, _auditTableBody);

                if (bodyRow.Ins == 3) //ابطالی
                {
                    DG_BODY_INVOICE.IsEnabled = false;
                }
                else
                {
                    DG_BODY_INVOICE.IsEnabled = true;
                }
            }

        }

        private bool TotalsAreConsistent(out string message)
        {
            var vs = SumTadis;  // Vs = مجموع بعد از تخفیف (TADIS)
            var ks = SumTvam;   // Ks = مجموع VAT (TVAM)
            var xs = SumTbill;  // Xs = Σ(Adis+Vam) = TBILL شما

            // 1) فرمول اصلی VAT در هر ردیف رعایت شده باشد: Vam == Round(Adis*Vra/100)
            foreach (var r in TAXDTL_DATA)
            {
                var adis = (r.Adis ?? 0);
                var vra = (r.Vra ?? 0);
                var expectVam = TaxMath.RoundIrr(vra == 0 ? 0 : adis * vra / 100m);
                if ((r.Vam ?? 0) != expectVam)
                {
                    message = $"اختلاف رندینگ VAT در ردیف با شناسه {r.IDD}.";
                    return false;
                }
            }

            // 2) جمع‌ها همخوان باشند: TBILL = Σ(Adis+Vam)
            var sumItemAdisPlusVam = TAXDTL_DATA.Sum(r => (r.Adis ?? 0) + (r.Vam ?? 0));
            if (xs != sumItemAdisPlusVam)
            {
                message = "مجموع صورتحساب TBILL با Σ(مبلغ بعد از تخفیف Adis + مبلغ مالیات Vam) همخوان نیست.";
                return false;
            }

            // 3) Vs = Σ(Adis) و Ks = Σ(Vam)
            if (vs != TAXDTL_DATA.Sum(r => r.Adis ?? 0))
            {
                message = "مجموع مبلغ پس از کسر تخفیف Tadis با Σ(مبلغ بعد از تخفیف Adis) همخوان نیست.";
                return false;
            }
            if (ks != TAXDTL_DATA.Sum(r => r.Vam ?? 0))
            {
                message = "مجموع مالیات Tvam با Σ(مبلغ مالیات Vam) همخوان نیست.";
                return false;
            }

            message = null;
            return true;
        }

        private void ReCalculateTotals()
        {
            if (TAXDTL_DATA == null) return;

            foreach (var item in TAXDTL_DATA)
            {
                // محاسبات هر سطر

                #region Goods
                ////محاسبه مجدد مبالغ جهت رفع اعشار
                item.Am = Math.Round((decimal)item.Am, 4); //تعداد/مقدار //MEGHk
                item.Fee = Math.Truncate((decimal)item.Fee); //مبلغ واحد //MABL
                item.Dis = Math.Truncate((decimal)item.Dis); //مبلغ تخفیف //N_MOIN

                var MABL_K = Math.Truncate((decimal)(item.Am * item.Fee));
                item.Prdis = MABL_K; //مبلغ قبل از تخفیف //MABL_K

                item.Adis = item.Prdis - (item.Dis ?? 0); //مبلغ بعد از تخفیف //(item.MABL_K - item.N_MOIN), //مبلغ بعد از تخفیف

                var IMBAA = Math.Truncate((decimal)(item.Adis * (item.Vra ?? 0) / 100)); //مبلغ مالیات بر ارزش افزوده //IMBAA	
                item.Vam = IMBAA; //مبلغ مالیات بر ارزش افزوده //IMBAA	

                item.Tsstam = item.Adis + item.Vam; //مبلغ کل کالا/خدمت
                #endregion

                #region Cleaning_RestoreValiding
                if (string.IsNullOrEmpty(item?.Crn) || item?.Crn == "0")
                {
                    if (item?.Crn != null)
                    {
                        item.Crn = null; //شناسه یکتای ثبت قرار داد فروشنده
                    }
                }

                ////if (item?.Irtaxid != null) //جلوگیری از مقدار خالی یا Space
                ////{
                ////    if (string.IsNullOrWhiteSpace(item?.Irtaxid))
                ////    {
                ////        if (item?.Inp != 7) //الگوی صورتحساب => صادرات نیست
                ////        {
                ////            item.Irtaxid = null;
                ////        }
                ////    }
                ////}

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
                #endregion

            }

            //GetSumHead
            GetHeaderSum();
        }
        private void GetHeaderSum()
        {
            foreach (var row in TAXDTL_DATA)
            {
                row.Tprdis = SumTprdis; //مجموع مبلغ قبل از کسر تخفیف
                row.Tdis = SumTdis; //مجموع تخفیفات
                row.Tadis = SumTadis; // مجموع مبلغ پس از کسر تخفیف
                row.Tvam = SumTvam; // مجموع مالیات بر ارزش افزوده
                row.Tbill = SumTbill; //مجموع صورتحساب
            }

            // به‌روزرسانی نمایشگرهای جمع کل
            TotalAmountText.Text = $"جمع کل: {TAXDTL_DATA.First().Tprdis:N0}" + " ریال "; //totalAmount
            TotalDiscountText.Text = $"جمع تخفیف: {TAXDTL_DATA.First().Tdis:N0}" + " ریال "; //totalDiscount
            TotalVatText.Text = $"جمع مالیات: {TAXDTL_DATA.First().Tvam:N0}" + " ریال "; //totalVat
            GrandTotalText.Text = $"مبلغ نهایی: {TAXDTL_DATA.First().Tbill:N0}" + " ریال "; //grandTotal
        }

        private void LoadConfig()
        {
            // SAZMAN: گرفتن کلید خصوصی و حافظه
            var saz = dbms.DoGetDataSQL<SAZMAN>("SELECT TOP 1 MOADINA_SCNUM , YEA , MEMORYID, MEMORYIDsand, PRIVIATEKEY, Dcertificate, ECODE FROM dbo.SAZMAN").FirstOrDefault();
            if (saz == null) throw new Exception("SAZMAN not configured.");

            _privateKey = saz.PRIVIATEKEY?.Replace("-----BEGIN PRIVATE KEY-----\r\n", "").Replace("\r\n-----END PRIVATE KEY-----\r\n", "").Trim();

            _memoryId = (TaxURL == "https://tp.tax.gov.ir/req/api/") ? saz.MEMORYID?.Trim() : saz.MEMORYIDsand?.Trim();
        }

        //Final Sending ----------------------------------------------
        private void BTN_SEND_Click(object sender, RoutedEventArgs e)
        {
            bool IsEbtali = false; //آیا ابطالی است == موضوع صورت حساب Ins
            if (DG_HEAD_INVOICE.SelectedItem is TAXDTL SelectedHeadItem) //Head
            {
                if (TAXDTL_DATA == null || SelectedHeadItem.Ins <= 0)
                {
                    new Msgwin(false, "لطفاً یک صورتحساب و نوع عملیات را انتخاب کنید.").Show();
                    return;
                }
                IsEbtali = SelectedHeadItem.Ins == 3;
                if (TAXDTL_DATA.Count == 0 && !IsEbtali)
                {
                    new Msgwin(false, "برای صورتحساب اصلاحی یا برگشتی باید حداقل یک قلم کالا وجود داشته باشد.").Show();
                    return;
                }
                if (TAXDTL_DATA.Any(x => string.IsNullOrWhiteSpace(x.Inno) || string.IsNullOrEmpty(x.Inno)))
                {
                    new Msgwin(false, "سریال جدید (Inno) را وارد کنید.").Show();
                    return;
                }
                if (TAXDTL_DATA.Any(x => string.IsNullOrWhiteSpace(x.Irtaxid) || string.IsNullOrEmpty(x.Irtaxid)))
                {
                    new Msgwin(false, "شماره صورت حساب مرجع (Irtaxid) را وارد کنید.").Show();
                    return;
                }
            }

            Msgwin msgwin1 = new Msgwin(true, $"آیا از ارسال صورت حساب از نوع [{GetMessageBasedOnId((int)TAXDTL_DATA.FirstOrDefault().Ins)}] با مقادیر انتخاب شده مطمئن هستید ؟ ");
            msgwin1.ShowDialog();
            if (msgwin1.DialogResult != true)
            {
                return;
            }

            ReCalculateTotals();

            if (!TotalsAreConsistent(out var why))
            {
                new Msgwin(false, "قواعد جمع/رُندینگ رعایت نشده: " + why).Show();
                return;
            }

            AMALIAT();

            try
            {
                LoadConfig();

                #region TMPTEST
                //try
                //{
                //    var serverInfo = TaxApiService.Instance.TaxApis.GetServerInformation();
                //    if (serverInfo?.ServerTime != null)
                //    {
                //        TimeSync.SyncWithServer(serverInfo.ServerTime);
                //    }
                //}
                //catch { }
                #endregion

                //// TaxService آماده
                var taxService = new TaxService(_memoryId, _privateKey, TaxURL);
                var _ = taxService.RequestToken(); //// اعتبارسنجی

                //// تاریخ و TaxId جدید
                var iranTZ = TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time");
                var serverInfo = TaxApiService.Instance.TaxApis.GetServerInformation();

                var serverUtcNow = serverInfo != null
                    ? DateTimeOffset.FromUnixTimeMilliseconds(serverInfo.ServerTime).UtcDateTime
                    : DateTime.UtcNow + TokenLifeTime.ServerClockSkew;

                var now = TimeZoneInfo.ConvertTimeFromUtc(serverUtcNow, iranTZ);

                var taxidNew = taxService.RequestTaxId(_memoryId, now);
                long indatim = TaxService.ConvertDateToLong(now);
                long indatim2 = TaxService.ConvertDateToLong(now);

                ////indatim = TimeSync.GetMoadianTimestamp();
                ////indatim2 = TimeSync.GetMoadianTimestamp();

                //بروز رسانی آیتم های حیاتی تغییر یافته برای ارسال جدید:
                foreach (var taxrow in TAXDTL_DATA)
                {
                    taxrow.Taxid = taxidNew;
                    taxrow.Indatim_Sec = indatim;
                    taxrow.Indati2m_Sec = indatim2;
                }

                var HeadFirst = TAXDTL_DATA.First() as TAXDTL;

                // هدر جدید
                #region PrepareModel
                TaxModel.InvoiceModel.Header header = new TaxModel.InvoiceModel.Header();
                header.Taxid = taxidNew; //شماره منحصر به فرد مالیاتی
                header.Indatim = indatim; //تاریخ و زمان صدور صورتحساب (میلادی)
                header.Indati2m = indatim2; //تاریخ و زمان ایجاد صورتحساب (میلادی)
                header.Inty = Convert.ToInt32(HeadFirst.Inty); //(انواع صورتحساب الکترونیکی 1و2و3) نوع صورتحساب
                header.Inno = HeadFirst.Inno; //سریال صورتحساب  //NUMBER	 HEAD_LST
                header.Irtaxid = HeadFirst.Irtaxid; //شماره منحصر به فرد مالیاتی صورتحساب مرجع
                header.Inp = Convert.ToInt32(HeadFirst.Inp); //الگوی صورتحساب
                header.Ins = Convert.ToInt32(HeadFirst.Ins); //موضوع صورتحساب
                header.Tins = HeadFirst.Tins; //شماره اقتصادی فروشنده //ECODE SAZMAN
                header.Tob = Convert.ToInt32(HeadFirst.Tob); //نوع شخص خریدار
                header.Bid = HeadFirst.Bid; //شماره/شناسه ملی/شناسه مشارکت مدنی/کد فراگیر خریدار //MCODEM	SAZMAN
                header.Tinb = HeadFirst.Tinb; //شماره اقتصادی خریدار //ECODE CUST_HESAB
                header.Sbc = HeadFirst.Sbc; //کد شعبه فروشنده //MCODEM	CUST_HESAB
                header.Bbc = HeadFirst.Bbc; //کد شعبه خریدار
                header.Bpc = HeadFirst.Bpc; //کد پستی خریدار
                header.Ft = Convert.ToInt32(HeadFirst.Ft); //نوع پرواز
                                                           ////header.Bpn = HeadFirst.Bpn; //شماره گذرنامه خریدار
                header.Scln = HeadFirst.Scln; //شماره پروانه گمرکی فروشنده
                                              ////header.Scc = HeadFirst.Scc; //کد گمرک محل اظهار
                header.Crn = HeadFirst.Crn; //شناسه یکتای ثبت قرارداد فروشنده
                header.Billid = HeadFirst.Billid; //شماره اشتراک/شناسه قبض بهره بردار
                header.Tprdis = Convert.ToDecimal(HeadFirst.Tprdis); //مجموع مبلغ قبل از کسر تخفیف //INVO_LST	Sum(MABL_K)
                header.Tdis = Convert.ToDecimal(HeadFirst.Tdis); //مجموع تخفیفات //INVO_LST	Sum(N_MOIN)
                header.Tadis = Convert.ToDecimal(HeadFirst.Tadis); //مجموع مبلغ پس از کسر تخفیف //INVO_LST 	Sum(INVO_LST.MABL_K - INVO_LST.N_MOIN AS mabkbt)
                header.Tvam = Convert.ToDecimal(HeadFirst.Tvam); //مجموع مالیات بر ارزش افزوده //INVO_LST	Sum(IMBAA)
                header.Todam = Convert.ToDecimal(HeadFirst.Todam); //مجموع سایر مالیات , عوارض و وجوه قانونی
                header.Tbill = Convert.ToDecimal(HeadFirst.Tbill); //مجموع صورت حساب //mabkn	INVO_LST.MABL_K-INVO_LST.N_MOIN+INVO_LST.IMBAA AS mabkn
                header.Setm = Convert.ToInt32(HeadFirst.Setm); //روش تسویه
                header.Cap = Convert.ToDecimal(HeadFirst.Cap); //مبلغ پرداختی نقدی
                header.Insp = Convert.ToDecimal(HeadFirst.Insp); //مبلغ پرداختی نسیه
                header.Tvop = Convert.ToDecimal(HeadFirst.Tvop); //مجموع سهم مالیات بر ارزش افزوده از پرداخت
                header.Tax17 = Convert.ToDecimal(HeadFirst.Tax17); //مالیات موضوع ماده 17
                header.Cdcd = Convert.ToInt32(HeadFirst.Cdcd); //تاریخ کوتاژ اظهارنامه گمرکی
                header.Tonw = Convert.ToDecimal(HeadFirst.Tonw); //مجموع وزن خالص
                header.Torv = Convert.ToDecimal(HeadFirst.Torv); //مجموع ارزش ریالی
                header.Tocv = Convert.ToDecimal(HeadFirst.Tocv); //مجموع ارزش ارزی

                List<TaxModel.InvoiceModel.Body>? bodies = new List<TaxModel.InvoiceModel.Body>();

                if (!IsEbtali) // اگر ابطالی نیست
                {
                    foreach (var item in TAXDTL_DATA)
                    {
                        bodies.Add(new TaxModel.InvoiceModel.Body
                        {
                            Sstid = item.Sstid, //شناسه کالا/خدمت //CODE	STUF_DEF      
                            Sstt = item.Sstt, //شرح کالا/خدمت //NAME	STUF_DEF
                            Mu = item.Mu, //واحد اندازه گیری //VNAMES	TCOD_VAHEDS
                            Am = Convert.ToDecimal(item.Am),//تعداد/مقدار //MEGH	INVO_LST
                            Fee = Convert.ToDecimal(item.Fee), //مبلغ واحد //MABL	INVO_LST
                            Cfee = Convert.ToDecimal(item.Cfee), //میزان ارز
                            Cut = item.Cut, //نوع ارز
                            Exr = Convert.ToDecimal(item.Exr), //نرخ برابری ارز با ریال
                            Prdis = Convert.ToDecimal(item.Prdis), //مبلغ قبل از تخفیف //MABL_K	INVO_LST
                            Dis = Convert.ToDecimal(item.Dis), //مبلغ تخفیف //N_MOIN	INVO_LST
                            Adis = Convert.ToDecimal(item.Adis), //مبلغ بعد از تخفیف //Sum(INVO_LST.MABL_K - INVO_LST.N_MOIN AS mabkbt)	INVO_LST
                            Vra = Convert.ToDecimal(item.Vra), //نرخ مالیات بر ارزش افزوده
                            Vam = Convert.ToDecimal(item.Vam), //مبلغ مالیات بر ارزش افزوده //IMBAA	 INVO_LST
                            Odt = item.Odt, //موضوع سایر مالیات و عوارض
                            Odr = Convert.ToDecimal(item.Odr), //نرخ سایر مالیات و عوارض
                            Odam = Convert.ToDecimal(item.Odam), //مبلغ سایر مالیات و عوارض
                            Olt = item.Olt, //موضوع سایر وجوه قانونی
                            Olr = Convert.ToDecimal(item.Olr), //نرخ سایر وجوه قانونی
                            Olam = Convert.ToDecimal(item.Olam), //مبلغ سایر وجوه قانونی
                            Consfee = Convert.ToDecimal(item.Consfee), //اجرت ساخت
                            Spro = Convert.ToDecimal(item.Spro), //سود فروشنده
                            Bros = Convert.ToDecimal(item.Bros), //حق العمل
                            Tcpbs = Convert.ToDecimal(item.Tcpbs), //جمع کل اجرت , حق العمل و سود
                            Cop = Convert.ToDecimal(item.Cop), //سهم نقدی از پرداخت
                            Vop = Convert.ToDecimal(item.Vop), //سهم ارزش افزوده از پرداخت
                            Bsrn = item.Bsrn, //شناسه یکتای ثبت قرارداد حق العملکاری
                            Tsstam = Convert.ToDecimal(item.Tsstam), //مبلغ کل کالا/خدمت //MABL_K	INVO_LST
                            Nw = Convert.ToDecimal(item.Nw), //وزن خالص
                            Ssrv = Convert.ToDecimal(item.Ssrv), //ارزش ریالی کالا
                            Sscv = Convert.ToDecimal(item.Sscv) //ارزش ارزی کالا
                        });
                    }
                }


                List<TaxModel.InvoiceModel.Payment>? payments = new List<TaxModel.InvoiceModel.Payment>();
                #endregion

                #region JSON_LOG
                try
                {
                    string directoryPath = @"C:\CORRECT\SENTS";
                    if (!Directory.Exists(directoryPath))
                    {
                        Directory.CreateDirectory(directoryPath);
                    }
                    var jsonObject = new
                    {
                        Header = header,
                        Bodies = bodies,
                        Payments = payments
                    };
                    string jsonData = JsonSerializer.Serialize(jsonObject);
                    string combinedFilePath = System.IO.Path.Combine(directoryPath, $"{DateTime.Now.ToString("yyyy-mm-dd-ss-fff")}-Combined.json");
                    File.WriteAllText(combinedFilePath, jsonData);
                }
                catch { }
                #endregion

                TaxModel.SendInvoicesModel sendInvoicesModel = taxService.SendInvoices(header, bodies, payments);

                //بروز رسانی کد های رهگیری در لیست سی شارپ
                for (int i = 0; i < TAXDTL_DATA.Count; i++)
                {
                    TAXDTL_DATA[i].UID = sendInvoicesModel.Uid;
                    TAXDTL_DATA[i].RefrenceNumber = sendInvoicesModel.ReferenceNumber;
                }

                //به کدام سامانه ارسال شده
                byte _apitypesent = 0;
                if (TaxURL is "https://tp.tax.gov.ir/req/api/") // اگر روی اصلیه
                    _apitypesent = 1;
                else
                    _apitypesent = 0;

                CL_Generaly.WriteRecordText($@"
                                             -----------------------------------------------------------------
                                             زمان : {DateTime.Now}
                                             شماره فاکتور : {HeadFirst?.NUMBER}
                                             شماره مالیاتی صورت حساب : {sendInvoicesModel.TaxId}
                                             شناسه (رهگیری) صورت حساب مودیان: {sendInvoicesModel.ReferenceNumber}
                                             -----------------------------------------------------------------");

                try
                {
                    var head = TAXDTL_DATA.FirstOrDefault();
                    var pk = head != null ? $"Taxid={head.Taxid}|NUMBER={head.NUMBER}" : $"Taxid={TAXID_PARAM}";
                    LogAudit(_auditTableSend, pk, 'I', "ACTION", null, "SEND_CLICK", $"{_module}|SEND");

                    // خلاصهٔ مجموع‌ها هم به‌عنوان تغییر وضعیت لاگ شود (اختیاری)
                    LogAudit(_auditTableSend, pk, 'U', "SUM_TPRDIS", null, SumTprdis.ToString(CultureInfo.InvariantCulture));
                    LogAudit(_auditTableSend, pk, 'U', "SUM_TADIS", null, SumTadis.ToString(CultureInfo.InvariantCulture));
                    LogAudit(_auditTableSend, pk, 'U', "SUM_TVAM", null, SumTvam.ToString(CultureInfo.InvariantCulture));
                    LogAudit(_auditTableSend, pk, 'U', "SUM_TBILL", null, SumTbill.ToString(CultureInfo.InvariantCulture));
                }
                catch { /*ignore*/ }

                try
                {
                    //{ درج در جدول مالیات در دیتابیس 
                    foreach (var src_item in TAXDTL_DATA)
                    {
                        var IDD_OF_TAXDTL = TheFunctions.GetNewIDD();

                        const string insertSql = @"INSERT INTO dbo.TAXDTL (
Taxid, Indatim, Indati2m, Indatim_Sec, Indati2m_Sec, Inty, Inno, Irtaxid, Inp, Ins, Tins, Tob, Bid, Tinb, Sbc, Bpc, Ft, Bpn, Scln, Scc, Crn, Billid, Tprdis, Tdis, Tadis, Tvam, Todam, Tbill, Setm, Cap, Insp, Tvop, Tax17, Cdcd, Tonw, Torv, Tocv, Sstid, Sstt, Mu, Am, Fee, Cfee, Cut, Exr, Prdis, Dis, Adis, Vra, Vam, Odt, Odr, Odam, Olt, Olr, Olam, Consfee, Spro, Bros, Tcpbs, Cop, Vop, Bsrn, Tsstam, Nw, Ssrv, Sscv, IDD, UID, RefrenceNumber, TheStatus, ApiTypeSent, SentTaxMemory, NUMBER, TAG, DATE_N)
VALUES (@Taxid, @Indatim, @Indati2m, @Indatim_Sec, @Indati2m_Sec, @Inty, @Inno, @Irtaxid, @Inp, @Ins, @Tins, @Tob, @Bid, @Tinb, @Sbc, @Bpc, @Ft, @Bpn, @Scln, @Scc, @Crn, @Billid, @Tprdis, @Tdis, @Tadis, @Tvam, @Todam, @Tbill, @Setm, @Cap, @Insp, @Tvop, @Tax17, @Cdcd, @Tonw, @Torv, @Tocv, @Sstid, @Sstt, @Mu, @Am, @Fee, @Cfee, @Cut, @Exr, @Prdis, @Dis, @Adis, @Vra, @Vam, @Odt, @Odr, @Odam, @Olt, @Olr, @Olam, @Consfee, @Spro, @Bros, @Tcpbs, @Cop, @Vop, @Bsrn, @Tsstam, @Nw, @Ssrv, @Sscv, @IDD, @UID, @RefrenceNumber, @TheStatus, @ApiTypeSent, @SentTaxMemory, @NUMBER, @TAG, @DATE_N);";

                        var p = new
                        {
                            Taxid = CL_MOADIAN.SafeString(taxidNew, 22),
                            Indatim = (DateTime?)null,
                            Indati2m = (DateTime?)null,
                            src_item.Indatim_Sec,
                            src_item.Indati2m_Sec,
                            src_item.Inty,
                            Inno = CL_MOADIAN.SafeString(src_item.Inno, 10),
                            Irtaxid = CL_MOADIAN.SafeString(src_item.Irtaxid, 22), //src_item.Irtaxid
                            src_item.Inp,
                            src_item.Ins,
                            Tins = CL_MOADIAN.SafeString(src_item.Tins, 14),
                            src_item.Tob,
                            Bid = CL_MOADIAN.SafeString(src_item.Bid, 12),
                            Tinb = CL_MOADIAN.SafeString(src_item.Tinb, 14) ?? string.Empty,
                            Sbc = CL_MOADIAN.SafeString(src_item.Sbc, 10),
                            Bpc = CL_MOADIAN.SafeString(src_item.Bpc, 10),
                            src_item.Ft,
                            Bpn = CL_MOADIAN.SafeString(src_item.Bpn, 9),
                            Scln = CL_MOADIAN.SafeString(src_item.Scln, 14),
                            Scc = CL_MOADIAN.SafeString(src_item.Scc, 5),
                            Crn = CL_MOADIAN.SafeString(src_item.Crn, 12),
                            Billid = CL_MOADIAN.SafeString(src_item.Billid, 19),
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
                            Sstid = CL_MOADIAN.SafeString(src_item.Sstid, 13),
                            Sstt = CL_MOADIAN.SafeString(src_item.Sstt, 400),
                            Mu = CL_MOADIAN.SafeDecimal(src_item.Mu),
                            src_item.Am,
                            src_item.Fee,
                            src_item.Cfee,
                            Cut = CL_MOADIAN.SafeString(src_item.Cut, 3),
                            src_item.Exr,
                            src_item.Prdis,
                            src_item.Dis,
                            src_item.Adis,
                            src_item.Vra,
                            src_item.Vam,
                            Odt = CL_MOADIAN.SafeString(src_item.Odt, 255),
                            src_item.Odr,
                            src_item.Odam,
                            Olt = CL_MOADIAN.SafeString(src_item.Olt, 255),
                            src_item.Olr,
                            src_item.Olam,
                            src_item.Consfee,
                            src_item.Spro,
                            src_item.Bros,
                            src_item.Tcpbs,
                            src_item.Cop,
                            src_item.Vop,
                            Bsrn = CL_MOADIAN.SafeString(src_item.Bsrn, 12),
                            src_item.Tsstam,
                            src_item.Nw,
                            src_item.Ssrv,
                            src_item.Sscv,
                            IDD = IDD_OF_TAXDTL,
                            UID = CL_MOADIAN.SafeString(src_item.UID, 100),
                            RefrenceNumber = CL_MOADIAN.SafeString(src_item.RefrenceNumber, 100),
                            TheStatus = "PENDING",
                            ApiTypeSent = _apitypesent,
                            SentTaxMemory = CL_MOADIAN.SafeString(_memoryId, 12),
                            src_item?.NUMBER,
                            src_item?.TAG,
                            DATE_N = src_item?.DATE_N
                        };

                        dbms.DoExecuteSQL(insertSql, p);
                    }
                    //آماده سازی داده ها }

                    Msgwin msgwin = new Msgwin(false, $"صورت حساب [{GetMessageBasedOnId((int)(HeadFirst?.Ins))}] با شماره {HeadFirst?.NUMBER} به کد دهگیری :  {sendInvoicesModel.ReferenceNumber} " +
                        $"\n به شماره صورت حساب مالیاتی (شماره مالیاتی) : {taxidNew}\n در صف ارسال قرار گرفت , لطفا مدتی بعد بررسی بفرمایید.");
                    msgwin.ShowDialog();

                    TrytoGetInvoiceStatus(sendInvoicesModel.ReferenceNumber);
                }
                catch (Exception ex)
                {
                    CL_Generaly.DoGetwriteAppenLog($"Message : {ex.Message} \n\n {ex}");
                    throw new NullyExceptiony("Invoice Sent but could not save it to db");
                }
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


            SendHappenned = true;

            BTN_SEND.IsEnabled = false;
        }

        public string GetMessageBasedOnId(int id)
        {
            switch (id)
            {
                case 1:
                    return "اصلی";
                case 2:
                    return "اصلاحی";
                case 3:
                    return "ابطالی";
                case 4:
                    return "برگشت فروش";
                default:
                    return "شناسه نامعتبر";
            }
        }
        private void TrytoGetInvoiceStatus(string _ReferenceNumber_)
        {
            #region JustTryToEstelam
            try
            {
                string _msger = null;
                //var _qre0 = dbms.DoGetDataSQL<ES1>($"SELECT TheError,TheStatus FROM dbo.TAXDTL WHERE TheStatus <> N'PENDING' AND TheError <> N'' AND RefrenceNumber = N'{FactorInfoSent.ReferenceNumber}' ").ToList();
                var _qre0 = dbms.DoGetDataSQL<ES1>($"SELECT TheError,TheStatus FROM dbo.TAXDTL WHERE RefrenceNumber = N'{_ReferenceNumber_}' ").ToList();
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
}
