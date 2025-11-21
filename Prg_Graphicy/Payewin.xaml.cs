using Prg_Graphicy.LMethods;
using Prg_Graphicy.Wins;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.FUNCTIONS;
using Prg_Moadian.Generaly;
using Prg_Moadian.SQLMODELS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using static Prg_Moadian.FUNCTIONS.CL_FUNTIONS;
using static Prg_Moadian.Generaly.CL_Generaly;

namespace Prg_Graphicy
{
    public partial class Payewin : Window
    {
        public class ES1
        {
            public string? TheError { get; set; }
            public string? TheStatus { get; set; }
        }
        public Payewin()
        {
            InitializeComponent();
        }

        CL_CCNNMANAGER dbms;
        CL_FUNTIONS TheFunctions = new CL_FUNTIONS();
        private void GoFullExitNow()
        {
            CL_PRC_LOADER.Dispose();
            System.Environment.Exit(0);
        }
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var _IsSuccess = true;

            var args = CL_Generaly.ARG_PARAM;

            string[]? NUMBER_AND_TAG = args[0].Split('_');
            Msgwin msgwin0 = new Msgwin(true, $"انتخاب کنید که به کدام سامانه میخواهید ارسال کنید \n\n توجه داشته باشید باتوجه به اینکه سامانه مودیان فی با اعشار قبول نمی کند , ممکن است صورت حساب ارسالی در حد چند ریال تغییر کند. ({NUMBER_AND_TAG[0]})", null, false, true, "سامانه تستی", "سامانه اصلی");
            msgwin0.ShowDialog();
            if (msgwin0.DialogResult == false)
            {
                //MAIN_API
                CL_MOADIAN.TaxURL = "https://tp.tax.gov.ir/req/api/"; //درصورت عدم تایید به سامانه اصلی ارسال میشود.
            }
            if (msgwin0.ClosedByUser) //اگر کاربر پنجره رو بست و نخواست ادامه بده
            {
                GoFullExitNow();
            }

            CustomExceptErMsg CER = new CustomExceptErMsg();
            try
            {
                dbms = new CL_CCNNMANAGER();
                var TestCNN = dbms.DoGetDataSQL<string>("SELECT TOP 1 YEA FROM dbo.SAZMAN").FirstOrDefault();
            }
            catch (Exception er)
            {
                _IsSuccess = false;
                new Msgwin(false, "خطا در ارتباط با دیتابیس").ShowDialog();
                LogWriter.WriteLog($"\n[ Database Error, Expetion : Message: {er.Message}{Environment.NewLine} StackTrace: {er.StackTrace}{Environment.NewLine} \n {er.Data} \n {er.InnerException} \n" +
                     $" {er.Source} \n" +
                     $" {er.TargetSite} \n" +
                     $" {er.HResult} \n " +
                     $" {er.HelpLink} \n " +
                     $"End Log ]\n");
                GoFullExitNow();
            }

            CL_ScriptUpdateDB.Go(); // Update Table Scripts

