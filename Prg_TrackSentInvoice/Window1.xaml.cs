using Prg_Graphicy.Wins;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.FUNCTIONS;
using Prg_Moadian.Generaly;
using Prg_Moadian.SQLMODELS;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using static Prg_Moadian.Generaly.CL_Generaly;
using static Prg_Graphicy.LMethods.CL_TOOLS;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json.Linq;
using System.Threading;
using Prg_Moadian.Service;

namespace Prg_TrackSentInvoice
{
    public partial class Window1 : Window
    {
        System.Windows.Threading.DispatcherTimer Timer1 = new System.Windows.Threading.DispatcherTimer();
        private bool IsTheTimerStillWorking;
        public bool IsOtherProccessingNow { get; set; } = false;

        CL_FUNTIONS Functions = new CL_FUNTIONS();
        private string PrivateKeyTax { get; set; } = "MIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQCIkui+QVA/NfOx\r\nems4hm4jj1cQRgUehU75osEy/HZk0ed+HwC4iXLA32g5qt2OPf9Z2BORBoKSXxyY\r\nPhZOlUNKy2JPbcSDJy3qLR0+Y1dvfGNHGjanHH6Q67sVw/sd1bxnO61+AQ6JXuPL\r\nM2ZHVL5JieJbZfM9mhLM2Tb3+iP8O2//4b9kbyTShmBnXK0sskHY04mFnheDI4mQ\r\nUy0pQBSQEW+9YHzXWWVZ4isCgS4y0oRgGPnl6bGSgEHIU5TI6ZzhQOVhQETNRyQ0\r\nP+8j9rjUwa4V+bhIStWT+IZVqmHGJdRb0Oj4Uw69YF699mLN2r8UJkCQLs7g+HiJ\r\n5QSngLdtAgMBAAECggEAIvbJUvvRmX0seEFI9d2kkMH/nhDu+pNSVqKOJ4lunf0G\r\n8MjrHFND55DKVAkkU2gX2V5yE+zAvMxQu8EZYODhq2JKNt95McJ0BMGr/O7d0ZLc\r\nr7VMTJgE5wESkk3sGgVACIXEsr9+gzihxMHR620MkjMUmiWNkjXBnmP1qKKHV+lU\r\n/QBxPDoljlmUTi4JAZkr9lNla/ZbyMrHjmWB2IKNDuxjWvAFTVpij6gdECjDLE++\r\n9J8ZywDr+qBB4D0jDj2qU5oqwGUuNCqw1dYAG+fkx7r7MWSfrCbNPeXCmHMNuKA8\r\nVEkaAoI2oU/dICrQMahD5r8eMFLTBINJCU3tfEHSWQKBgQDuZA569gyjIy8ofrq6\r\nyyYGyvQ0VmMDPkGUq/CAPmR3l74fJQUO0utKTq9L7lz9WGKMst3i6fhfw61obdYn\r\n+ra6sCRay3quNFtG+Ks0XndeVGDZsumaHjHb3902GQcZQpvRJbC4NYsXcQg2y5yC\r\neALj0v+CwugJX54vY2Wh/+2o2wKBgQCSqYFT7FWZ/6wihUROHkHHKVjCp91OkWDG\r\nw6fptJBGfsm+Zw11E3q1yl9C7GGpGd3OacoNN6bgLDMPDejSdtm8uqu3vYkQy0Jk\r\niIerZIwwuwivVatOhU2YMZq1+XolFub7Dj/0CiethHS+NS6wwOMIOEk9rE21bvJi\r\nl4PEhF6PVwKBgQDAYGBLHDowgFkzDan0ybGjM68EeV4npNrZdjN72l3LINpdWcuO\r\nHemgqoTN+spx7ByDPGjREEzOQyOyLUjwNGO3niOIXcJfyIKMcGoAtecQaXlK1RWs\r\nuIc1z589Y88VtGn3yrmkvhjDzwR467EenGiAn6pwRIdp4Q7PYSAILncElwKBgG9f\r\nmX6JslfH+Igee8h24azEkUsA/uZzL/LBEfo/zHA8SCf3ShjmOgFjNQQ1TdSEeBQP\r\n8ggnguoppnyAK5Xn+2F+wHg/zp6aPEjsBVr6eBtpbSb4/6YZRNuWj84xLbiMs8ti\r\n/t3r+EWkmKL48AP59m5/j97twfVN03NbbA0IGGbxAoGANzW8Fc/q6vNwizMH1XSx\r\ndMrCnyEfjcaG3ezvZz/2OBNUntvpsDE8DE5reuCIWwadiZOwITZ2nn+SzcJaFcYt\r\n1GIMFrYLCMcYoyIDKs6O/cR23MkucmAHxz8fGoOF1LCTPGfuKswAbzZ0zlQpfdv8\r\ny6obLvqRHPGvKGloYITMTPs=";
        private string MemoryIDTax { get; set; } = "A278R7";
        public static TaxService? taxService = null;
        public static TaxModel.InquiryByReferenceIdModel.Root root = null;

        public List<string> MY_ALL_DATA_ROW { get; set; } = new List<string>();

        List<string> StrRefList = new List<string>();

