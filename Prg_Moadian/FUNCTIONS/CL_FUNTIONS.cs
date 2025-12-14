using Newtonsoft.Json;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.Service;
using Prg_Moadian.SQLMODELS;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TaxCollectData.Library.Business;
using TaxCollectData.Library.Dto.Content;
using static Prg_Moadian.CNNMANAGER.TaxModel;

namespace Prg_Moadian.FUNCTIONS
{
    public class CL_FUNTIONS
    {
        public string SafeRemoveFirstFour(string text)
        {
            // ۱. اگر نال یا خالی بود -> برگرداندن نال یا خالی
            if (string.IsNullOrEmpty(text)) return text;

            // ۲. اگر طول رشته ۴ یا کمتر بود -> یعنی بعد از حذف ۴ تا چیزی نمی‌ماند
            if (text.Length <= 4) return string.Empty;

            // ۳. پرش از ۴ کاراکتر اول و گرفتن بقیه
            return text.Substring(4);
        }
        // در کلاس CL_FUNTIONS اضافه یا جایگزین کنید
        public string GenerateFixedLengthInno(string year, long invoiceNumber)
        {
            // طول ثابت طبق استاندارد: 10 کاراکتر 
            const int FixedLength = 10;
            int yearLen = year.Length; // معمولاً 4
            int serialLen = FixedLength - yearLen; // 6

            // 1. تلاش برای ساخت ده‌دهی (روش قدیم شما)
            string decimalSerial = invoiceNumber.ToString();

            if (decimalSerial.Length <= serialLen)
            {
                // اگر جا می‌شود (مثلاً 10391 در 6 رقم)، با صفر پر کن: 1404010391
                return year + decimalSerial.PadLeft(serialLen, '0');
            }
            else
            {
                // 2. اگر جا نمی‌شود (مثلاً 1000000 که 7 رقم است)، سوئیچ به هگز
                // 1000000 در هگز می‌شود F4240 (پنج رقم) که راحت جا می‌شود.
                string hexSerial = invoiceNumber.ToString("X"); // حروف بزرگ

                // بررسی نهایی که حتی هگز هم سرریز نکند (بعید است تا 16 میلیون)
                if (hexSerial.Length > serialLen)
                    throw new Exception($"Serial number {invoiceNumber} is too large even for Hex!");

                return year + hexSerial.PadLeft(serialLen, '0');
            }
        }
        public static string CODEUN(string cody)
        {
            if (string.IsNullOrEmpty(cody))
            {
                return cody;
            }

            byte[] RawCoded = Encoding.GetEncoding(1256).GetBytes(cody);// ی 237

            var Parsy = Encoding.GetEncoding(1256);
            for (byte i = 0; i < RawCoded.Count(); i++)
            {
                RawCoded[i] = (byte)(RawCoded[i] - 20);
            }
            var result = Parsy.GetString(RawCoded);
            cody = result;
            return cody;
        }
        public static string CODEPS(string plainPassword)
        {
            if (string.IsNullOrEmpty(plainPassword))
            {
                return string.Empty; // Or return cody; or throw new ArgumentNullException(nameof(cody));
            }

            Encoding windows1256 = Encoding.GetEncoding(1256);

            byte[] inputBytes;
            try
            {
                inputBytes = windows1256.GetBytes(plainPassword);
            }
            catch (EncoderFallbackException ex)
            {
                return null; // یا یک مقدار خاص برای نشان دادن خطا
            }

            byte[] processedBytes = new byte[inputBytes.Length];
            for (int i = 0; i < inputBytes.Length; i++)
            {
                processedBytes[i] = (byte)(inputBytes[i] - 10);
            }

            string coreEncoded;
            try
            {
                coreEncoded = windows1256.GetString(processedBytes);
            }
            catch (DecoderFallbackException ex)
            {
                return null; // یا یک مقدار خاص برای نشان دادن خطا
            }
            return coreEncoded;
        }
        public static string DECODEUN(string cody)
        {
            if (string.IsNullOrEmpty(cody))
            {
                return cody;
            }

            byte[] RawCoded = Encoding.GetEncoding(1256).GetBytes(cody);// ی 237

            var Parsy = Encoding.GetEncoding(1256);
            for (byte i = 0; i < RawCoded.Count(); i++)
            {
                RawCoded[i] = (byte)(RawCoded[i] + 20);
            }
            var result = Parsy.GetString(RawCoded);
            cody = result;
            return cody;
        }
        public static string DECODEPS(string cody)
        {
            byte[] RawCoded = Encoding.GetEncoding(1256).GetBytes(cody);// ی 237
            var Parsy = Encoding.GetEncoding(1256);
            for (byte i = 0; i < RawCoded.Count(); i++)
            {
                RawCoded[i] = (byte)(RawCoded[i] + 10);
            }

            var result = Parsy.GetString(RawCoded);
            result = result.Substring(3, result.Length - 6);
            cody = result;
            return cody;
        }

