using Prg_Graphicy.Wins;
using Prg_Grpsend.Utility;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.SQLMODELS;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Syncfusion.Data.Extensions;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.ScrollAxis;
using Syncfusion.UI.Xaml.BulletGraph;
using System.Windows.Controls;
using System.Windows.Input;
using System.ComponentModel;
using Prg_Moadian.FUNCTIONS;
using static Prg_Moadian.FUNCTIONS.CL_FUNTIONS;
using System.Collections.Generic;
using Prg_Graphicy.LMethods;
using Prg_Grpsend.MODEL;
using System.Collections.Specialized;
using System.Threading.Tasks;
using Prg_Moadian.Generaly;
using Prg_Moadian.Bulk;

namespace Prg_Grpsend
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        private bool _IsEnabledIndicator = false;
        public bool IsEnabledIndicator
        {
            get { return _IsEnabledIndicator; }
            set
            {
                if (_IsEnabledIndicator == value) return;
                _IsEnabledIndicator = value;
                OnPropertyChanged(nameof(IsEnabledIndicator));
            }
        }

        private int _selectedItemsCount = 0;
        public int SelectedItemsCount
        {
            get => _selectedItemsCount;
            set
            {
                if (_selectedItemsCount == value) return;
                _selectedItemsCount = value;
                OnPropertyChanged(nameof(SelectedItemsCount));
            }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); }
        }

        private double _progress = 0;
        public double Progress // ۰ … ۱۰۰
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(); }
        }

        CL_CCNNMANAGER dbms = new CL_CCNNMANAGER();
        public MainWindow()
        {
            InitializeComponent();

            this.DataContext = this;

            if (FACTOR_DATA != null) // اطمینان از اینکه FACTOR_DATA null نیست
            {
                FACTOR_DATA.CollectionChanged += FACTOR_DATA_OnCollectionChanged;
            }
        }
        // متد برای اشتراک در PropertyChanged همه آیتم‌های موجود در یک کالکشن
        private void SubscribeToAllItems(IEnumerable<HEAD_LST> items)
        {
            if (items == null) return;
            foreach (var item in items)
            {
                if (item != null) // اطمینان از اینکه خود آیتم null نیست
                {
                    item.PropertyChanged -= HeadLstItem_OnPropertyChanged; // ابتدا لغو اشتراک برای جلوگیری از اشتراک چندباره
                    item.PropertyChanged += HeadLstItem_OnPropertyChanged;
                }
            }
        }
        // متد برای لغو اشتراک از PropertyChanged همه آیتم‌های موجود در یک کالکشن
        private void UnsubscribeFromAllItems(IEnumerable<HEAD_LST> items)
        {
            if (items == null) return;
            foreach (var item in items)
            {
                if (item != null)
                {
                    item.PropertyChanged -= HeadLstItem_OnPropertyChanged;
                }
            }
        }
        private void FACTOR_DATA_OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // وقتی آیتمی از کالکشن حذف می‌شود، اشتراک PropertyChanged آن را لغو کن
            if (e.OldItems != null)
            {
                foreach (HEAD_LST item in e.OldItems)
                {
                    if (item != null) item.PropertyChanged -= HeadLstItem_OnPropertyChanged;
                }
            }

            // وقتی آیتمی به کالکشن اضافه می‌شود، به PropertyChanged آن مشترک شو
            if (e.NewItems != null)
            {
                foreach (HEAD_LST item in e.NewItems)
                {
                    if (item != null) item.PropertyChanged += HeadLstItem_OnPropertyChanged;
                }
            }

            // صرف نظر از اینکه چه تغییری رخ داده (اضافه، حذف، پاک شدن)، تعداد را به‌روز کن
            UpdateSelectedCount();
        }
        private void HeadLstItem_OnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // اگر پراپرتی IsSelected در یکی از آیتم‌ها تغییر کرد
            if (e.PropertyName == nameof(HEAD_LST.IsSelected))
            {
                UpdateSelectedCount();
            }
        }


        // فلگ برای جلوگیری از فراخوانی‌های تودرتو و غیرضروری
        private bool _isUpdatingSelectionByRange = false;
        private void Window_ContentRendered(object sender, EventArgs e)
        {
            NowIsReady = true;
        }
        public ObservableCollection<HEAD_LST> FACTOR_DATA { get; set; } = new ObservableCollection<HEAD_LST>();
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Logation.AMALIYAT_USER(this.GetType().Name);

            // چک کردن دسترسی
            var Result = Saftey.SETSECURITY("HEAD_LST_FROOSH");
            if (!string.IsNullOrEmpty(Result))
            {
                _ = new Msgwin(false, Result).ShowDialog();
                this.Close();
            }
            else
            {
                bool CanSeeAllInvoice = Saftey.LETSGO("FRSKB"); //فاکتور فروش سایر کاربران را بتواند ببیند
                if (!CanSeeAllInvoice)
                {
                    _ = new Msgwin(false, "شما دسترسی به فاکتور فروش سایر کاربران را ندارید").ShowDialog();
                    this.Close();
                }
            }

            var _YEA_ = dbms.DoGetDataSQL<string>("SELECT TOP 1 YEA FROM dbo.SAZMAN").FirstOrDefault();
            var _NAME_ = dbms.DoGetDataSQL<string>("SELECT TOP 1 NAME FROM dbo.SAZMAN").FirstOrDefault();
            this.Title = $"ارسال گروهی صورت حساب به سامانه مودیان | برای {_NAME_} سال مالی {_YEA_} ";

            //نوع صورتحساب:
            CMB_Inty.ItemsSource = new List<COMBOYMODEL>
            {
                new COMBOYMODEL { ID = 1, NAME = "نوع اول" },
                new COMBOYMODEL { ID = 2, NAME = "نوع دوم" }
            };

            //روش تسویه:
            CMB_Setm.ItemsSource = new List<COMBOYMODEL>
            {
                new COMBOYMODEL { ID = 1, NAME = "نقد" },
                new COMBOYMODEL { ID = 2, NAME = "نسیه" },
                ////new COMBOYMODEL { ID = 3, NAME = "نقد/نسیه" }
            };

            GetPersianTodayDate();

            ReGetData();

            // Ensure the SfDataGrid is not null before subscribing
            if (SYNCFUSION_DG != null)
            {
                SYNCFUSION_DG.FilterChanged += View_FilterChanged;
                SYNCFUSION_DG.Loaded += (s, e) => UpdateRowCountLabel();

                UpdateRowCountLabel();
            }

        }

        private void GetPersianTodayDate()
        {
            // تنظیم تاریخ امروز به عنوان پیش‌فرض برای تاریخ سفارشی
            var persianCalendar = new System.Globalization.PersianCalendar();
            var today = DateTime.Now;
            var year = persianCalendar.GetYear(today);
            var month = persianCalendar.GetMonth(today);
            var day = persianCalendar.GetDayOfMonth(today);
            TXT_CUSTOM_DATE.Text = $"{year:0000}/{month:00}/{day:00}";
        }

        public static bool IsDenafaraz { get; set; } = false;
        private void ReGetData()
        {
            FACTOR_DATA?.Clear();

            string condition = IsDenafaraz ? "2" : "13";

            string SQL = @$"SELECT
                                                            dbo.HEAD_LST.NUMBER1,
                                                            dbo.HEAD_LST.TAH,
                                                            dbo.HEAD_LST.NUMBER,
                                                            dbo.HEAD_LST.DATE_N,
                                                            dbo.HEAD_LST.MAS,
                                                            dbo.HEAD_LST.N_S,
                                                            dbo.HEAD_LST.CUST_NO,
                                                            dbo.CUST_HESAB.NAME,
                                                            dbo.HEAD_LST.MOLAH,
                                                            dbo.HEAD_LST.M_NAGHD,
                                                            dbo.HEAD_LST.MABL_VAR,
                                                            dbo.HEAD_LST.MOIN_VAR,
                                                            dbo.HEAD_LST.MABL_HAV,
                                                            dbo.HEAD_LST.MOIN_HAV,
                                                            dbo.HEAD_LST.MABL_HAZ,
                                                            dbo.HEAD_LST.MOIN_HAZ,
                                                            dbo.HEAD_LST.TAKHFIF,
                                                            dbo.HEAD_LST.MOIN_KHF,
                                                            dbo.HEAD_LST.TAG,
                                                            dbo.DEPART.DEPNAME,
                                                            dbo.SHIFT.SHNAME,
                                                            dbo.CUSTKIND.CUSTKNAME,
                                                            dbo.HEAD_LST.USER_NAME,
                                                            dbo.HEAD_LST.SHARAYET,
                                                            dbo.HEAD_LST.MBAA,
                                                            dbo.HEAD_LST.HMBAA,
                                                            dbo.HEAD_LST.TICMBAA,
                                                            dbo.HEAD_LST.TKHF,
                                                            dbo.HEAD_LST.OKF,
                                                            dbo.HEAD_LST.JAY,
                                                            dbo.HEAD_LST.SGN1,
                                                            dbo.HEAD_LST.SGN2,
                                                            dbo.HEAD_LST.SGN3,
                                                            dbo.HEAD_LST.sgn1usid,
                                                            dbo.HEAD_LST.sgn2usid,
                                                            dbo.HEAD_LST.sgn3usid,
                                                            dbo.HEAD_LST.CRT,
                                                            dbo.HEAD_LST.UID,
                                                            dbo.PRICE_ELAMIE.PEPNAME,
                                                            dbo.PRICE_ELAMIETF.PENAME,
                                                            dbo.PRICE_PAYNO.PPAME,
                                                            dbo.HEAD_LST_EXTENDED.inty,
                                                            dbo.HEAD_LST_EXTENDED.inp,
                                                            dbo.HEAD_LST_EXTENDED.ins AS ext_ins, -- تغییر نام مستعار برای جلوگیری از تداخل احتمالی با منطق TAXDTL.Ins
                                                            dbo.HEAD_LST_EXTENDED.irtaxid,
                                                            dbo.HEAD_LST_EXTENDED.insp,
                                                            dbo.HEAD_LST_EXTENDED.cap,
                                                            dbo.HEAD_LST_EXTENDED.setm
                                                        FROM
                                                            dbo.HEAD_LST
                                                        LEFT OUTER JOIN
                                                            dbo.HEAD_LST_EXTENDED ON dbo.HEAD_LST.NUMBER = dbo.HEAD_LST_EXTENDED.NUMBER
                                                        LEFT OUTER JOIN
                                                            dbo.PRICE_PAYNO ON dbo.HEAD_LST.MODAT_PPID = dbo.PRICE_PAYNO.PPID
                                                        LEFT OUTER JOIN
                                                            dbo.PRICE_ELAMIETF ON dbo.HEAD_LST.PEID = dbo.PRICE_ELAMIETF.PEID
                                                        LEFT OUTER JOIN
                                                            dbo.CUSTKIND ON dbo.HEAD_LST.CUST_KIND = dbo.CUSTKIND.CUST_COD
                                                        LEFT OUTER JOIN
                                                            dbo.PRICE_ELAMIE ON dbo.HEAD_LST.PEPID = dbo.PRICE_ELAMIE.PEPID
                                                        LEFT OUTER JOIN
                                                            dbo.DEPART ON dbo.HEAD_LST.DEPATMAN = dbo.DEPART.DEPATMAN
                                                        LEFT OUTER JOIN
                                                            dbo.SHIFT ON dbo.HEAD_LST.SHIFT = dbo.SHIFT.SHIFT_ID
                                                        LEFT OUTER JOIN
                                                            dbo.CUST_HESAB ON dbo.HEAD_LST.CUST_NO = dbo.CUST_HESAB.hes
                                                        WHERE
                                                            (dbo.HEAD_LST.TAG = {condition}) AND
                                                            (dbo.HEAD_LST_EXTENDED.irtaxid IS NULL OR dbo.HEAD_LST_EXTENDED.irtaxid = N'0' OR dbo.HEAD_LST_EXTENDED.irtaxid = N'') AND -- اونهایی که صورت حساب مرجع شون خالیه
                                                            NOT EXISTS (
                                                                SELECT 1
                                                                FROM dbo.TAXDTL
                                                                WHERE
                                                                    dbo.TAXDTL.NUMBER = dbo.HEAD_LST.NUMBER AND  -- شرط اتصال دو جدول
                                                                    dbo.TAXDTL.ApiTypeSent = 1 AND             -- شرط نوع API ارسال سامانه اصلی
                                                                    dbo.TAXDTL.Ins = 1 AND                     -- موضوع صورتحساب : اصلی/فروش
                                                                    dbo.TAXDTL.TheStatus IN ('SUCCESS', 'PENDING') -- شرط وضعیت‌های ارسال شده یا در انتظار
                                                            ) ORDER BY dbo.HEAD_LST.NUMBER1,dbo.HEAD_LST.NUMBER DESC";

            var MasterHead = dbms.DoGetDataSQL<HEAD_LST>(SQL).ToList();
            foreach (var item in MasterHead)
            {
                FACTOR_DATA?.Add(item);
            }
        }

        #region FilterBy
        private void View_FilterChanged(object sender, GridFilterEventArgs e)
        {
            UpdateRowCountLabel();
        }
        private void UpdateRowCountLabel()
        {
            // Defensive checks
            if (ROWCOUNT_TEXTBLK == null) return;
            if (SYNCFUSION_DG?.View == null) return;

            // Safely retrieve the record count
            var recordCount = SYNCFUSION_DG.View.Records?.Count ?? 0;

            // Set the label content
            ROWCOUNT_TEXTBLK.Text = recordCount.ToString();
        }

        private readonly FilterService<HEAD_LST> filterService = new FilterService<HEAD_LST>();
        public ObservableCollection<string> ActiveFilters { get; set; } = new ObservableCollection<string>();
        public bool NowIsReady { get; private set; }

        private string? CurrentCellValue = null;
        private RowColumnIndex CurrentCellIndex;
        private void SYNCFUSION_DG_CurrentCellActivated(object sender, Syncfusion.UI.Xaml.Grid.CurrentCellActivatedEventArgs e) // Event handler for when a cell is activated in the data grid
        {
            if (e?.CurrentRowColumnIndex == null)
            {
                return;
            }

            UpdateCurrentCellValue(e.CurrentRowColumnIndex);
        }
        private void SYNCFUSION_DG_SelectionChanged(object sender, GridSelectionChangedEventArgs e) // Event handler for when the selection changes in the data grid
        {
            //// Get the selected row and column index
            //var currentCell = SYNCFUSION_DG.SelectionController.CurrentCellManager.CurrentCell;
            //if (currentCell != null)
            //{
            //    var rowColumnIndex = new RowColumnIndex(currentCell.RowIndex, currentCell.ColumnIndex);
            //    UpdateCurrentCellValue(rowColumnIndex);
            //}

            UpdateSelectedCount();
        }
        private void UpdateCurrentCellValue(RowColumnIndex rowColumnIndex) // Method to update the current cell value
        {
            CurrentCellIndex = rowColumnIndex; // Update current cell index
            CurrentCellValue = null; // Reset current cell value

            int rowIndex = rowColumnIndex.RowIndex;
            int columnIndex = this.SYNCFUSION_DG.ResolveToGridVisibleColumnIndex(rowColumnIndex.ColumnIndex);
            if (columnIndex < 0) return;

            var mappingName = this.SYNCFUSION_DG.Columns[columnIndex].MappingName;
            var recordIndex = this.SYNCFUSION_DG.ResolveToRecordIndex(rowIndex);
            if (recordIndex < 0) return;

            var record = this.SYNCFUSION_DG.View.Records.GetItemAt(recordIndex);

            //if (mappingName != "IsSelected")
            //{
            //    if (record is HEAD_LST item )
            //    {
            //        item.IsSelected = !item.IsSelected;
            //    }
            //}


            if (record == null)
            {
                return;
            }
            if (string.IsNullOrEmpty(mappingName))
            {
                return;
            }
            var property = record.GetType().GetProperty(mappingName);
            if (property == null)
            {
                Console.WriteLine("Property " + mappingName + " not found on type " + record.GetType().Name);
                return;
            }

            //CurrentCellValue = property.GetValue(record)?.ToString();
            CurrentCellValue = record?.GetType()?.GetProperty(mappingName)?.GetValue(record)?.ToString();
        }
        private void FilterBySelection_Click(object sender, RoutedEventArgs e)
        {
            var selectedText = GetSelectedText();
            var (columnName, filterValue) = GetSelectedCellDetails(); // Get the details of the selected cell

            if (!string.IsNullOrEmpty(selectedText))
            {
                // Add the Contains filter to the filter service (inclusion filter)
                filterService.AddFilter(columnName, selectedText, isExclusion: false); // False means it's an inclusion filter
                ActiveFilters.Add($"{columnName} Contains {selectedText}");
            }
            else
            {
                if (filterValue != null)
                {
                    //برای اینکه دقیقا همون آیتم رو فیلتر کنه:
                    //filterService.AddFilter(columnName, filterValue, isExclusion: false, isExactMatch: false);

                    // Add the filter to the filter service
                    filterService.AddFilter(columnName, filterValue);
                    // Add the filter to the list of active filters

                    ActiveFilters.Add($"{columnName} = {filterValue}");
                    // Apply the cumulative filter to the data grid
                }
            }
            ApplyCumulativeFilter();
        }
        private void FilterExcludingSelection_Click(object sender, RoutedEventArgs e)
        {
            var selectedText = GetSelectedText();
            if (!string.IsNullOrEmpty(selectedText))
            {
                var (columnName, filterValue) = GetSelectedCellDetails(); // Get the details of the selected cell
                if (filterValue != null)
                {
                    // Add the Not Contains filter to the filter service (exclusion filter)
                    filterService.AddFilter(columnName, selectedText, isExclusion: true); // True means it's an exclusion filter
                                                                                          // Add the exclusion filter to the list of active filters
                    ActiveFilters.Add($"{columnName} Does Not Contain {selectedText}");
                    // Apply the cumulative filter to the data grid
                    ApplyCumulativeFilter();
                }
            }
            else
            {
                var (columnName, filterValue) = GetSelectedCellDetails(); // Get the details of the selected cell
                if (filterValue != null)
                {
                    // Add the exclusion filter to the filter service
                    filterService.AddFilter(columnName, filterValue, isExclusion: true);
                    // Add the filter to the list of active filters
                    ActiveFilters.Add($"{columnName} != {filterValue}");
                    // Apply the cumulative filter to the data grid
                    ApplyCumulativeFilter();
                }
            }
        }

        private void RemoveFilterSort_Click(object sender, RoutedEventArgs e) // Event handler to remove all filters and sorting
        {
            // Clear all filters in the filter service
            filterService.ClearFilters();
            // Clear the list of active filters
            ActiveFilters.Clear();
            // Apply the cumulative filter to the data grid
            ApplyCumulativeFilter();
        }
        private (string ColumnName, object FilterValue) GetSelectedCellDetails() // Method to get the details of the selected cell
        {
            // Check if there is a current cell selected in the data grid
            if (SYNCFUSION_DG.SelectionController.CurrentCellManager.CurrentCell != null)
            {
                var columnName = SYNCFUSION_DG.SelectionController.CurrentCellManager.CurrentCell.GridColumn.MappingName; // Get the name of the column
                                                                                                                          // Return the column name and the current cell value
                return (columnName, CurrentCellValue);
            }
            return (null, null); // If no cell is selected, return null values
        }
        private void ApplyCumulativeFilter() // Method to apply all cumulative filters to the data grid
        {
            // Set the filter for the data grid view using the filter service
            SYNCFUSION_DG.View.Filter = item => filterService.ApplyFilter(item as HEAD_LST);
            // Refresh the filter to update the view
            SYNCFUSION_DG.View.RefreshFilter();

            UpdateRowCountLabel();
        }
        private void SYNCFUSION_DG_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var element = e.OriginalSource as FrameworkElement;
            if (element != null)
            {
                element.ContextMenu = this.Resources["DataGridContextMenu"] as ContextMenu;
            }
        }
        private string GetSelectedText()
        {
            var dataGrid = SYNCFUSION_DG;
            var currentCell = dataGrid.SelectionController.CurrentCellManager.CurrentCell;

            if (currentCell != null && currentCell.IsEditing)
            {
                // Find the editing element (which will be a TextBox in edit mode)
                var editingElement = dataGrid.FindElementOfType<TextBox>();
                if (editingElement != null)
                {
                    return editingElement.SelectedText; // Return the selected text
                }
            }
            return string.Empty;
        }

        #endregion

        CustomExceptErMsg CER = new CustomExceptErMsg();
        private async void BTN_SENDGRP_Click(object sender, RoutedEventArgs e)
        {
            Logation.AMALIYAT_USER($"{GetType().Name}| کلیک روی دکمۀ ارسال گروهی SelectedCount:{SelectedItemsCount}");

            if (!IsValid()) return;

            // -- دیالوگ تأیید
            if (!(new Msgwin(true, "آیا از ارسال گروهی آیتم‌های انتخاب‌شده مطمئن هستید؟")
                    .ShowDialog() ?? false))
                return;

            // -- آدرس سرویس
            CL_MOADIAN.TaxURL = (RD_MAINAPI.IsChecked ?? false)
                    ? "https://tp.tax.gov.ir/req/api/"
                    : "https://sandboxrc.tax.gov.ir/req/api/";


            // ❶ آماده‌سازی UI
            bool IsCustomDate = CHK_CUSTOM_DATE?.IsChecked ?? false;
            string CustomDateValue = TXT_CUSTOM_DATE.Text.Trim();

            IsEnabledIndicator = true;
            BTN_SENDGRP.IsEnabled = false;
            IsBusy = true;
            Progress = 0;                   // شروع با ۰٪
            await Task.Yield();                // ⭐ اجازه بده UI فوراً رِندر شود

            try
            {
                var selected = FACTOR_DATA.Where(x => x.IsSelected)
                      .Select(x => (long)x.NUMBER)
                      .ToList();

                int total = selected.Count;
                int done = 0;
                int TAG = 2;
                int? inty = (int?)CMB_Inty.SelectedValue;
                int? setm = (int?)CMB_Setm.SelectedValue;

                int sent = 0;

                // ❶ Progress object روی ترد UI ساخته می‌شود
                var pr = new Progress<int>(done =>
                {
                    sent = done;
                    Progress = Math.Round(100.0 * sent / total, 1);
                });

                // ❷ متد را مستقیماً await کنید؛ Task.Run لازم نیست
                var result = await Task.Run(async () =>
                {
                    var bulk = new SendInvoiceBulk(dbms, CL_MOADIAN.TaxURL);  // دیگر روی UI نیست
                    return await bulk.SendAsync(
                                 selected, 2,
                                 inty,
                                 setm,
                                 pr, IsCustomDate, CustomDateValue);
                });

                //-------------------------------------------------
                //                     UI
                //-------------------------------------------------
                var summary = $"تعداد ارسال موفق: {result.Success}\n" + (result.Failures.Any()
                                  ? $"تعداد ارسال ناموفق: {result.Failures.Count}"
                                  : "همه فاکتورها ارسال و در صف قرار گرفتند");

                _ = new Msgwin(false, summary).ShowDialog();

                if (result.Failures.Any())
                {
                    var errors = result.Failures
                                       .Select((kv, i) => new MsgTaxModel
                                       {
                                           ROW_U = i + 1,
                                           CODE_U = (int)kv.Key,
                                           MessageText_U = kv.Value
                                       })
                                       .GroupBy(x => x.MessageText_U)
                                       .Select(g => g.First())
                                       .ToList();

                    CL_Generaly.DoGetwriteAppenLog(string.Join(Environment.NewLine, errors.Select(e => $"[BulkSend] #{e.CODE_U}: {e.MessageText_U}")));

                    _ = new MsgListwin(false, errors).ShowDialog();
                }

                ReGetData();

            }
            catch (Exception ex)
            {
                var friendly = CER.ExpecMsgEr(ex) ?? "خطا در انجام عملیات، ارتباط با سامانه مقدور نیست.";
                new Msgwin(false, friendly).ShowDialog();

                if (friendly == null)           // خطای ناشناخته را لاگ کن
                    LogWriter.WriteLog($"[BulkSend-Err] {ex}");
            }
            finally
            {
                IsEnabledIndicator = false;
                BTN_SENDGRP.IsEnabled = true;

                IsBusy = false;
                Progress = 0;
                BTN_SENDGRP.Content = "ارسال گروهی فاکتورهای انتخاب شده";
            }
        }

        private bool IsValid()
        {
            var azNumber = AZ_NUMBER.Value;
            var taNumber = TA_NUMBER.Value;

            if (azNumber == null || taNumber == null || azNumber > taNumber)
            {
                new Msgwin(false, "محدوده شماره برای ارسال گروهی نامعتبر است.").ShowDialog();
                return false;
            }

            List<MsgTaxModel> ErrosMessages = new List<MsgTaxModel>();

            var selected = FACTOR_DATA.Where(h => h.IsSelected)
                                      .Select(h => (long)h.NUMBER)
                                      .ToList();
            if (!selected.Any())
            {
                ErrosMessages.Add(new MsgTaxModel { MessageText_U = $"هیچ فاکتور انتخاب نشده !" });
            }

            //if (selected.Count > 99)
            //{
            //    ErrosMessages.Add(new MsgTaxModel { MessageText_U = $"شما بیش از 99 فاکتور رو برای ارسال انتخاب کرده اید , که این مجاز نیست , اصلاح کنید و سپس مجددا امتحان کنید" });
            //}

            if (ErrosMessages.Any())
            {
                ErrosMessages = ErrosMessages.Select(x => x.MessageText_U).Distinct()
                    .Select(message => new MsgTaxModel { MessageText_U = message }).ToList();

                _ = new MsgListwin(false, ErrosMessages).ShowDialog();
                ErrosMessages?.Clear();

                return false;
            }

            return true;
        }

        private void RD_RANGENUMBER_Checked(object sender, RoutedEventArgs e)
        {
            if (!NowIsReady) { return; }

            SELECTOR_COLUMN.IsReadOnly = true;
            ApplyRangeSelection(); // اعمال انتخاب بر اساس محدوده فعلی
        }
        private void RD_SELECTIVE_Checked(object sender, RoutedEventArgs e)
        {
            if (!NowIsReady) { return; }
            SELECTOR_COLUMN.IsReadOnly = false;

            // وقتی به حالت انتخاب سفارشی می‌رویم، معمولاً می‌خواهیم انتخاب‌های محدوده پاک شوند
            // تا کاربر کنترل کامل روی انتخاب‌ها داشته باشد.
            // اگر نمی‌خواهید پاک شوند، این حلقه را حذف کنید.
            DeSelectItemsForFresh();
        }

        private void DeSelectItemsForFresh()
        {
            if (!_isUpdatingSelectionByRange && FACTOR_DATA != null) // جلوگیری از پاک شدن در حین به‌روزرسانی خودکار
            {
                _isUpdatingSelectionByRange = true; // برای جلوگیری از فراخوانی مجدد ApplyRangeSelection از طریق PropertyChanged
                foreach (var invoice in FACTOR_DATA)
                {
                    if (invoice.IsSelected)
                        invoice.IsSelected = false;
                }
                UpdateSelectedCount();
                _isUpdatingSelectionByRange = false;
            }
        }
        private void ApplyRangeSelection()
        {
            // متد برای اعمال انتخاب بر اساس محدوده

            if (!NowIsReady || _isUpdatingSelectionByRange) return; // اگر UI آماده نیست یا در حال به‌روزرسانی دیگری است، خارج شو
            if (RD_RANGENUMBER.IsChecked != true) return;   // فقط زمانی که انتخاب محدوده فعال است

            _isUpdatingSelectionByRange = true; // شروع به‌روزرسانی برنامه‌ریزی شده

            var azNumber = AZ_NUMBER.Value; // مقدار شروع محدوده از NumericUpDown
            var taNumber = TA_NUMBER.Value; // مقدار پایان محدوده از NumericUpDown

            if (FACTOR_DATA == null)
            {
                _isUpdatingSelectionByRange = false;
                return;
            }

            // اگر محدوده نامعتبر است، تمام انتخاب‌ها را لغو کن
            if (azNumber == null || taNumber == null || azNumber > taNumber)
            {
                foreach (var invoice in FACTOR_DATA)
                {
                    if (invoice.IsSelected) // فقط اگر نیاز به تغییر است، برای بهینگی
                        invoice.IsSelected = false;
                }
                _isUpdatingSelectionByRange = false;
                return;
            }

            // حلقه روی تمام فاکتورها و تنظیم وضعیت IsSelected
            foreach (var invoice in FACTOR_DATA)
            {
                if (invoice.NUMBER.HasValue) // بررسی اینکه شماره فاکتور null نباشد
                {
                    // بررسی اینکه آیا شماره فاکتور در محدوده مشخص شده قرار دارد
                    bool isInRange = invoice.NUMBER.Value >= azNumber && invoice.NUMBER.Value <= taNumber;
                    if (invoice.IsSelected != isInRange) // فقط اگر نیاز به تغییر است
                        invoice.IsSelected = isInRange;
                }
                else
                {
                    if (invoice.IsSelected) // اگر شماره ندارد و انتخاب شده، لغو انتخاب کن
                        invoice.IsSelected = false;
                }
            }

            UpdateSelectedCount();

            _isUpdatingSelectionByRange = false; // پایان به‌روزرسانی برنامه‌ریزی شده
        }
        private void UpdateSelectedCount()
        {
            if (FACTOR_DATA != null)
            {
                SelectedItemsCount = FACTOR_DATA.Count(f => f.IsSelected);
            }
        }
        private void AZ_NUMBER_ValueChanged(object sender, RoutedPropertyChangedEventArgs<int> e)
        {
            // اگر مقدار واقعاً تغییر کرده و UI آماده است
            if (NowIsReady && e.OldValue != e.NewValue)
            {
                var azNumber = AZ_NUMBER.Value;
                var taNumber = TA_NUMBER.Value;
                if (azNumber == null || taNumber == null || azNumber > taNumber)
                {
                    e.Handled = true; return;
                }

                ApplyRangeSelection();
            }
        }
        private void TA_NUMBER_ValueChanged(object sender, RoutedPropertyChangedEventArgs<int> e)
        {
            // اگر مقدار واقعاً تغییر کرده و UI آماده است
            if (NowIsReady && e.OldValue != e.NewValue)
            {
                var azNumber = AZ_NUMBER.Value;
                var taNumber = TA_NUMBER.Value;
                if (azNumber == null || taNumber == null || azNumber > taNumber)
                {
                    e.Handled = true; return;
                }

                ApplyRangeSelection();
            }
        }

        bool AllSelected = false;
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            AllSelected = !AllSelected;

            // دسترسی به View فعلی (شامل فیلترها)
            var view = SYNCFUSION_DG.View;

            var Btn = sender as Button;
            if (Btn != null)
            {
                Btn.Content = AllSelected ? "لغو اتخاب همه آیتم های موجود" : "اتخاب همه آیتم های موجود";
            }

            // هر رکورد نمایش‌داده‌شده را بردار و IsSelected را true کن
            foreach (var record in view.Records)
            {
                if (record.Data is HEAD_LST item)
                    item.IsSelected = AllSelected;
            }

        }
    }
}