        BackgroundWorker BGWorker = new BackgroundWorker();
        public Window1()
        {
            InitializeComponent();

            Timer1.Tick += Timer1_TICK;
            Timer1.Interval = new TimeSpan(0, 0, 0, 5);

            string[] values = {
                        "b010edaa-ddc4-4d1f-a345-5dbf563f4bba",
                        "ee01bcad-53a7-49ec-936e-04a3edd4b81b",
                        "53508164-6d41-4652-ae9f-ece41df39231",
                        "92f02f8e-7614-4858-90c5-f8c291a22cb8",
                        "6be798ce-55c8-47e6-87fb-60b4244b7a26",
                        "0e69de82-c382-454b-90b7-ff1ab22b9a68",
                        "d3f179dd-42c5-41a3-90ac-5b00a44e8a91",
                        "0613b1ec-1edf-4d98-8e8e-c3cca8ec61a0",
                        "c6006058-c4e0-4356-ad90-07333d797d57",
                        "40aa96f6-51c1-41ff-b91d-15b3d4d79add",
                        "058425e6-1701-4e8f-a05e-03d8a67882bc",
                        "8399afab-d5bd-41c7-8fd5-709214618969",
                        "29e519eb-2312-4f08-85d2-d7665d6f7baa",
                        "7e0d34db-a21d-4e41-bb85-1a45400c0c32",
                        "0735aee4-c95f-4e2a-860d-3379c441f27a",
                        "7ab606a0-2c24-4ac2-bcc4-792310925985",
                        "3804269a-d00b-4653-9656-7743fd9fe08e",
                        "2810c9a7-7f3a-4d52-b886-5771cafd54e9",
                        "e5800f0d-69b8-416f-8c82-23d11ca83b38",
                        "17f66b73-4b84-4256-90d2-0b47dad36d98",
                        "f9dfe511-44cb-4e22-b8e6-3a7e15be76b0",
                        "c97d741b-5fce-4368-9e28-d64a14621964",
                        "6a7a16a9-720a-440e-9d04-62a3d5cd348e",
                        "a2874773-2465-47da-ad2a-1668080b024c",
                        "734ce6a1-d563-4039-83a0-563dbaa5c91c",
                        "5c657900-c24d-442f-83fb-d29943b7d305",
                        "0a41258a-c406-4981-919d-cb8a0e0f97ed"
            };
            StrRefList = values.ToList();

            BGWorker.DoWork += BGWorker_DoWork;
            BGWorker.ProgressChanged += BGWorker_ProgressChanged;
            BGWorker.RunWorkerCompleted += BGWorker_RunWorkerCompleted;  //Tell the user how the process went
            BGWorker.WorkerReportsProgress = true;
            BGWorker.WorkerSupportsCancellation = true;
        }
        private void BGWorker_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            //{Begin---------------------------------
            for (int row = 0; row < StrRefList.Count; row++)
            {
                var _ErMessages = GetInquieryMessage(StrRefList[row]);
                Dispatcher.Invoke(new Action(() =>
                {
                    MainListBox.Items.Add($"Row {StrRefList[row]} Added");
                    //MainListBox.ItemsSource = null;
                    //MainListBox.ItemsSource = Functions.GetNormilizedMsg(_ErMessages);
                }));

                BGWorker.ReportProgress(row);
                //Check if there is a request to cancel the process
                if (BGWorker.CancellationPending)
                {
                    e.Cancel = true;
                    BGWorker.ReportProgress(0);
                    return;
                }
            }
            //End}-----------------------------------


            //If the process exits the loop, ensure that progress is set to 100%
            //Remember in the loop we set i < 100 so in theory the process will complete at 99%
            BGWorker.ReportProgress(StrRefList.Count);
        }
        private void BGWorker_ProgressChanged(object sender, System.ComponentModel.ProgressChangedEventArgs e)
        {
            progressBar1.Maximum = StrRefList.Count;
            progressBar1.Value = e.ProgressPercentage;
        }
        private void BGWorker_RunWorkerCompleted(object sender, System.ComponentModel.RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled)
            {
                lblStatus.Text = "Process was cancelled";
            }
            else if (e.Error != null)
            {
                lblStatus.Text = "There was an error running the process. The thread aborted";
            }
            else
            {
                lblStatus.Text = "Process was completed";
            }
        }
        private void Button1_Click(object sender, RoutedEventArgs e) //Cancel
        {
            if (BGWorker.IsBusy)
            {
                BGWorker.CancelAsync();
            }
        }

        private string GetInquieryMessage(string refrence_number)
        {
            root = taxService.InquiryByReferenceId(refrence_number);
            List<string>? _er_lst = new List<string>();
            foreach (var item in root.error)
                _er_lst.Add(item.code + " | " + item.message);

            string? ERVALS = (_er_lst != null && _er_lst.Count > 0) ? $"{string.Join(",", _er_lst)}" : null;

            return ERVALS;
        }
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            taxService = new TaxService(MemoryIDTax, PrivateKeyTax, "https://sandboxrc.tax.gov.ir/req/api/");
            taxService.RequestToken();
            root = new TaxModel.InquiryByReferenceIdModel.Root();

            BGWorker.RunWorkerAsync();

            //Timer1.Start();
        }
        private void Timer1_TICK(object? sender, EventArgs e)
        {
            if (IsTheTimerStillWorking) return; // جلوگیری از اینکه تایمر هنوز در حال کار است.

            if (IsOtherProccessingNow is false) // تداخل با حالت دستی نداشته باشه 
            {
                IsTheTimerStillWorking = true;
                //{Begin---------------------------------

                foreach (var item in StrRefList)
                {
                    var _ErMessages = GetInquieryMessage(item);
                    if (!string.IsNullOrEmpty(_ErMessages))
                    {
                        MainListBox.ItemsSource = null;
                        MainListBox.ItemsSource = Functions.GetNormilizedMsg(_ErMessages);
                    }
                }



                //End}-----------------------------------
                IsTheTimerStillWorking = false;
            }
        }


     
    }
}