            try
            {
                CL_PRC_LOADER.Start();

                var _NUMBER_ = NUMBER_AND_TAG[0]?.ToString();
                var _TAG_ = NUMBER_AND_TAG[1].ToString();

                #region DUP_CHECK
                var _factornum = Convert.ToString(_NUMBER_)?.TrimStart('0');
                //بررسی اینکه ارسال تکراری نباشد حالا بسته ارسال موفق یا در انتظار
                var _HEAD_EXTENDED = dbms.DoGetDataSQL<HEAD_LST_EXTENDED>($"SELECT * FROM dbo.HEAD_LST_EXTENDED WHERE NUMBER = {_NUMBER_} AND TGU = {_TAG_}").FirstOrDefault();
                if (_HEAD_EXTENDED?.inty is null)
                {
                    throw new NullyExceptiony("HEAD_EXTENDED is null");
                }

                if (CL_MOADIAN.TaxURL == "https://tp.tax.gov.ir/req/api/")
                {
                    if (_HEAD_EXTENDED.ins == 1) //اصلی
                    {
                        //-- آیا فاکتوری با نوع اصلی (فروش) به این شماره فاکتور قبلا با موفقیت ارسال شده ؟
                        //-- 1 = اصلی
                        //var _number = TheFunctions.InnoAddZeroes(_NUMBER_.ToString());
                        //فاکترو های فروش ارسال شده به سامانه اصلی با این شماره فاکتور داخلی نرم افزار
                        var _Issent = dbms.DoGetDataSQL<string>($"SELECT TheStatus FROM dbo.TAXDTL WHERE ApiTypeSent = 1 AND Ins = 1 AND TheStatus IN ('SUCCESS', 'PENDING') AND NUMBER = {_NUMBER_} AND TAG = {_TAG_}  ").ToList();
                        if (_Issent.Count > 0)
                        {
                            if (_Issent.Contains("SUCCESS"))
                            {
                                Msgwin msgwinv = new Msgwin(true, $"این صورت حساب از نوع اصلی (فروش) به شماره (حواله) {_factornum} قبلا ارسال و ثبت شده آیا مایلید مجددا ارسال کنید ؟");
                                msgwinv.ShowDialog();
                                if (msgwinv.DialogResult is false)
                                {
                                    GoFullExitNow();
                                }
                            }
                            else if (_Issent.Contains("PENDING"))
                            {
                                Msgwin msgwinv = new Msgwin(true, $"این صورت حساب اصلی (فروش) به شماره (حواله) {_factornum} قبلا ارسال شده اما وضعیت آن هنوز در انتظار (PENDING) است , آیا مایلید مجددا ارسال کنید ؟");
                                msgwinv.ShowDialog();
                                if (msgwinv.DialogResult is false)
                                {
                                    GoFullExitNow();
                                }
                            }
                            else if (_Issent.Contains("FAILED"))
                            {
                                Msgwin msgwinv = new Msgwin(true, $"این صورت حساب اصلی (فروش) به شماره (حواله) {_factornum} قبلا ارسال شده اما وضعیت آن در ناموفق (FAILED) شده است , گاهی اوقات به دلیل اشکالات سامانه , میتواند صورت حسابی که ناموفق خورده نیز در کارپوشه ثبت شده باشد لزا پیشنهاد میشود قبل از ارسال , با شماره مالیاتی در کارپوشه جستجو کنید ,آیا مایلید مجددا ارسال کنید ؟");
                                msgwinv.ShowDialog();
                                if (msgwinv.DialogResult is false)
                                {
                                    GoFullExitNow();
                                }
                            }
                        }
                    }

                    if (_HEAD_EXTENDED.ins == 2) //اگر از نوع اصلاحی است
                    {
                        //var _inno_number = TheFunctions.InnoAddZeroes(_NUMBER_);
                        var _Corrective_ = dbms.DoGetDataSQL<string>($"SELECT TheStatus FROM dbo.TAXDTL WHERE ApiTypeSent = 1 AND Ins = 2 AND NUMBER = {_NUMBER_} AND TAG = {_TAG_}  ").ToList(); //2 = اصلاحی

                        if (_Corrective_.Contains("PENDING") && CL_MOADIAN.TaxURL == "https://tp.tax.gov.ir/req/api/")
                        {
                            Msgwin msgwinv = new Msgwin(true, $"این صورت حساب از نوع اصلاحی به شماره (حواله) {_factornum} قبلا ارسال شده , اما وضعیت آن هنوز *درانتظار* است, آیا میخواهید مجددا ارسال کنید ؟");
                            msgwinv.ShowDialog();
                            if (msgwinv.DialogResult is false)
                            {
                                GoFullExitNow();
                            }
                        }
                        else if (_Corrective_.Contains("SUCCESS") && CL_MOADIAN.TaxURL == "https://tp.tax.gov.ir/req/api/")
                        {
                            Msgwin msgwinv = new Msgwin(true, $"این صورت حساب از نوع اصلاحی به شماره (حواله) {_factornum} قبلا با موفقیت ارسال و در سامانه ثبت شده , آیا میخواهید مجددا ارسال کنید ؟");
                            msgwinv.ShowDialog();
                            if (msgwinv.DialogResult is false)
                            {
                                GoFullExitNow();
                            }
                        }
                        else if (_Corrective_.Contains("FAILED"))
                        {
                            Msgwin msgwinv = new Msgwin(true, $"این صورت حساب اصلاحی به شماره (حواله) {_factornum} قبلا ارسال شده اما وضعیت آن در ناموفق (FAILED) شده است , گاهی اوقات به دلیل اشکالات سامانه , میتواند صورت حسابی که ناموفق خورده نیز در کارپوشه ثبت شده باشد لزا پیشنهاد میشود قبل از ارسال , با شماره مالیاتی در کارپوشه جستجو کنید ,آیا مایلید مجددا ارسال کنید ؟");
                            msgwinv.ShowDialog();
                            if (msgwinv.DialogResult is false)
                            {
                                GoFullExitNow();
                            }
                        }

                        //irtaxid_RefNum_corrective = "A278R704C75000163ECF64"; //TaxIDWasGenerated
                        if (!string.IsNullOrEmpty(_HEAD_EXTENDED.irtaxid)) { }
                        else
                        {
                            throw new NullyExceptiony("irtaxid is null");
                        }
                    }

                    if (_HEAD_EXTENDED.ins == 3)   //اگر از نوع ابطالی است
                    {
                        //var _inno_number = TheFunctions.InnoAddZeroes(_NUMBER_);

                        var _Cancely_ = dbms.DoGetDataSQL<string>($"SELECT TheStatus FROM dbo.TAXDTL WHERE ApiTypeSent = 1 AND Ins = 3 AND NUMBER = {_NUMBER_} AND TAG = {_TAG_}  ").ToList(); //3 = ابطالی
                        if (_Cancely_.Contains("PENDING") && CL_MOADIAN.TaxURL == "https://tp.tax.gov.ir/req/api/")
                        {
                            Msgwin msgwinv = new Msgwin(true, $"این صورت حساب از نوع ابطالی به شماره (حواله) {_factornum} قبلا ارسال شده , اما وضیعت آن هنوز *درانتظار* است, آیا میخواهید مجددا ارسال کنید ؟");
                            msgwinv.ShowDialog();
                            if (msgwinv.DialogResult is false)
                            {
                                GoFullExitNow();
                            }
                        }
                        else if (_Cancely_.Contains("SUCCESS") && CL_MOADIAN.TaxURL == "https://tp.tax.gov.ir/req/api/")
                        {
                            Msgwin msgwinv = new Msgwin(true, $"این صورت حساب از نوع ابطالی به شماره (حواله) {_factornum} قبلا با موفقیت ارسال و در سامانه ثبت شده , آیا میخواهید مجددا ارسال کنید ؟");
                            msgwinv.ShowDialog();
                            if (msgwinv.DialogResult is false)
                            {
                                GoFullExitNow();
                            }
                        }
                        else if (_Cancely_.Contains("FAILED"))
                        {
                            Msgwin msgwinv = new Msgwin(true, $"این صورت حساب ابطالی به شماره (حواله) {_factornum} قبلا ارسال شده اما وضعیت آن در ناموفق (FAILED) شده است , گاهی اوقات به دلیل اشکالات سامانه , میتواند صورت حسابی که ناموفق خورده نیز در کارپوشه ثبت شده باشد لزا پیشنهاد میشود قبل از ارسال , با شماره مالیاتی در کارپوشه جستجو کنید ,آیا مایلید مجددا ارسال کنید ؟");
                            msgwinv.ShowDialog();
                            if (msgwinv.DialogResult is false)
                            {
                                GoFullExitNow();
                            }
                        }
                        //irtaxid_RefNum_cancel = "A278R704C75000163ECF64"; //TaxIDWasGenerated
                        if (!string.IsNullOrEmpty(_HEAD_EXTENDED.irtaxid)) { }
                        else
                        {
                            throw new NullyExceptiony("irtaxid is null");
                        }
                    }

                    if (_HEAD_EXTENDED.ins == 4) //برگشتی
                    {
                        //var _inno_number = TheFunctions.InnoAddZeroes(_NUMBER_);
                        var _Returny_ = dbms.DoGetDataSQL<string>($"SELECT TheStatus FROM dbo.TAXDTL WHERE ApiTypeSent = 1 AND Ins = 4 AND NUMBER = {_NUMBER_} AND TAG = {_TAG_}  ").ToList(); //4 = برگشتی
                        if (_Returny_.Contains("PENDING") && CL_MOADIAN.TaxURL == "https://tp.tax.gov.ir/req/api/")
                        {
                            Msgwin msgwinv = new Msgwin(true, $"این صورت حساب از نوع برگشتی به شماره (حواله) {_factornum} قبلا ارسال شده , اما وضیعت آن هنوز *درانتظار* است, آیا میخواهید مجددا ارسال کنید ؟");
                            msgwinv.ShowDialog();
                            if (msgwinv.DialogResult is false)
                            {
                                GoFullExitNow();
                            }
                        }
                        else if (_Returny_.Contains("SUCCESS") && CL_MOADIAN.TaxURL == "https://tp.tax.gov.ir/req/api/")
                        {
                            Msgwin msgwinv = new Msgwin(true, $"این صورت حساب از نوع برگشتی به شماره (حواله) {_factornum} قبلا با موفقیت ارسال و در سامانه ثبت شده , آیا میخواهید مجددا ارسال کنید ؟");
                            msgwinv.ShowDialog();
                            if (msgwinv.DialogResult is false)
                            {
                                GoFullExitNow();
                            }
                        }
                        else if (_Returny_.Contains("FAILED"))
                        {
                            Msgwin msgwinv = new Msgwin(true, $"این صورت حساب برگشتی به شماره (حواله) {_factornum} قبلا ارسال شده اما وضعیت آن در ناموفق (FAILED) شده است , گاهی اوقات به دلیل اشکالات سامانه , میتواند صورت حسابی که ناموفق خورده نیز در کارپوشه ثبت شده باشد لزا پیشنهاد میشود قبل از ارسال , با شماره مالیاتی در کارپوشه جستجو کنید ,آیا مایلید مجددا ارسال کنید ؟");
                            msgwinv.ShowDialog();
                            if (msgwinv.DialogResult is false)
                            {
                                GoFullExitNow();
                            }
                        }
                        //Now it can request to returny this invoice :
                        //irtaxid_RefNum_cancel = "A278R704C75000163ECF64"; //TaxIDWasGenerated
                        if (!string.IsNullOrEmpty(_HEAD_EXTENDED.irtaxid)) { }
                        else
                        {
                            throw new NullyExceptiony("irtaxid is null");
                        }
                    }
                }


                #endregion


                CL_MOADIAN.DoSendInvoice(CL_Generaly.ARG_PARAM);
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
                    Msgwin msgwin = new Msgwin(false, "خطا در انجام عملیات , ادامه عملیات فعلا ممکن نیست."); //Unknown
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
                List<MsgTaxModel> ErrosMessages = new List<MsgTaxModel>();

                foreach (var item in CL_ERRLST.ERROR_BODY_LST)
                {
                    if (item.MU is null)
                    {
                        _IsSuccess = false;
                        ErrosMessages.Add(new MsgTaxModel { MessageText_U = $"کالای شماره {item.CODE} واحد مودیان انتخاب نشده" }/* + "\n"*/);
                    }
                    if (item.SSTID is null)
                    {
                        _IsSuccess = false;
                        ErrosMessages.Add(new MsgTaxModel { MessageText_U = $"کالای شماره {item.CODE} شناسه کالا یا خدمات مودیان انتخاب نشده" }/* + "\n"*/);
                        //IsSuccessed = false;
                    }
                }
                if (ErrosMessages.Count > 0)
                {
                    ErrosMessages = ErrosMessages.Select(x => x.MessageText_U).Distinct()
                        .Select(message => new MsgTaxModel { MessageText_U = message }).ToList();

                    new MsgListwin(false, ErrosMessages).ShowDialog();
                }

                if (_IsSuccess)
                {
                    if (!string.IsNullOrEmpty(FactorInfoSent.ReferenceNumber))
                    {
                        Msgwin msgwin = new Msgwin(false, $"صورت حساب با شماره {FactorInfoSent.NUMBER} به کد دهگیری :  {FactorInfoSent.ReferenceNumber} " +
                            $"\n به شماره صورت حساب مالیاتی (شماره مالیاتی) : {FactorInfoSent.TaxID}\n در صف ارسال قرار گرفت , لطفا مدتی بعد بررسی بفرمایید.");
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

                CL_PRC_LOADER.Dispose();
                Application.Current.Shutdown();
            }
        }
    }
}
