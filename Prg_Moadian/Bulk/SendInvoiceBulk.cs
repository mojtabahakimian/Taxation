using Azure;
using Microsoft.Identity.Client;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.FUNCTIONS;
using Prg_Moadian.Generaly;
using Prg_Moadian.Service;
using Prg_Moadian.SQLMODELS;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection.Metadata;
using System.Text.Json;
using System.Threading.Tasks;
using TaxCollectData.Library.Business;
using TaxCollectData.Library.Dto.Content;
using TaxCollectData.Library.Dto.Transfer;
using static Prg_Moadian.CNNMANAGER.TaxModel;
using static Prg_Moadian.CNNMANAGER.TaxModel.InvoiceModel;
using static Prg_Moadian.Generaly.CL_Generaly;

namespace Prg_Moadian.Bulk
{
    public class InvoiceValidationException : Exception
    {
        public long InvoiceNumber { get; }
        public InvoiceValidationException(long number, string message)
            : base(message) => InvoiceNumber = number;
    }
    public sealed class SendInvoiceBulk
    {
        private const int MaxPerRequest = 99;

        private readonly CL_CCNNMANAGER _db;
        private readonly SAZMAN _sazman;
        private readonly TaxService _taxService;
        private readonly string _memoryId;
        private readonly bool _isSandbox;
        private readonly CL_FUNTIONS _fn = new CL_FUNTIONS();

        public string CALLER_NAME { get; set; } = "m";

        private SendInvoiceBulk(CL_CCNNMANAGER db,
                          SAZMAN sazman,
                          TaxService taxService)
        {
            _db = db;
            _sazman = sazman;
            _taxService = taxService;
        }

        public SendInvoiceBulk(CL_CCNNMANAGER db, string taxUrl)
        {
            _db = db;
            _sazman = _db.DoGetDataSQL<SAZMAN>("SELECT TOP 1 * FROM dbo.SAZMAN").First();
            _isSandbox = taxUrl.Contains("sandbox", StringComparison.OrdinalIgnoreCase);
            _memoryId = _isSandbox ? _sazman.MEMORYIDsand.Trim() : _sazman.MEMORYID.Trim();
            var privateKey = _sazman.PRIVIATEKEY
                .Replace("-----BEGIN PRIVATE KEY-----\r\n", string.Empty)
                .Replace("\r\n-----END PRIVATE KEY-----\r\n", string.Empty)
                .Trim();
            _taxService = new TaxService(_memoryId, privateKey, taxUrl);


            RequestTokenModel? model = _taxService.RequestToken();
        }

        /// <summary>
        /// ارسال گروهی فاکتورها با تگ مشخص
        /// </summary>
        /// <param name="invoiceNumbers">لیست شماره فاکتورها</param>
        /// <param name="inty_value">نوع صورت حساب</param>
        /// <param name="setm_value">روش تسویه</param>
        /// <param name="progress">گزارش پیشرفت</param>
        /// <param name="useCustomDate">استفاده از تاریخ سفارشی</param>
        /// <param name="customDateText">متن تاریخ سفارشی (مثال: 1404/08/24)</param>
        public async Task<BulkSendResult> SendAsync(IEnumerable<long> invoiceNumbers, int tag, int? inty_value = default, int? setm_value = default,
            IProgress<int>? progress = null, bool useCustomDate = false, string customDateText = null)
        {
            var cer = new CustomExceptErMsg();

            var result = new BulkSendResult();
            var numbers = invoiceNumbers.Distinct().ToList();
            if (!numbers.Any()) return result;

            progress?.Report(25);

            int totalNumbers = numbers.Count;
            int buildProgress = 0;

            // 1. تبدیل به DTO و جمع‌آوری رکوردها
            var allDtos = new List<InvoiceDto>();
            var allRecords = new Dictionary<InvoiceDto, List<TAXDTL>>();
            foreach (var number in numbers)
            {

                try
                {
                    var (dto, records) = BuildDtoAndRecords(number, tag, inty_value, setm_value, useCustomDate, customDateText);

                    allDtos.Add(dto);
                    allRecords[dto] = records;
                }
                catch (InvoiceValidationException ex)
                {
                    // فاکتور نامعتبر را به لیست خطاها اضافه می‌کنیم و ادامه می‌دهیم
                    result.Failures[ex.InvoiceNumber] = ex.Message;
                    //continue;
                }
                catch (Exception ex)
                {
                    // بقیه‌ی خطاها را هم ثبت می‌کنیم
                    // و به کاربر فقط پیام مناسب
                    var friendly = cer.ExpecMsgEr(ex) ?? $"خطای داخلی در پردازش فاکتور {number} . لطفاً بعداً تلاش کنید.";

                    result.Failures[number] = friendly + $" شماره {number} ";
                    //result.Failures[number] = ex.Message;

                    //continue;
                }

                // ✅ هوشمندانه گزارش پیشرفت در مرحله‌ی ساخت
                buildProgress++;
                double buildPercent = (double)buildProgress / totalNumbers;
                progress?.Report((int)(buildPercent * 50));  // ← تا ۵۰٪ مربوط به مرحله ساخت
            }

            // 2. بسته‌بندی در بسته‌های MaxPerRequest و ارسال
            var batches = allDtos
                .Select((dto, idx) => new { dto, idx })
                .GroupBy(x => x.idx / MaxPerRequest, x => x.dto)
                .Select(g => g.ToList());

            foreach (var batch in batches)
            {
                try
                {
                    //------------------------------------
                    // ❶ یک «تخمینی» قبل از ارسال (اختیاری)
                    progress?.Report(0);

                    //var response = TaxApiService.Instance.TaxApis.SendInvoices(batch, null);

                    //------------------------------------
                    // ❷ فراخوانی وب‌سرویس (ترجیحاً نسخهٔ async)
                    var response = await Task
                        .Run(() => TaxApiService.Instance.TaxApis.SendInvoices(batch, null))
                        .ConfigureAwait(false);


                    var batchRecords = batch.Select(dto => allRecords[dto]).ToList();

                    // درج در پایگاه
                    PersistChunk(batch, batchRecords, response.Body.Result, tag);
                    result.Success += response.Body.Result.Count;


                    progress?.Report(response.Body.Result.Count);
                }
                catch (Exception ex)
                {
                    var friendly = cer.ExpecMsgEr(ex) ?? "خطا در اتصال به سرور مودیان برای ارسال گروهی.";
                    // اگر ارسال یک بسته به کل شکست خورد، برای همه‌ی DTOهای آن بسته خطا بزن
                    foreach (var dto in batch)
                    {
                        // استخراج شماره از Inno
                        var num = long.Parse(dto.Header.Inno!.Substring(_sazman.YEA.ToString().Length + 2));
                        //result.Failures[num] = ex.Message;

                        result.Failures[num] = friendly;
                    }
                }
            }

            return result;
        }