        /// <summary>
        /// Item1: SAL | Item2: MAH | Item3: ROOZ | 
        /// </summary>
        /// <param name="_the_date_"></param>
        /// <returns></returns>
        public Tuple<string, string, string> GetSplitPersianDate(string _the_date_)
        {
            var SAL = _the_date_.Substring(0, 4); //Year
            var MAH = _the_date_.Substring(4, 2); //Month
            var ROOZ = _the_date_.Substring(6, 2); //Day

            return Tuple.Create(SAL, MAH, ROOZ);
        }
        /// <summary>
        /// Get Miladi Date | AD Date
        /// </summary>
        /// <param name="persianDate"></param>
        /// <returns></returns>
        public Tuple<int, int, int> GetGregorianDate(string persianDate)
        {
            int persianYear = int.Parse(persianDate.Substring(0, 4));
            int persianMonth = int.Parse(persianDate.Substring(4, 2));
            int persianDay = int.Parse(persianDate.Substring(6, 2));

            PersianCalendar? persianCalendar = new PersianCalendar();
            DateTime dateTime = persianCalendar.ToDateTime(persianYear, persianMonth, persianDay, 0, 0, 0, 0);

            int gregorianYear = dateTime.Year;
            int gregorianMonth = dateTime.Month;
            int gregorianDay = dateTime.Day;

            return Tuple.Create(gregorianYear, gregorianMonth, gregorianDay);
        }// var td = new DateTime(Convert.ToInt32("1401"), Convert.ToInt32("11"), Convert.ToInt32("14")).ToString("yyyy-MM-dd", CultureInfo.GetCultureInfo("en-US"));

