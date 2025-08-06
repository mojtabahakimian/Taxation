using Newtonsoft.Json;
using Prg_Graphicy.Wins;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.FUNCTIONS;
using Prg_Moadian.SQLMODELS;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Windows;
using System.Windows.Input;
using static Prg_TrackSentInvoice.FactorManagement_WIN;

namespace Prg_TrackSentInvoice
{
    public partial class FactorEditor_WIN : Window
    {
        public bool SavedPressed { get; set; } = false;
        public List<FULL_TAXDTL> ITEM_GOT { get; set; } = new List<FULL_TAXDTL>();
        public List<FULL_TAXDTL> WAS_ITEM_GOT { get; set; } = new List<FULL_TAXDTL>();
        CL_CCNNMANAGER dbms = new CL_CCNNMANAGER();
        public string RSDTL_TMP_NAME { get; set; }

        List<VAHEDS> VAHEDHA = new List<VAHEDS>();
        public FactorEditor_WIN(object _selecteditem, List<VAHEDS> _vahedha, string table_name)
        {
            InitializeComponent();
            ITEM_GOT.Add((_selecteditem as FULL_TAXDTL));

            RSDTL_TMP_NAME = table_name;

            WAS_ITEM_GOT.Add(((FULL_TAXDTL)_selecteditem).Clone() as FULL_TAXDTL); //WHAT WAS LAST BEFORE CHANGE
            ////WAS_ITEM_GOT.Add(JsonConvert.DeserializeObject<FULL_TAXDTL>(JsonConvert.SerializeObject(ITEM_GOT[0]))); Claude2

            VAHEDHA = _vahedha;

            DataContext = ITEM_GOT[0];
        }
        private void ReCompute()
        {
            var Row = ITEM_GOT[0];
            //1-Fee Cutter 
            Row.Fee = (decimal?)Math.Truncate((double)Row.Fee); // مبلغ 
            Row.Dis = (decimal?)Math.Truncate((double)Row.Dis); //مبلغ تخفیف
            Row.Am = (decimal?)Math.Round((double)Row.Am, 4); //مقدار کل

            //Calcute
            Row.Prdis = (decimal)Math.Floor((double)(Row.Fee * Row.Am)); // مبلغ قبل از کسر تخفیف

            Row.Adis = Row.Prdis - Row.Dis;  // مبلغ پس از کسر تخفیف    //dbo.INVO_LST.MABL_K - dbo.INVO_LST.N_MOIN AS mabkbt

            //حاصلضرب مبلغ کالا پس از کسر تخفیفات و سایر مبالغ که در قانون اشاره شده در نرخ مالیات بر ارزش افزوده.
            if (Row.Vam > 0)
            {
                Row.Vam = Row.Adis * Row.Vra / 100;

                //4-Vam Cutter
                Row.Vam = (decimal?)Math.Truncate((double)Row.Vam);
            }
            Row.Tsstam = Row.Adis + Row.Vam; //مبلغ کل کالا /خدمت  // dbo.INVO_LST.MABL_K - dbo.INVO_LST.N_MOIN + dbo.INVO_LST.Vam AS mabkn

            //header.Tprdis //مجموع مبلغ قبل از کسر تخفیف //INVO_LST	Sum(MABL_K)
            //header.Tdis  //مجموع تخفیفات //INVO_LST	Sum(N_MOIN)
            //header.Tadis //مجموع مبلغ پس از کسر تخفیف //INVO_LST 	Sum(INVO_LST.MABL_K - INVO_LST.N_MOIN AS mabkbt)
            //header.Tvam  //مجموع مالیات بر ارزش افزوده //INVO_LST	Sum(IMBAA)
            //header.Tbill//مجموع صورت حساب //mabkn	INVO_LST.MABL_K-INVO_LST.N_MOIN+INVO_LST.IMBAA AS mabkn
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {

        }
        private void SAVE_BTN_Click(object sender, RoutedEventArgs e)
        {
            ReCompute();

            var row = ITEM_GOT[0];

            //1- First Single Row
            dbms.DoExecuteSQL($@"UPDATE {RSDTL_TMP_NAME} SET 
                            Fee = {row.Fee},
                            Dis = {row.Dis},
                            Am = {row.Am},
                            Prdis = {row.Prdis},
                            Adis = {row.Adis},
                            Vam = {row.Vam},
                            Tsstam = {row.Tsstam}
                            WHERE IDD = {row.IDD}");

            //2- Update All Row for header part
            dbms.DoExecuteSQL($@"UPDATE  {RSDTL_TMP_NAME} 
                                SET 
                                    Tprdis = aggregated_values.sum_prdis,
                                    Tdis = aggregated_values.sum_dis,
                                    Tadis = aggregated_values.sum_adis,
                                    Tvam = aggregated_values.sum_vam,
                                    Tbill = aggregated_values.sum_tsstam
                                FROM (
                                    SELECT 
                                        SUM(prdis) AS sum_prdis,
                                        SUM(dis) AS sum_dis,
                                        SUM(adis) AS sum_adis,
                                        SUM(vam) AS sum_vam,
                                        SUM(tsstam) AS sum_tsstam
                                    FROM  {RSDTL_TMP_NAME} 
                                ) AS aggregated_values;");

            SavedPressed = true;
            this.Close();
        }
        private void CLOSER_BTN_Click(object sender, RoutedEventArgs e)
        {
            RestoreUndoBefore();
            this.Close();
        }
        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Force a move to the next focusable element in the tab order
                InputManager.Current.ProcessInput(
                    new KeyEventArgs(
                        Keyboard.PrimaryDevice,
                        PresentationSource.FromVisual(this), // replace 'this' with the relevant control if not handled at the window level
                        0,
                        Key.Tab)
                    {
                        RoutedEvent = Keyboard.KeyDownEvent
                    }
                );

                e.Handled = true; // Indicate that the key event has been handled
            }
        }
        private void AmOnLeave(object sender, RoutedEventArgs e)
        {
            ReCompute();
        }
        private void FeeOnLeave(object sender, RoutedEventArgs e)
        {
            ReCompute();
        }
        private void DisOnLeave(object sender, RoutedEventArgs e)
        {
            ReCompute();
        }
        private void RestoreUndoBefore()
        {
            foreach (var prop in typeof(FULL_TAXDTL).GetProperties())
            {
                prop.SetValue(ITEM_GOT[0], prop.GetValue(WAS_ITEM_GOT[0]));
                ITEM_GOT[0].OnPropertyChanged(prop.Name);
            }
        }
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (SavedPressed) {/*Nothing*/}
            else
            {
                RestoreUndoBefore();
            }
        }

    }
}