        private (InvoiceDto Dto, List<TAXDTL> Records) BuildDtoAndRecords(long number, int tag, int? Inty_Value = default, int? Setm_Value = default, bool useCustomDate = false, string customDateText = null)
        {
            // 1. بارگذاری HEAD_LST_EXTENDED
            var headExt = _db.DoGetDataSQL<HEAD_LST_EXTENDED>($"SELECT * FROM dbo.HEAD_LST_EXTENDED WHERE NUMBER={number} AND TGU={tag}").FirstOrDefault();
            if (headExt == null)
            {
                //if (Inty_Value != null || Setm_Value != null) //نوع صورت حساب
                //{

                //    _db.DoExecuteSQL(@$"INSERT INTO dbo.HEAD_LST_EXTENDED(NUMBER, tgu, inty, inp, ins, sbc, Bbc, ft, bpn, scln, scc, cdcn, cdcd, crn, billid, todam, tonw, torv, tocv, setm, cap, insp, tvop, tax17, cut, irtaxid)
                //                VALUES({number}, {tag}, DEFAULT, DEFAULT, DEFAULT, DEFAULT, DEFAULT, DEFAULT, DEFAULT, DEFAULT, DEFAULT, DEFAULT, DEFAULT, DEFAULT, DEFAULT, DEFAULT, DEFAULT, DEFAULT, DEFAULT, DEFAULT, DEFAULT, DEFAULT, DEFAULT, DEFAULT, '2', DEFAULT);");

                //    headExt = _db.DoGetDataSQL<HEAD_LST_EXTENDED>($"SELECT * FROM dbo.HEAD_LST_EXTENDED WHERE NUMBER={number} AND TGU={tag}").FirstOrDefault();

                //}
                //else
                //{
                //    throw new NullyExceptiony($"HEAD_LST_EXTENDED not found for invoice {number} tag {tag}");
                //}

                // ✅ ساخت خودکار رکورد پیش‌فرض برای سربرگ مودیان
                // اگر نوع صورت حساب یا روش تسویه از UI ارسال شده، از اونها استفاده می‌کنیم
                // در غیر این صورت از مقادیر پیش‌فرض استفاده می‌شود
                int defaultInty = Inty_Value ?? 1;  // پیش‌فرض: نوع اول
                int defaultSetm = Setm_Value ?? 1;  // پیش‌فرض: نقدی

                try
                {
                    _db.DoExecuteSQL(@$"INSERT INTO dbo.HEAD_LST_EXTENDED(NUMBER, tgu, inty, inp, ins, sbc, Bbc, ft, bpn, scln, scc, cdcn, cdcd, crn, billid, todam, tonw, torv, tocv, setm, cap, insp, tvop, tax17, cut, irtaxid)
                            VALUES({number}, {tag}, {defaultInty}, 1, 1, DEFAULT, DEFAULT, DEFAULT, DEFAULT, DEFAULT, DEFAULT, DEFAULT, DEFAULT, DEFAULT, DEFAULT, 0, DEFAULT, DEFAULT, DEFAULT, {defaultSetm}, DEFAULT, DEFAULT, DEFAULT, DEFAULT, '2', DEFAULT);");
                }
                catch { }

                headExt = _db.DoGetDataSQL<HEAD_LST_EXTENDED>($"SELECT * FROM dbo.HEAD_LST_EXTENDED WHERE NUMBER={number} AND TGU={tag}").FirstOrDefault();

                // اگر بعد از INSERT هم نتونستیم بخونیم، خطا بده (این نباید اتفاق بیفته)
                if (headExt == null)
                {
                    throw new NullyExceptiony($"Failed to create HEAD_LST_EXTENDED for invoice {number} tag {tag}");
                }
            }


            if (Inty_Value != null) //نوع صورت حساب
            {
                headExt.inty = Inty_Value;
            }
            if (Setm_Value != null) //روش تسویه
            {
                headExt.setm = Setm_Value;
            }

            // بارگذاری HEAD_LST برای پیدا کردن DEPATMAN
            var mainHead = _db
                .DoGetDataSQL<HEAD_LST>($"SELECT * FROM dbo.HEAD_LST WHERE NUMBER={number} AND TAG={tag}")
                .FirstOrDefault()
                ?? throw new NullyExceptiony($"HEAD_LST not found for invoice {number} tag {tag}");

            // 2. بارگذاری خطوط فاکتور
            var lines = FetchInvoiceLines(number, tag);
            if (!lines.Any())
                throw new InvoiceValidationException(number, $"Invoice {number}/{tag} has no detail lines");

            // 3. اعتبارسنجی هر خط
            bool contin = true;
            foreach (var ln in lines)
            {
                if (string.IsNullOrEmpty(ln.sstid))
                {
                    CL_ERRLST.ERROR_BODY_LST.Add(new CL_ERRLST.ER_BOD_MODEL { CODE = ln.CODE, SSTID = ln.sstid });
                    contin = false;
                }
                if (string.IsNullOrEmpty(ln.mu))
                {
                    CL_ERRLST.ERROR_BODY_LST.Add(new CL_ERRLST.ER_BOD_MODEL { CODE = ln.CODE, MU = ln.mu });
                    contin = false;
                }
            }
            if (!contin)
                throw new InvoiceValidationException(number, $"Validation failed (empty SSTID or MU) for invoice {number}");

            // 4. اصلاح آدرس و شعبه از جدول DEPART
            var depart = _db
                .DoGetDataSQL<DEPART>($"SELECT * FROM dbo.DEPART WHERE DEPATMAN = {mainHead.DEPATMAN}")
                .FirstOrDefault();
            if (depart != null)
            {
                if (!string.IsNullOrEmpty(depart.BBC)) headExt.bbc = depart.BBC;
                if (!string.IsNullOrEmpty(depart.PCODE)) headExt.bpc = depart.PCODE;
            }

            // 5. تعیین ECODE_M و CODEMELI_M
            string srcEcode = lines.First().ECODE;
            string ECODE_M = null, CODEMELI_M = null;//= lines.First().MCODEM;
            if (headExt.inty == 1)
            {
                // بررسی خالی نبودن کد اقتصادی برای نوع اول صورتحساب
                if (string.IsNullOrWhiteSpace(srcEcode))
                {
                    throw new NullyExceptiony("ECODE is null or empty");
                }

                if (lines.First().tob == 1) // حقیقی
                {
                    if (srcEcode.Length > 14) throw new InvoiceValidationException(number, "Over Length 14 Ecode for tob=1");
                    ECODE_M = srcEcode;
                }
                else // حقوقی
                {
                    if (srcEcode.Length > 11) throw new InvoiceValidationException(number, "Over Length 11 Ecode for tob=2");
                    ECODE_M = srcEcode;
                }
            }


            // 6. FLOATFIXER: گردکردن مقادیر
            bool isReturn = headExt.ins == 4;
            foreach (var ln in lines)
            {
                ln.MABL = Math.Truncate(ln.MABL ?? 0);
                ln.N_MOIN = Math.Truncate(ln.N_MOIN ?? 0);
                ln.MEGHk = Math.Round(ln.MEGHk ?? 0, 4);
                if (isReturn)
                {
                    ln.MEGH_MAR = Math.Round(ln.MEGH_MAR ?? 0, 4);
                    ln.MEGHk -= ln.MEGH_MAR;
                }
                ln.MABL_K = Math.Truncate((ln.MABL ?? 0) * (ln.MEGHk ?? 0));
                ln.mabkbt = ln.MABL_K - (ln.N_MOIN ?? 0);
                if ((ln.vra ?? 0) > 0 && (ln.IMBAA ?? 0) <= 0)
                    throw new InvoiceValidationException(number, "NO IMBAA BUT HAS VRA");
                if ((ln.IMBAA ?? 0) > 0)
                {
                    ln.IMBAA = Math.Truncate((decimal)(ln.mabkbt * (ln.vra ?? 0) / 100));
                }
                ln.mabkn = ln.mabkbt + (ln.IMBAA ?? 0);
            }

            if (Setm_Value != null) //اگر کاربر انتخاب کرده , مقدار انتخابی اون رو اعمال کن و کاری به مقدار داخل دیتابیس برای فقط همین فیلد نداشته باش
            {
                decimal Tprdis_sum = lines.Sum(l => l.MABL_K ?? 0); //مجموع مبلغ قبل از کسر تخفیف //INVO_LST	Sum(MABL_K)
                decimal Tdis_sum = lines.Sum(l => l.N_MOIN ?? 0); //مجموع تخفیفات //INVO_LST	Sum(N_MOIN)
                decimal Tadis_sum = lines.Sum(l => l.mabkbt ?? 0); //مجموع مبلغ پس از کسر تخفیف //INVO_LST 	Sum(INVO_LST.MABL_K - INVO_LST.N_MOIN AS mabkbt)
                decimal Tvam_sum = lines.Sum(l => l.IMBAA ?? 0); //مجموع مالیات بر ارزش افزوده //INVO_LST	Sum(IMBAA)

                decimal Tbill_sum = Tadis_sum + Tvam_sum + (headExt.todam ?? 0); // Todam هدر، اگر متفاوت از مجموع ردیف‌ها است
                                                                                 // یا Tbill_sum = Tadis_sum + Tvam_sum + Todam_sum_from_lines + (headExt.todam_header_only ?? 0)
                                                                                 // در اینجا فرض می‌کنیم headExt.todam مبلغ کلی سایر عوارض هدر است.
                                                                                 // اگر Todam باید از مجموع ردیف‌ها بیاید: Tbill_sum = Tadis_sum + Tvam_sum + Todam_sum_from_lines; و headExt.todam = Todam_sum_from_lines

                // اگر headExt.todam باید مجموع ردیف‌ها باشد:
                Tbill_sum = Tadis_sum + Tvam_sum;  //مجموع صورت حساب //mabkn	INVO_LST.MABL_K-INVO_LST.N_MOIN+INVO_LST.IMBAA AS mabkn

                // محاسبه cap و insp
                // اگر setmValueFromUi آمده، اولویت با آن است. در غیر این صورت، از مقادیر headExt که از دیتابیس خوانده شده استفاده می‌شود.
                // capValueFromUi فقط زمانی استفاده می‌شود که setm نهایی ۳ (نقدی/نسیه) باشد.
                decimal? capForCalculation = (headExt.cap ?? 0);
                if (headExt.setm.HasValue && headExt.setm != 3) // اگر روش تسویه از UI آمده و نقدی/نسیه نیست، cap ورودی UI نادیده گرفته می‌شود
                {
                    capForCalculation = null; // اجازه بده CalculateCapInsp تصمیم بگیرد
                }

                (headExt.cap, headExt.insp, string? capInspError) = CalculateCapInsp((int)headExt.setm, Tbill_sum, capForCalculation, (int)Inty_Value);
                if (capInspError != null)
                {
                    var buildResult = capInspError + $"#capInspError# (صورتحساب شماره {number})";
                    throw new NullyExceptiony(buildResult);
                }

                // بررسی نهایی مجموع cap و insp با Tbill
                if (Math.Abs((headExt.cap ?? 0) + (headExt.insp ?? 0) - Tbill_sum) > 0.01m) // تلرانس برای مقایسه decimal
                {
                    var buildResult = $"#Tbill_insp_cap# مجموع مبلغ نقدی ({headExt.cap ?? 0}) و نسیه ({headExt.insp ?? 0}) با مبلغ کل صورتحساب ({Tbill_sum}) برای فاکتور {number} پس از محاسبات همخوانی ندارد. لطفا تنظیمات روش تسویه را بررسی کنید.";
                    throw new NullyExceptiony(buildResult);
                }
            }

            // 7. خالی‌سازی شعبه‌ها در صورت <=0
            if (long.TryParse(headExt.bbc, out var bb) && bb <= 0) headExt.bbc = null;
            if (long.TryParse(headExt.sbc, out var sb) && sb <= 0) headExt.sbc = null;

            // 8. صادرات (الگوی 7)
            bool isExport = headExt.inp == 7;
            if (!isExport) { headExt.cut = null; headExt.exr = null; }

            // محاسبه تاریخ و TaxId
            //var dt = _fn.GetGregorianDateTime(lines.First().DATE_N.ToString());
            // اگر تاریخ سفارشی فعال شده باشد، از آن استفاده کن، در غیر این صورت از تاریخ فاکتور استفاده کن
            DateTime dt;
            if (useCustomDate && !string.IsNullOrWhiteSpace(customDateText))
            {
                try
                {
                    // تبدیل تاریخ شمسی سفارشی به میلادی
                    // فرمت انتظاری: 1404/08/24
                    var customDateInt = int.Parse(customDateText.Replace("/", ""));
                    dt = _fn.GetGregorianDateTime(customDateInt.ToString());
                }
                catch
                {
                    // اگر تاریخ سفارشی نامعتبر بود، از تاریخ فاکتور استفاده کن
                    dt = _fn.GetGregorianDateTime(lines.First().DATE_N.ToString());
                }
            }
            else
            {
                dt = _fn.GetGregorianDateTime(lines.First().DATE_N.ToString());
            }

            //var taxId = _taxService.RequestTaxId(_memoryId, dt);
            //var ts = TaxService.ConvertDateToLong(dt);
            // اعمال اختلاف زمانی سرور
            var dtAdjusted = dt.Add(TokenLifeTime.ServerClockSkew); //جلوگیری از خطای تاریخ
            var taxId = _taxService.RequestTaxId(_memoryId, dtAdjusted);
            var ts = TaxService.ConvertDateToLong(dtAdjusted);

            // 1. دریافت شماره فاکتور (مثلاً 10391)
            long invoiceNum = long.Parse(number.ToString());
            // 2. تولید سریال ۱۰ رقمی استاندارد
            // فرض: _sazman.YEA "1404" است
            string finalInno = _fn.GenerateFixedLengthInno(_sazman.YEA.ToString(), invoiceNum);

            // آماده‌سازی Header
            var header = new InvoiceHeaderDto
            {
                Taxid = taxId,
                Indatim = ts,
                Indati2m = ts,
                Inty = headExt.inty ?? 1,
                Inno = finalInno, //// _fn.InnoAddZeroes($"{_sazman.YEA}00{number}")
                Irtaxid = null,
                Inp = /*headExt.inp ??*/ 1,
                Ins = /*headExt.ins ??*/ 1,
                Tins = _sazman.ECODE,
                Tob = lines.First().tob ?? 2,
                Bid = CODEMELI_M,
                Tinb = ECODE_M,
                Sbc = headExt.sbc,
                Bbc = headExt.bbc,
                Bpc = headExt.bpc,
                Tprdis = lines.Sum(l => l.MABL_K ?? 0),
                Tdis = lines.Sum(l => l.N_MOIN ?? 0),
                Tadis = lines.Sum(l => l.mabkbt ?? 0),
                Tvam = lines.Sum(l => l.IMBAA ?? 0),
                Todam = headExt.todam,
                Tbill = lines.Sum(l => l.mabkn ?? 0),
                Setm = headExt.setm,
                Cap = headExt.cap,
                Insp = headExt.insp,
                Tvop = headExt.tvop,
                Tax17 = headExt.tax17,

                #region MINE
                //Taxid = taxId, //شماره منحصر به فرد مالیاتی
                //Indatim = ts, //تاریخ و زمان صدور صورتحساب (میلادی)
                //Indati2m = ts, //تاریخ و زمان ایجاد صورتحساب (میلادی)
                //Inty = Convert.ToInt32(headExt.inty), //(انواع صورتحساب الکترونیکی 1و2و3) نوع صورتحساب
                //Inno = _fn.InnoAddZeroes($"{_sazman.YEA}00{number}"), //سریال صورتحساب  //NUMBER	 HEAD_LST
                //Irtaxid = null, //شماره منحصر به فرد مالیاتی صورتحساب مرجع
                //Inp = Convert.ToInt32(headExt.inp), //الگوی صورتحساب
                //Ins = Convert.ToInt32(headExt.ins), //موضوع صورتحساب
                //Tins = _sazman.ECODE, //شماره اقتصادی فروشنده //ECODE SAZMAN ******************************************************************************************
                //Tob = Convert.ToInt32(lines.First().tob), //نوع شخص خریدار
                //Bid = CODEMELI_M, //شماره/شناسه ملی/شناسه مشارکت مدنی/کد فراگیر خریدار //MCODEM	SAZMAN
                //Tinb = ECODE_M, //شماره اقتصادی خریدار //ECODE CUST_HESAB
                //Sbc = headExt.sbc, //کد شعبه فروشنده //MCODEM	CUST_HESAB
                //Bbc = headExt.bbc, //کد شعبه خریدار
                //Bpc = headExt.bpc, //کد پستی خریدار
                //Ft = 0, //نوع پرواز
                //Scln = null, //شماره پروانه گمرکی فروشنده
                //Crn = null, //شناسه یکتای ثبت قرارداد فروشنده
                //Billid = null, //شماره اشتراک/شناسه قبض بهره بردار
                //Tprdis = Convert.ToDecimal(lines.Sum(l => l.MABL_K)), //مجموع مبلغ قبل از کسر تخفیف //INVO_LST	Sum(MABL_K)
                //Tdis = Convert.ToDecimal(lines.Sum(l => l.N_MOIN)), //مجموع تخفیفات //INVO_LST	Sum(N_MOIN)
                //Tadis = Convert.ToDecimal(lines.Sum(l => l.mabkbt )), //مجموع مبلغ پس از کسر تخفیف //INVO_LST 	Sum(INVO_LST.MABL_K - INVO_LST.N_MOIN AS mabkbt)
                //Tvam = Convert.ToDecimal(lines.Sum(l => l.IMBAA )), //مجموع مالیات بر ارزش افزوده //INVO_LST	Sum(IMBAA)
                //Todam = Convert.ToDecimal(headExt.todam), //مجموع سایر مالیات , عوارض و وجوه قانونی
                //Tbill = Convert.ToDecimal(lines.Sum(l => l.mabkn )), //مجموع صورت حساب //mabkn	INVO_LST.MABL_K-INVO_LST.N_MOIN+INVO_LST.IMBAA AS mabkn
                //Setm = Convert.ToInt32(headExt.setm), //روش تسویه
                //Cap = Convert.ToDecimal(headExt.cap), //مبلغ پرداختی نقدی
                //Insp = Convert.ToDecimal(headExt.insp), //مبلغ پرداختی نسیه
                //Tvop = Convert.ToDecimal(headExt.tvop), //مجموع سهم مالیات بر ارزش افزوده از پرداخت
                //Tax17 = Convert.ToDecimal(headExt.tax17), //مالیات موضوع ماده 17
                //Cdcd = Convert.ToInt32(headExt.cdcd), //تاریخ کوتاژ اظهارنامه گمرکی
                //Tonw = 0, //مجموع وزن خالص
                //Torv = 0, //مجموع ارزش ریالی
                //Tocv = 0, //مجموع ارزش ارزی
                #endregion
            };

            // آماده‌سازی Body
            var bodies = lines.Select(l => new InvoiceBodyDto
            {

                Sstid = l.sstid, //شناسه کالا/خدمت //CODE	STUF_DEF
                Sstt = l.KALA, //شرح کالا/خدمت //NAME	STUF_DEF
                Mu = l.mu, //واحد اندازه گیری //VNAMES	TCOD_VAHEDS
                Am = l.MEGHk ?? 0, //تعداد/مقدار //MEGH	INVO_LST
                Fee = l.MABL ?? 0, //مبلغ واحد //MABL	INVO_LST
                Prdis = l.MABL_K ?? 0, //مبلغ قبل از تخفیف //MABL_K	INVO_LST
                Dis = l.N_MOIN ?? 0, //مبلغ تخفیف //N_MOIN	INVO_LST
                Adis = l.mabkbt ?? 0, //مبلغ بعد از تخفیف //Sum(INVO_LST.MABL_K - INVO_LST.N_MOIN AS mabkbt)	INVO_LST
                Vra = l.vra ?? 0, //نرخ مالیات بر ارزش افزوده
                Vam = l.IMBAA ?? 0, //مبلغ مالیات بر ارزش افزوده //IMBAA	 INVO_LST
                Tsstam = l.mabkn ?? 0, //مبلغ کل کالا/خدمت //MABL_K	INVO_LST

            }).ToList();

            var dto = new InvoiceDto
            {
                Header = header,
                Body = bodies,
                Payments = new List<PaymentDto>(),
                Extension = new List<InvoiceExtension> { new InvoiceExtension() }
            };

            // ساخت رکوردهای TAXDTL برای درج در پایگاه
            var records = bodies.Select(b => new TAXDTL
            {
                NUMBER = number,
                TAG = tag,
                DATE_N = (int?)lines.First().DATE_N,      // <— این خط اضافه شد
                Taxid = header.Taxid,
                Indatim_Sec = header.Indatim,
                Indati2m_Sec = header.Indati2m,
                Inty = header.Inty,
                Inno = header.Inno,
                Inp = header.Inp,
                Ins = header.Ins,
                Tins = header.Tins,
                Tob = header.Tob,
                Bid = header.Bid,
                Tinb = header.Tinb,
                Sbc = header.Sbc,
                Bbc = header.Bbc,
                Bpc = header.Bpc,
                Tprdis = header.Tprdis,
                Tdis = header.Tdis,
                Tadis = header.Tadis,
                Tvam = header.Tvam,
                Todam = header.Todam,
                Tbill = header.Tbill,
                Setm = header.Setm,
                Cap = header.Cap,
                Insp = header.Insp,
                Tvop = header.Tvop,
                Tax17 = header.Tax17,
                Cdcd = header.Cdcd,
                Sstid = b.Sstid,
                Sstt = b.Sstt,
                Mu = b.Mu,
                Am = b.Am,
                Fee = b.Fee,
                Prdis = b.Prdis,
                Dis = b.Dis,
                Adis = b.Adis,
                Vra = b.Vra,
                Vam = b.Vam,
                Tsstam = b.Tsstam
            }).ToList();

            return (dto, records);
        }

        private List<DRV_TBL> FetchInvoiceLines(long number, int tag)
        {
            string sql = "";
            if (CALLER_NAME == CL_Generaly.MrCorrect) //اگر مسترکارکت هست
            {
                sql = ($@"SELECT DATE_N, NUMBER1, HESAB, ADDRESS, TEL, CODE, ECODE, MCODEM, PCODE, IYALAT, CITY, KALA, MEGH, MABL, MABL_K, VNAMES, N_MOIN, mabkbt, IMBAA, mabkn, BARCODE, MEGHk, MOLAH, DEPART, DEPNAME, NUMBER,tob,DRVD_TBL.sstid,DRVD_TBL.mu,DRVD_TBL.vra, MEGH_MAR
                  FROM(SELECT dbo.HEAD_BACK_ANBAR.NUMBER1, dbo.HEAD_BACK_ANBAR.NUMBER, dbo.HEAD_BACK_ANBAR.DATE_N, dbo.INVO_LST.NUMBER AS INUMBER, dbo.HEAD_BACK_ANBAR.HTAG, dbo.INVO_LST.ANBAR, dbo.INVO_LST.RADIF, dbo.INVO_LST.CODE, dbo.INVO_LST.MEGH, dbo.INVO_LST.MEGHk, dbo.INVO_LST.MEGH_MAR, dbo.INVO_LST.MANDAH, dbo.INVO_LST.MABL, dbo.INVO_LST.MABL_K, dbo.INVO_LST.FROM_A, dbo.INVO_LST.N_RASID, dbo.INVO_LST.MEGH_R, dbo.INVO_LST.RADAH, dbo.INVO_LST.SANAD_NO, dbo.INVO_LST.ANBARF, dbo.INVO_LST.VAHED_K, dbo.STUF_DEF.NAME, dbo.TCOD_ANBAR.NAMES, dbo.TCOD_VAHEDS.NAMES AS VNAMES, dbo.HEAD_BACK_ANBAR.TAH, dbo.HEAD_BACK_ANBAR.MOLAH, dbo.CUSTKIND.CUSTKNAME, dbo.DEPART.DEPNAME, dbo.SHIFT.SHNAME, dbo.CUST_HESAB.NAME AS HESAB, dbo.CUST_HESAB.ADDRESS, dbo.CUST_HESAB.TEL, ISNULL(dbo.STUF_DEF.NAME, N' ')+N' '+ISNULL(dbo.INVO_LST.MANDAH, N' ') AS KALA, dbo.HEAD_BACK_ANBAR.CUST_NO, dbo.INVO_LST.N_KOL, dbo.INVO_LST.N_MOIN, dbo.HEAD_BACK_ANBAR.FNUMCO, dbo.CUST_HESAB.ECODE, dbo.CUST_HESAB.PCODE, dbo.CUST_HESAB.IYALAT, dbo.CUST_HESAB.MCODEM, dbo.CUST_HESAB.CITY, dbo.INVO_LST.MABL_K-dbo.INVO_LST.N_MOIN AS mabkbt, dbo.INVO_LST.IMBAA, dbo.INVO_LST.MABL_K-dbo.INVO_LST.N_MOIN+dbo.INVO_LST.IMBAA AS mabkn, dbo.CUST_HESAB.CODE_E, dbo.HEAD_BACK_ANBAR.TAKHFIF, dbo.HEAD_BACK_ANBAR.MBAA, dbo.STUF_DEF.N_FANI, dbo.HEAD_BACK_ANBAR.SADER, dbo.HEAD_BACK_ANBAR.ANBARF AS ANBARFF, dbo.OTHER_DTL.REQUEST_NO, dbo.OTHER_DTL.BARNAMEH, dbo.OTHER_DTL.DRIVER, dbo.OTHER_DTL.DRIVER_MOB, dbo.OTHER_DTL.CAMIUN_NUM, dbo.OTHER_DTL.MAGHSAD, dbo.OTHER_DTL.CAM_KHALY, dbo.OTHER_DTL.CAM_POOR, dbo.OTHER_DTL.TOZIH, dbo.OTHER_DTL.CAMIUN, dbo.OTHER_DTL_SUB.CODE AS CODEG, dbo.OTHER_DTL_SUB.CAM_KHALY AS CAM_KHALYG, dbo.OTHER_DTL_SUB.CAM_POOR AS CAM_POORG, dbo.OTHER_DTL_SUB.MEGHk AS MEGHkG, dbo.OTHER_DTL_SUB.TOZIH AS TOZIHG, dbo.OTHER_DTL_SUB.VAZNH, STUF_DEF_1.NAME AS NAMEG, dbo.HEAD_BACK_ANBAR.SHARAYET, dbo.DEPART.DEPART, dbo.INVO_LST.TKHN, dbo.HEAD_BACK_ANBAR.MAS, dbo.HEAD_BACK_ANBAR.HTAG AS TAG, dbo.SALA_DTL.EMZA AS EMZA1, SALA_DTL_1.EMZA AS EMZA2, SALA_DTL_2.EMZA AS EMZA3, dbo.HEAD_BACK_ANBAR.SGN1usid, dbo.HEAD_BACK_ANBAR.sgn2usid, dbo.HEAD_BACK_ANBAR.sgn3usid, dbo.HEAD_LST.SGN1, dbo.HEAD_LST.SGN2, dbo.HEAD_LST.SGN3, dbo.HEAD_LST.SGN4, dbo.STUF_DEF.BARCODE, dbo.CUST_HESAB.tob , dbo.STUF_DEF.sstid,dbo.STUF_DEF.mu , dbo.STUF_DEF.vra
                        FROM dbo.STUF_DEF
                            RIGHT OUTER JOIN dbo.SHIFT
                                             RIGHT OUTER JOIN dbo.DEPART
                                                              RIGHT OUTER JOIN dbo.HEAD_LST
                                                                               INNER JOIN dbo.INVO_LST
                                                                                          INNER JOIN dbo.TCOD_ANBAR ON dbo.INVO_LST.ANBAR=dbo.TCOD_ANBAR.CODE
                                                                                          INNER JOIN dbo.HEAD_BACK_ANBAR ON dbo.INVO_LST.NUMBER=dbo.HEAD_BACK_ANBAR.NUMBER AND dbo.INVO_LST.TAG=dbo.HEAD_BACK_ANBAR.HTAG ON dbo.HEAD_LST.NUMBER=dbo.HEAD_BACK_ANBAR.NUMBER AND dbo.HEAD_LST.TAG=dbo.HEAD_BACK_ANBAR.TAG ON dbo.DEPART.DEPATMAN=dbo.HEAD_BACK_ANBAR.DEPATMAN
                                                              LEFT OUTER JOIN dbo.CUSTKIND ON dbo.HEAD_BACK_ANBAR.CUST_KIND=dbo.CUSTKIND.CUST_COD ON dbo.SHIFT.SHIFT_ID=dbo.HEAD_BACK_ANBAR.SHIFT
                                             LEFT OUTER JOIN dbo.OTHER_DTL ON dbo.HEAD_BACK_ANBAR.NUMBER=dbo.OTHER_DTL.NUMBER AND dbo.HEAD_BACK_ANBAR.HTAG=dbo.OTHER_DTL.TAG
                                             LEFT OUTER JOIN dbo.SALA_DTL AS SALA_DTL_2 ON dbo.HEAD_BACK_ANBAR.sgn3usid=SALA_DTL_2.IDD
                                             LEFT OUTER JOIN dbo.SALA_DTL AS SALA_DTL_1 ON dbo.HEAD_BACK_ANBAR.sgn2usid=SALA_DTL_1.IDD
                                             LEFT OUTER JOIN dbo.SALA_DTL ON dbo.HEAD_BACK_ANBAR.SGN1usid=dbo.SALA_DTL.IDD
                                             LEFT OUTER JOIN dbo.STUF_DEF AS STUF_DEF_1
                                                             INNER JOIN dbo.OTHER_DTL_SUB ON STUF_DEF_1.CODE=dbo.OTHER_DTL_SUB.CODE ON dbo.INVO_LST.CODE=dbo.OTHER_DTL_SUB.CODE AND dbo.INVO_LST.NUMBER=dbo.OTHER_DTL_SUB.NUMBER AND dbo.INVO_LST.TAG=dbo.OTHER_DTL_SUB.TAGG
                                             LEFT OUTER JOIN dbo.CUST_HESAB ON dbo.HEAD_BACK_ANBAR.CUST_NO=dbo.CUST_HESAB.hes
                                             LEFT OUTER JOIN dbo.TCOD_VAHEDS ON dbo.INVO_LST.VAHED_K=dbo.TCOD_VAHEDS.CODE ON dbo.STUF_DEF.CODE=dbo.INVO_LST.CODE) AS DRVD_TBL
                  WHERE NUMBER={number} AND DRVD_TBL.TAG={tag} ");
            }
            else //دنافراز
            {
                sql = ($@"SELECT        dbo.HEAD_LST.NUMBER, dbo.HEAD_LST.TAG, dbo.HEAD_LST.DATE_N, dbo.INVO_LST.MABL, dbo.INVO_LST.MABL_K, dbo.INVO_LST.N_MOIN, dbo.INVO_LST.MABL_K - dbo.INVO_LST.N_MOIN AS mabkbt, 
                                    dbo.INVO_LST.IMBAA, dbo.INVO_LST.MABL_K - dbo.INVO_LST.N_MOIN + dbo.INVO_LST.IMBAA AS mabkn, dbo.CUST_HESAB.MCODEM, dbo.CUST_HESAB.tob, dbo.INVO_LST.CODE, dbo.STUF_DEF.sstid, dbo.STUF_DEF.mu, 
                                    ISNULL(dbo.STUF_DEF.NAME, N' ') + N' ' + ISNULL(dbo.INVO_LST.MANDAH, N' ') AS KALA, dbo.INVO_LST.MEGHk, dbo.STUF_DEF.vra, dbo.CUST_HESAB.ECODE,dbo.INVO_LST.MEGH_MAR
                                    FROM            dbo.HEAD_LST INNER JOIN
                                                             dbo.INVO_LST ON dbo.HEAD_LST.NUMBER = dbo.INVO_LST.NUMBER AND dbo.HEAD_LST.TAG = dbo.INVO_LST.TAG INNER JOIN
                                                             dbo.CUST_HESAB ON dbo.HEAD_LST.CUST_NO = dbo.CUST_HESAB.hes INNER JOIN
                                                             dbo.STUF_DEF ON dbo.INVO_LST.CODE = dbo.STUF_DEF.CODE
                                    WHERE        (dbo.HEAD_LST.NUMBER = {number}) AND (dbo.HEAD_LST.TAG = {tag}) ");
            }

            return _db.DoGetDataSQL<DRV_TBL>(string.Format(sql, number, tag)).ToList();
        }

        private void PersistChunk(List<InvoiceDto> sent, List<List<TAXDTL>> recordsSets, IEnumerable<PacketResponse> responses, int tag)
        {
            // مرتب‌سازی مطابق با ایندکس
            var pairs = sent.Select((dto, idx) => new { dto, records = recordsSets[idx], resp = responses.ElementAt(idx) });

            foreach (var pair in pairs)
            {
                var header = pair.dto.Header;
                var uid = pair.resp.Uid;
                var refNum = pair.resp.ReferenceNumber;
                var status = uid != null ? "PENDING" : "FAILED";

                for (int i = 0; i < pair.dto.Body.Count; i++)
                {
                    var body = pair.dto.Body[i];
                    var record = pair.records[i];   // اینجا ردیف TAXDTL متناظر
                    var idd = _fn.GetNewIDD();

                    var sql = @"
                     INSERT INTO dbo.TAXDTL
                     (
                         Taxid, Indatim_Sec, Indati2m_Sec, Inty, Inno, Inp, Ins, Tins, Tob,
                         Bid, Tinb, Sbc, Bpc, Ft, Crn, Billid, Tprdis, Tdis, Tadis, Tvam,
                         Todam, Tbill, Setm, Cap, Insp, Tvop, Tax17, Cdcd,
                         DATE_N,
                         NUMBER, TAG, Sstid, Sstt, Mu, Am, Fee, Prdis, Dis, Adis, Vra, Vam, Tsstam,
                         UID, RefrenceNumber, TheStatus, ApiTypeSent, SentTaxMemory, IDD, REMARKS
                     )
                     VALUES
                     (
                         @Taxid, @Ind1, @Ind2, @Inty, @Inno, @Inp, @Ins, @Tins, @Tob,
                         @Bid, @Tinb, @Sbc, @Bpc, @Ft, @Crn, @Billid, @Tprdis, @Tdis, @Tadis, @Tvam,
                         @Todam, @Tbill, @Setm, @Cap, @Insp, @Tvop, @Tax17, @Cdcd,
                         @Date_N,
                         @Number, @Tag, @Sstid, @Sstt, @Mu, @Am, @Fee, @Prdis, @Dis, @Adis, @Vra, @Vam, @Tsstam,
                         @UID, @Ref, @Status, @Api, @Mem, @IDD, N'Bulk'
                     )";
                    var p = new Dictionary<string, object>
                    {
                        ["Taxid"] = header.Taxid,
                        ["Ind1"] = header.Indatim,
                        ["Ind2"] = header.Indati2m,
                        ["Inty"] = header.Inty,
                        ["Inno"] = header.Inno,
                        ["Inp"] = header.Inp,
                        ["Ins"] = header.Ins,
                        ["Tins"] = header.Tins,
                        ["Tob"] = header.Tob,
                        ["Bid"] = header.Bid,
                        ["Tinb"] = header.Tinb ?? string.Empty,
                        ["Sbc"] = header.Sbc,
                        ["Bpc"] = header.Bpc,
                        ["Ft"] = header.Ft,
                        ["Crn"] = header.Crn,
                        ["Billid"] = header.Billid,
                        ["Tprdis"] = header.Tprdis,
                        ["Tdis"] = header.Tdis,
                        ["Tadis"] = header.Tadis,
                        ["Tvam"] = header.Tvam,
                        ["Todam"] = header.Todam,
                        ["Tbill"] = header.Tbill,
                        ["Setm"] = header.Setm,
                        ["Cap"] = header.Cap,
                        ["Insp"] = header.Insp,
                        ["Tvop"] = header.Tvop,
                        ["Tax17"] = header.Tax17,
                        ["Cdcd"] = header.Cdcd,
                        ["Date_N"] = record.DATE_N ?? 0,
                        ["Number"] = record?.NUMBER > 0 ? record.NUMBER : _fn.SafeRemoveFirstFour(header.Inno),
                        ["Tag"] = tag,
                        ["Sstid"] = body.Sstid,
                        ["Sstt"] = body.Sstt,
                        ["Mu"] = body.Mu,
                        ["Am"] = body.Am,
                        ["Fee"] = body.Fee,
                        ["Prdis"] = body.Prdis,
                        ["Dis"] = body.Dis,
                        ["Adis"] = body.Adis,
                        ["Vra"] = body.Vra,
                        ["Vam"] = body.Vam,
                        ["Tsstam"] = body.Tsstam,
                        ["UID"] = uid,
                        ["Ref"] = refNum,
                        ["Status"] = status,
                        ["Api"] = _isSandbox ? 0 : 1,
                        ["Mem"] = _memoryId,
                        ["IDD"] = idd
                    };
                    _db.DoExecuteSQL(sql, p);
                }
            }
        }

        #region MyRegion
        private (decimal? Cap, decimal? Insp, string? ErrorMessage) CalculateCapInsp(int setm, decimal tbill, decimal? capInput, int inty)
        {
            decimal capResult = 0;
            decimal inspResult = 0;
            string? errorMessage = null;

            // ---------- اجبار یا اصلاح setm بر اساس inty ----------
            switch (inty)
            {
                case 1: // آزاد
                    // هیچ محدودیتی ندارد؛ بعداً کنترل می‌کنیم که setm یکی از 1..3 باشد
                    break;

                case 2: // فقط نقدی
                case 3: // رسید دستگاه یا درگاه
                    if (setm != 1) //اگر نقد نیست برای نوع دوم یا سوم صورت حساب
                    {
                        //errorMessage = $"برای صورت حساب نوع {inty} فقط روش تسویه‌ی نقدی (setm=1) مجاز است.";
                        //return (null, null, errorMessage);
                    }
                    break;
                default:
                    return (null, null, "کد inty نامعتبر است (باید 1، 2 یا 3 باشد).");
            }

            // مقادیر نمی‌توانند منفی باشند
            if (tbill < 0) tbill = 0;

            switch (setm)
            {
                case 1: // نقدی
                case 5: // ساتنا/پایا
                case 6: // کارتخوان
                    capResult = tbill;
                    inspResult = 0;
                    break;
                case 2: // نسیه
                case 4: // چک (در سامانه مودیان چک معمولا نوعی نسیه با سررسید است)
                    capResult = 0;
                    inspResult = tbill;
                    break;
                case 3: // نقدی/نسیه
                    if (capInput.HasValue)
                    {
                        capResult = Math.Truncate(capInput.Value);
                        if (capResult < 0)
                        {
                            errorMessage = $"مبلغ نقدی ({capResult}) نمی‌تواند منفی باشد.";
                            capResult = 0; // اصلاح به حداقل مجاز
                        }
                        if (capResult > tbill)
                        {
                            // اگر مبلغ نقدی بیش از کل است، کل را نقدی و نسیه را صفر در نظر می‌گیریم
                            // یا می‌توان خطا داد. اینجا اصلاح می‌کنیم:
                            // errorMessage = $"مبلغ نقدی ({capResult}) بیشتر از مبلغ کل صورتحSAP ({tbill}) است.";
                            capResult = tbill;
                        }
                        inspResult = tbill - capResult;
                    }
                    else
                    {
                        // اگر برای نقدی/نسیه، مبلغ نقدی ورودی (capInput) داده نشده باشد.
                        // این حالت باید توسط منطق برنامه مدیریت شود.
                        // ۱. خطا برگردانده شود.
                        // ۲. یک مقدار پیش‌فرض در نظر گرفته شود (مثلا کل مبلغ نقدی).
                        errorMessage = "برای روش تسویه 'نقدی/نسیه'، مبلغ پرداخت نقدی اولیه مشخص نشده است. کل مبلغ، نقدی در نظر گرفته شد.";
                        capResult = tbill; // پیش‌فرض: کل مبلغ نقدی
                        inspResult = 0;
                    }
                    break;
                case 7: // سایر
                        // برای روش "سایر"، معمولا به مقادیر cap و insp که از قبل (مثلا از دیتابیس) آمده‌اند اتکا می‌شود.
                        // capInput در این حالت می‌تواند cap خوانده شده از دیتابیس باشد.
                    if (capInput.HasValue)
                    {
                        capResult = Math.Truncate(capInput.Value);
                        if (capResult < 0) capResult = 0;
                        if (capResult > tbill) capResult = tbill; // اصلاح اگر بیش از حد باشد
                        inspResult = tbill - capResult;
                    }
                    else // اگر هیچ ورودی برای cap نیست، پیش‌فرض نقدی
                    {
                        capResult = tbill;
                        inspResult = 0;
                    }
                    break;
                default:
                    errorMessage = $"روش تسویه با کد {setm} تعریف نشده یا نامعتبر است.";
                    // در صورت خطای روش تسویه، می‌توان مقادیر را صفر یا tbill را به صورت نقدی برگرداند.
                    // فعلا خطا برمی‌گردانیم تا در UI مشخص شود.
                    return (null, null, errorMessage);
            }

            return (Math.Truncate(capResult), Math.Truncate(inspResult), errorMessage);
        }

        #endregion

    }

    public class BulkSendResult
    {
        public int Success { get; set; }
        public Dictionary<long, string> Failures { get; } = new();
    }
}