        public DateTime GetGregorianDateTime(string persianDate)
        {
            int persianYear = int.Parse(persianDate.Substring(0, 4));
            int persianMonth = int.Parse(persianDate.Substring(4, 2));
            int persianDay = int.Parse(persianDate.Substring(6, 2));

            PersianCalendar? persianCalendar = new PersianCalendar();
            DateTime dateTime = persianCalendar.ToDateTime(persianYear, persianMonth, persianDay, 0, 0, 0, 0);
            //var now = new DateTimeOffset(TheFunctions.GetGregorianDateTime("14020224")).ToUnixTimeMilliseconds();
            return dateTime;
        }
        public DateTimeOffset ConvertPersianDateToDateTimeOffset(string persianDate)
        {
            var year = int.Parse(persianDate.Substring(0, 4));
            var month = int.Parse(persianDate.Substring(4, 2));
            var day = int.Parse(persianDate.Substring(6, 2));

            var persianCalendar = new PersianCalendar();
            var gregorianDate = persianCalendar.ToDateTime(year, month, day, 0, 0, 0, 0);

            return new DateTimeOffset(gregorianDate, TimeSpan.Zero);
        }
        /// <summary>
        /// تبدیل تاریخ میلادی به شمسی امن بدون در نظر گرفتم منطقه مثال 
        ///  10:47:47.897  2023-08-12
        /// </summary>
        /// <param name="datey"></param>
        /// <returns></returns>
        public string ConvertToPersianDate_Stringy(string datey)
        {
            DateTime InputDate = DateTime.ParseExact(datey, "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

            PersianCalendar pc = new PersianCalendar();
            string persianDate = string.Format("{0}/{1}/{2} {3}",
                pc.GetYear(InputDate),
                pc.GetMonth(InputDate).ToString("00"),
                pc.GetDayOfMonth(InputDate).ToString("00"),
                InputDate.ToString("HH:mm:ss.fff"));
            return persianDate;
            //DateTime date = DateTime.ParseExact("2023-08-12 10:47:47.897", "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            //string persianDate = ConvertToPersianDate(date);
        }
        public string ConvertToPersianDate(DateTime adDate)
        {
            PersianCalendar persianCalendar = new PersianCalendar();

            int year = persianCalendar.GetYear(adDate);
            int month = persianCalendar.GetMonth(adDate);
            int day = persianCalendar.GetDayOfMonth(adDate);
            int hour = adDate.Hour;
            int minute = adDate.Minute;
            int second = adDate.Second;
            int millisecond = adDate.Millisecond;

            string persianDate = $"{year}/{month:00}/{day:00} {hour:00}:{minute:00}:{second:00}.{millisecond:000}";

            return persianDate;
        }
        public int GetNewIDD()
        {
            CL_CCNNMANAGER? dbms = new CL_CCNNMANAGER();

            int NEWIDD = Convert.ToInt32(dbms.DoGetDataSQL<int?>("SELECT MAX(IDD) FROM dbo.TAXDTL").FirstOrDefault());
            if (NEWIDD is 0 || NEWIDD < 0)
                NEWIDD = 1;
            else
                NEWIDD = Convert.ToInt32(dbms.DoGetDataSQL<int?>("SELECT MAX(IDD+1) FROM dbo.TAXDTL").FirstOrDefault());

            return NEWIDD;
        }
        public int GetNewIDDSafe()
        {
            CL_CCNNMANAGER? dbms = new CL_CCNNMANAGER();
            //Make Lock Table with Fake Query
            dbms.DoExecuteSQL("UPDATE TOP(1) dbo.TAXDTL SET IDD = IDD");

            int NEWIDD = Convert.ToInt32(dbms.DoGetDataSQL_Safe<int?>("SELECT MAX(IDD) FROM dbo.TAXDTL").FirstOrDefault());
            if (NEWIDD is 0 || NEWIDD < 0)
                NEWIDD = 1;
            else
                NEWIDD = Convert.ToInt32(dbms.DoGetDataSQL_Safe<int?>("SELECT MAX(IDD+1) FROM dbo.TAXDTL").FirstOrDefault());

            return NEWIDD;
        }
        public void TrackingCodeInquiry(string _the_refrence_code_, string memory, string privateKey, string taxUrl, long _number, int _tag, int _idd)
        {
            CL_CCNNMANAGER? dbms = new CL_CCNNMANAGER();

            if (!Directory.Exists("C:\\correct\\ERLOGS_INQUIRY"))
                Directory.CreateDirectory("C:\\correct\\ERLOGS_INQUIRY");

            var _natijeh = dbms.DoGetDataSQL<TAXDTL>($"SELECT TOP(1) * FROM dbo.TAXDTL WHERE IDD = {_idd} ORDER BY IDD").FirstOrDefault();

            #region MyRegion
            TaxModel.InquiryByReferenceIdModel.Root? root = null;
            try
            {
                List<string>? list = new List<string>();
                list.Add(_the_refrence_code_);
                List<InquiryResultModel>? list2 = TaxApiService.Instance.TaxApis.InquiryByReferenceId(list);
                TaxModel.InquiryByReferenceIdModel? inquiryByReferenceIdModel = new TaxModel.InquiryByReferenceIdModel();
                string? value = list2?.Select((InquiryResultModel x) => x.Data).FirstOrDefault()!.ToString(); // May Error
                root = JsonConvert.DeserializeObject<TaxModel.InquiryByReferenceIdModel.Root>(value);
                root.status = list2[0].Status;
            }
            catch (Exception)
            {
                TaxService? taxService = new TaxService(memory, privateKey, taxUrl);

                taxService.RequestToken();
                root = new TaxModel.InquiryByReferenceIdModel.Root();
                root = taxService.InquiryByReferenceId(_the_refrence_code_);
            }
            #endregion
            var now = DateTime.Now;
            //Random ts = new Random(); ts.Next(0, 500);
            string? pathfile = @$"C:\CORRECT\ERLOGS_INQUIRY\TaxLog{now:yyyyMMdd_HHmmss}_{_idd}-{_number}-{_tag}.txt";
            if (!Directory.Exists("C:\\CORRECT\\ERLOGS_INQUIRY"))
                Directory.CreateDirectory("C:\\CORRECT\\ERLOGS_INQUIRY");

            foreach (var item in root.error)
            {
                File.AppendAllText(pathfile, Environment.NewLine + "خطا: " + item.message);
            }
            File.AppendAllText(pathfile, "\n______________________________________________");
            foreach (var item in root.warning)
            {
                File.AppendAllText(pathfile, Environment.NewLine + "هشدار: " + item.message);
            }

            List<string>? _warn_lst = new List<string>();
            foreach (var item in root.warning)
                _warn_lst.Add(item.code + " | " + item.message);

            List<string>? _er_lst = new List<string>();
            foreach (var item in root.error)
                _er_lst.Add(item.code + " | " + item.message);

            //string? ERVALS = (_er_lst != null && _er_lst.Count > 0) ? $"{string.Join(",", _er_lst)}" : "NULL";
            //string? WRVALS = (_warn_lst != null && _warn_lst.Count > 0) ? $"{string.Join(",", _warn_lst)}" : "NULL";

            string? ERVALS = _er_lst.Count > 0 ? string.Join(",", _er_lst) : null;
            string? WRVALS = _warn_lst.Count > 0 ? string.Join(",", _warn_lst) : null;

            const string updateSql = @"UPDATE dbo.TAXDTL SET TheConfirmationReferenceId=@ConfirmationReferenceId, TheError=@TheError, TheWarning=@TheWarning, TheStatus=@TheStatus, TheSuccess=@TheSuccess WHERE RefrenceNumber=@RefrenceNumber";
            var updateParams = new
            {
                ConfirmationReferenceId = CL_MOADIAN.SafeString((string?)root.confirmationReferenceId, 100),
                TheError = CL_MOADIAN.SafeString(ERVALS, 4000),
                TheWarning = CL_MOADIAN.SafeString(WRVALS, 4000),
                TheStatus = CL_MOADIAN.SafeString(root.status, 50),
                TheSuccess = Convert.ToByte(root.success),
                RefrenceNumber = CL_MOADIAN.SafeString(_the_refrence_code_, 100)
            };

            dbms.DoExecuteSQL(updateSql, updateParams);

            //string? test = $"UPDATE dbo.TAXDTL SET TheConfirmationReferenceId=N'{root.confirmationReferenceId}', TheError=N'{(ERVALS)}' ,TheStatus=N'{root.status}', TheSuccess={Convert.ToByte(root.success)} WHERE IDD={_idd}";
            //dbms.DoExecuteSQL(test);
        }
        public static bool IsApplicationRunning()
        {
            // Get the current process
            Process? currentProcess = Process.GetCurrentProcess();

            // Get all running processes with the same process name
            Process[]? runningProcesses = Process.GetProcessesByName(currentProcess.ProcessName);

            // Check if there is more than one instance running (excluding the current process)
            return runningProcesses.Length > 1;
        }
        public string GetBetweenStr(string strSource, string strStart, string strEnd)
        {
            if (strSource.Contains(strStart) || strSource.Contains(strEnd))
            {
                var nstrsorce = strSource.Replace(" ", "");
                var nstrstart = strStart.Replace(" ", "");
                var nstrend = strEnd.Replace(" ", "");
                int Start, End;
                Start = nstrsorce.IndexOf(nstrstart, 0) + nstrstart.Length;
                End = nstrsorce.IndexOf(nstrend, Start);
                if (End == -1)
                {
                    End = nstrsorce.Length;
                }
                return nstrsorce.Substring(Start, End - Start);
            }
            return "";
        }

        public void SendSampleInvoiceTest0(string memory, string privateKey, string taxUrl)
        {
            TaxService? taxService = new TaxService(memory, privateKey, taxUrl);
            RequestTokenModel? model = taxService.RequestToken();

            TaxModel.InvoiceModel.Header header = new TaxModel.InvoiceModel.Header();

            #region ADDONY
            var dt = GetGregorianDateTime("14020224");
            #endregion

            header.Taxid = taxService.RequestTaxId(memory, dt);
            header.Indatim = TaxService.ConvertDateToLong(dt);
            header.Indati2m = TaxService.ConvertDateToLong(dt);

            header.Inty = 1;
            header.Inno = "1020";
            header.Irtaxid = "";
            header.Inp = 1;
            header.Ins = 1;
            header.Tins = "10840014242";
            header.Tob = 1;
            header.Bid = "0";
            header.Tinb = "19117484910002";
            header.Sbc = "0";
            header.Bbc = "0";
            header.Ft = 0;
            header.Bpn = "0";
            header.Scln = "0";
            //header.Scc = "0"; // کد گمرک محل اظهار فروشنده
            header.Crn = "0";
            header.Billid = "0";
            header.Tprdis = 110008424;
            header.Tdis = 0;
            header.Tadis = 110008424;
            header.Tvam = 9900757;
            header.Todam = 0;
            header.Tbill = 119909181;
            header.Setm = 1;
            header.Cap = 110008424;
            header.Insp = 0;
            header.Tvop = 0;
            header.Tax17 = 0;
            header.Cdcd = 0;
            header.Tonw = 0;
            header.Torv = 0;
            header.Tocv = 0;

            List<TaxModel.InvoiceModel.Body>? bodies = new List<TaxModel.InvoiceModel.Body>();

            bodies.Add(new TaxModel.InvoiceModel.Body
            {
                Sstid = "1254219865985",
                Sstt = "تستی",
                Mu = "لیتر",
                Am = 10.96M,
                Fee = 5263561,
                Cfee = 0,
                Cut = "0",
                Exr = 0,
                Prdis = 57688628,
                Dis = 0,
                Adis = 57688628,
                Vra = 9,
                Vam = 5191976,
                Odt = "0",
                Odr = 0,
                Odam = 0,
                Olt = "0",
                Olr = 0,
                Olam = 0,
                Consfee = 0,
                Spro = 0,
                Bros = 0,
                Tcpbs = 0,
                Cop = 0,
                Vop = 0,
                Bsrn = "",
                Tsstam = 62880604,
                Nw = 0,
                Ssrv = 0,
                Sscv = 0
            });
            //اگر یکی بیشتر نباشه  خطا میده
            bodies.Add(new TaxModel.InvoiceModel.Body
            {
                Sstid = "1254219865984",
                Sstt = "روغن موتوسل",
                Mu = "لیتر",
                Am = 9.94M,
                Fee = 5263561,
                Cfee = 0,
                Cut = "0",
                Exr = 0,
                Prdis = 52319796,
                Dis = 0,
                Adis = 52319796,
                Vra = 9,
                Vam = 4708781,
                Odt = "0",
                Odr = 0,
                Odam = 0,
                Olt = "0",
                Olr = 0,
                Olam = 0,
                Consfee = 0,
                Spro = 0,
                Bros = 0,
                Tcpbs = 0,
                Cop = 0,
                Vop = 0,
                Bsrn = "",
                Tsstam = 57028577,
                Nw = 0,
                Ssrv = 0,
                Sscv = 0
            });

            List<TaxModel.InvoiceModel.Payment>? payments = new List<TaxModel.InvoiceModel.Payment>();

            payments.Add(new TaxModel.InvoiceModel.Payment
            {
                Iinn = "0",
                Acn = "252544",
                Trmn = "2356566",
                Trn = "252545",
                Pcn = "6037991785693260",
                Pid = "19117484910002",
                Pdt = TaxService.ConvertDateToLong(new DateTime(2023, 04, 20)),
                Pmt = 0,
                Pv = 0
            });

            TaxModel.SendInvoicesModel sendInvoicesModel = taxService.SendInvoices(header, bodies, payments);


            var sr = sendInvoicesModel.ReferenceNumber;
        }
        public void SendSampleInvoiceTest1(string memory, string privateKey, string taxUrl)
        {
            //کدملی / کد فراگیر / شناسه ملی
            //10840014242
            //شماره رهگیری ثبت نام
            //1025557530
            //نام شرکت/ مودی / تشکل قانونی / واحد صنفی
            //مجتمع تولیدی یزد سپار
            //نوع مودی
            //حقوقی
            //شماره اقتصادی
            //10840014242
            //MemoryID: A114K8

            TaxService? taxService = new TaxService(memory, privateKey, taxUrl);
            RequestTokenModel? model = taxService.RequestToken();
            TaxModel.InvoiceModel.Header header = new TaxModel.InvoiceModel.Header();

            var dt = GetGregorianDateTime("14020310"); // تا حداکثر تاریخ 7 روز قبل
            header.Taxid = taxService.RequestTaxId(memory, dt);
            header.Indatim = TaxService.ConvertDateToLong(dt);
            header.Indati2m = TaxService.ConvertDateToLong(dt);

            header.Inty = 1; //نوع صورتحساب
            header.Inno = "0000000001"; //سریال صورتحساب داخلی حافظه مالیاتی
            header.Irtaxid = ""; //شماره منحصر به فرد مالیاتی صورتحساب مرجع
            header.Inp = 1; //الگوی صورتحساب
            header.Ins = 1; //موضوع صورتحساب
            header.Tins = "10840014242"; //شماره اقتصادی فروشنده
            header.Tob = 2; //نوع شخص خریدار ---- حقوقی 2
            header.Bid = "10840011560"; //شناسه ملی/ شماره ملی/ شناسه مشارکت مدنی/ کد فراگیر اتباع غیر ایرانی خریدار
            header.Tinb = "10840011560"; //شماره اقتصادی خریدار
            header.Sbc = "0"; //کد شعبه فروشنده
            header.Bbc = "0"; //کد شعبه خریدار
            header.Ft = 0; //نوع پرواز
            //header.Bpn = "0"; //شماره گذرنامه خریدار
            header.Scln = "0";
            //header.Scc = "0"; کد گمرک محل اظهار
            header.Crn = "0";
            header.Billid = "0";
            header.Tprdis = 22822800; //مجموع مبلغ قبل از کسر تخفیف
            header.Tdis = 0; //مجموع تخفیفات
            header.Tadis = 22822800; //مجموع مبلغ پس از کسر تخفیف
            header.Tvam = 2054052; //مجموع مالیات بر ارزش افزوده
            header.Todam = 0; //مجموع سایر مالیات، عوارض و وجوه قانونی
            header.Tbill = 24876852; //مجموع صورتحساب
            header.Setm = 1; //روش تسویه
            header.Cap = 22822800; //مبلغ پرداختی نقدی
            header.Insp = 0;
            header.Tvop = 0;
            header.Tax17 = 0;
            header.Cdcd = 0;
            header.Tonw = 0;
            header.Torv = 0;
            header.Tocv = 0;

            List<TaxModel.InvoiceModel.Body>? bodies = new List<TaxModel.InvoiceModel.Body>();

            bodies.Add(new TaxModel.InvoiceModel.Body
            {
                Sstid = "2904985300019", //شناسه کالا/خدمت
                Sstt = "خامه  فله", // شرح کالا/ خدمت
                Mu = "164", //واحد اندازه گیری
                //164	کیلوگرم
                Am = 22.8M, //تعداد/مقدار
                Fee = 1001000, //مبلغ واحد
                Cfee = 0,
                Cut = "IRR", // کد نوع ارز
                Exr = 0,
                Prdis = 22822800, //مبلغ قبل از تخفیف
                Dis = 0, //مبلغ تخفیف
                Adis = 22822800, //مبلغ بعد از تخفیف
                Vra = 9,
                Vam = 2054052, //مبلغ مالیات بر ارزش افزوده
                Odt = "0",
                Odr = 0,
                Odam = 0,
                Olt = "0",
                Olr = 0,
                Olam = 0,
                Consfee = 0,
                Spro = 0,
                Bros = 0,
                Tcpbs = 0,
                Cop = 0,
                Vop = 0,
                Bsrn = "",
                Tsstam = 24876852, //مبلغ کل کالا/خدمت
                Nw = 0,
                Ssrv = 0, //ارزش ریالی کالا
                Sscv = 0
            });

            List<TaxModel.InvoiceModel.Payment>? payments = new List<TaxModel.InvoiceModel.Payment>();

            TaxModel.SendInvoicesModel sendInvoicesModel = taxService.SendInvoices(header, bodies, payments);
            var textBox_Peygiri = sendInvoicesModel.ReferenceNumber;
            Thread.Sleep(1000);

            #region PeyGiri
            TaxModel.InquiryByReferenceIdModel.Root? root = null;
            try
            {
                List<string>? list = new List<string>();
                list.Add(textBox_Peygiri);
                List<InquiryResultModel>? list2 = TaxApiService.Instance.TaxApis.InquiryByReferenceId(list);
                TaxModel.InquiryByReferenceIdModel? inquiryByReferenceIdModel = new TaxModel.InquiryByReferenceIdModel();
                string? value = list2.Select((InquiryResultModel x) => x.Data).FirstOrDefault()!.ToString(); // May Error
                root = JsonConvert.DeserializeObject<TaxModel.InquiryByReferenceIdModel.Root>(value);
                root.status = list2[0].Status;
            }
            catch (Exception)
            {
                TaxService? taxService0 = new TaxService(memory, privateKey, taxUrl);

                taxService0.RequestToken();
                root = new TaxModel.InquiryByReferenceIdModel.Root();
                root = taxService.InquiryByReferenceId(textBox_Peygiri);
            }

            var now = DateTime.Now;
            //Random ts = new Random(); ts.Next(0, 500);
            string? pathfile = @$"C:\CORRECT\ERLOGS_INQUIRY\TaxLog{now:yyyyMMdd_HHmmss}_555.txt";
            foreach (var item in root.error)
            {
                File.AppendAllText(pathfile, Environment.NewLine + "خطا: " + item.message);
            }
            File.AppendAllText(pathfile, "\n______________________________________________");
            foreach (var item in root.warning)
            {
                File.AppendAllText(pathfile, Environment.NewLine + "هشدار: " + item.message);
            }
            #endregion
        }

        private void Testy()
        {
            #region DBG1
            //1401/01/02
            //2022-03-22
            //var tid = taxService.RequestTaxId(MemoryID, new DateTime(2022, 03, 22));
            var date1 = TaxService.ConvertDateToLong(new DateTime(2022, 03, 22));
            var date2 = TaxService.ConvertDateToLong(new DateTime(2022, 03, 22));

            var dateTimeOffset = ConvertPersianDateToDateTimeOffset("14011203");
            var unixTimeMilliseconds = dateTimeOffset.ToUnixTimeMilliseconds();
            #endregion

            //۱۴۰۱/۱۱/۱۴
            //Thursday, February 2, 2023
            //2023 - 02 - 03
            //var src_Indati2m_ = dbms.DoGetDataSQL<DateTime>("SELECT CRT FROM dbo.HEAD_LST WHERE NUMBER = 2 AND TAG = 2 ").FirstOrDefault();
        }

        public string InnoEncrypt(string inputNumber)
        {
            if (inputNumber.Length > 10)
            {
                throw new ArgumentException("Input number should not be longer than 10 characters.");
            }
            string? characters = "abcdef";
            Random? random = new Random();
            string? serialNumber = inputNumber;
            while (serialNumber.Length < 10)
            {
                int index = random.Next(characters.Length);
                serialNumber += characters[index];
            }
            return serialNumber;
        }
        public string InnoDecrypt(string serialNumber)
        {
            if (!Regex.IsMatch(serialNumber, "^[a-fA-F0-9]{10}$"))
            {
                throw new ArgumentException("Serial number does not match the pattern ^[a-fA-F0-9]{10}$.");
            }
            // Extract the digits from the serial number
            Match? match = Regex.Match(serialNumber, @"\d+");
            if (match.Success)
            {
                return match.Value;
            }
            return "No number found in the serial number.";
        }

        public string InnoAddZeroes(string num)
        {
            while (num.Length < 10)
            {
                num = "0" + num;
            }
            return num;
        }
        public string InnoRemoveZeroes(string num)
        {
            int i = 0;
            while (i < num.Length && num[i] == '0')
            {
                i++;
            }
            return num.Substring(i);
        }

        public DataTable GetTaxMessageErWr(string resultValue)
        {
            DataTable dataTable = new DataTable();
            dataTable.Columns.Add("ROW", typeof(int));
            dataTable.Columns.Add("CODE", typeof(int));
            dataTable.Columns.Add("MESSAGE", typeof(string));

            string[] messages = resultValue.Split(',');

            foreach (string message in messages)
            {
                string[] parts = message.Trim().Split('|');

                if (parts.Length == 2)
                {
                    int code;
                    if (int.TryParse(parts[0].Trim(), out code))
                    {
                        dataTable.Rows.Add(dataTable.Rows.Count + 1, code, parts[1].Trim());
                    }
                }
            }
            return dataTable;
        }

        public class MsgTaxModel
        {
            public int ROW_U { get; set; }
            public int CODE_U { get; set; }
            public string? MessageText_U { get; set; }
        }
        public IEnumerable<MsgTaxModel> GetNormilizedMsg(string resultValue)
        {
            List<MsgTaxModel> messages = new List<MsgTaxModel>();

            string[] messageArray = resultValue.Split(',');

            foreach (string message in messageArray)
            {
                string[] parts = message.Trim().Split('|');

                if (parts.Length > 0 && int.TryParse(parts[0].Trim(), out int code))
                {
                    messages.Add(new MsgTaxModel { ROW_U = messages.Count + 1, CODE_U = code, MessageText_U = parts[1].Trim() });
                }
            }
            return messages;
        }
    }
}
