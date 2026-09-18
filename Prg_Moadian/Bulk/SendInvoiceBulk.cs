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
        public Func<string, bool>? OnValidationWarning { get; set; }

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

            // Fix sandbox tax url if it comes in without /req/api/
            if (taxUrl.Equals("https://sandboxrc.tax.gov.ir", StringComparison.OrdinalIgnoreCase))
            {
                taxUrl = "https://sandboxrc.tax.gov.ir/req/api/";
            }

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
        /// <param name="sendOptionalItemName">ارسال نام اختیاری کالا/خدمت در فیلد sstt</param>
        public async Task<BulkSendResult> SendAsync(IEnumerable<long> invoiceNumbers, int tag, int? inty_value = default, int? setm_value = default,
    IProgress<int>? progress = null, bool useCustomDate = false, string customDateText = null, bool sendOptionalItemName = true)
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
                    var (dto, records) = BuildDtoAndRecords(number, tag, inty_value, setm_value, useCustomDate, customDateText, sendOptionalItemName);
                    allDtos.Add(dto);
                    allRecords[dto] = records;
                }
                catch (InvoiceValidationException ex)
                {
                    result.Failures[ex.InvoiceNumber] = ex.Message;
                }
                catch (Exception ex)
                {
                    var friendly = cer.ExpecMsgEr(ex) ?? $"خطای داخلی در پردازش فاکتور {number} . لطفاً بعداً تلاش کنید.";
                    result.Failures[number] = friendly;
                }
                finally
                {
                    // این را در finally گذاشتیم تا حتی اگر فاکتوری خطا داد، 
                    // نوار پیشرفت (Progress) آپدیت شود و روی صفحه گیر نکند
                    buildProgress++;
                    double buildPercent = (double)buildProgress / totalNumbers;
                    progress?.Report((int)(buildPercent * 50));
                }
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
                    progress?.Report(0);

                    // ۱. متغیرهایی برای نگهداری وضعیت پاسخ سرور
                    List<PacketResponse> responsesToSave = new List<PacketResponse>();
                    string batchErrorMessage = null;

                    // آیا سرنوشت بسته نامعلوم است؟
                    // اگر درخواست رفته باشد ولی پاسخ نرسیده باشد، ممکن است سامانه آن را
                    // ثبت کرده باشد. در این حالت نباید FAILED ثبت کرد و نباید بدون
                    // استعلام دوباره فرستاد — FAQ ص۴۰ س۱۱-۲۶ می‌گوید ارسال مجدد با
                    // شماره مالیاتی جدید باعث سند تکراری در کارپوشه می‌شود.
                    bool outcomeUnknown = false;

                    try
                    {
                        // ❷ فراخوانی وب‌سرویس
                        var response = await Task.Run(() => TaxApiService.Instance.TaxApis.SendInvoices(batch, null)).ConfigureAwait(false);

                        // بررسی قطعی بودن پاسخ سرور
                        if (response == null || response.Body == null)
                        {
                            batchErrorMessage = $"پاسخ نامعتبر از سرور (کد وضعیت: {response?.Status}). احتمالاً سامانه دچار تایم‌اوت شده است.";
                            outcomeUnknown = true; // سرنوشت نامعلوم است
                        }
                        else if (response.Body.Errors != null && response.Body.Errors.Any())
                        {
                            // اگر Detail نال بود، ErrorCode را نشان بده
                            batchErrorMessage = string.Join(" | ", response.Body.Errors.Select(e => !string.IsNullOrEmpty(e.Detail) ? e.Detail : e.ErrorCode));
                        }
                        else if (response.Body.Result == null || !response.Body.Result.Any())
                        {
                            batchErrorMessage = "ارسال انجام شد اما سرور مودیان هیچ نتیجه‌ای (Reference Number) برنگرداند.";
                            outcomeUnknown = true; // درخواست رفته ولی نتیجه‌ای نداریم
                        }
                        else
                        {
                            // اگر هیچ خطایی نبود، لیست پاسخ‌های واقعی را بگیرید
                            responsesToSave = response.Body.Result.ToList();
                        }
                    }
                    catch (Exception reqEx)
                    {
                        // اگر اینترنت قطع بود یا DNS مشکل داشت
                        batchErrorMessage = cer.ExpecMsgEr(reqEx) ?? "قطعی ارتباط یا خطای شبکه‌ای در اتصال به سرور مودیان.";

                        // نمی‌دانیم درخواست به سامانه رسیده یا نه — نباید FAILED ثبت شود.
                        outcomeUnknown = true;
                    }

                    // ---------------------------------------------------------------------
                    // ❸ ثبت در دیتابیس (تحت هر شرایطی باید اجرا شود)
                    // ---------------------------------------------------------------------

                    // اگر خطایی رخ داده بود، یک لیست فیک می‌سازیم تا متد PersistChunk کرش نکند 
                    if (batchErrorMessage != null)
                    {
                        foreach (var dto in batch)
                        {
                            responsesToSave.Add(new PacketResponse(null, null, "SYS_ERR", batchErrorMessage));
                        }
                    }

                    // گرفتن رکوردهای دیتابیسی مربوط به این بچ
                    var batchRecords = batch.Select(dto => allRecords[dto]).ToList();

                    // ذخیره در دیتابیس!
                    PersistChunk(batch, batchRecords, responsesToSave, tag, outcomeUnknown);

                    // ---------------------------------------------------------------------
                    // ❹ گزارش‌دهی به کاربر
                    // ---------------------------------------------------------------------

                    if (batchErrorMessage == null)
                    {
                        // کاملاً موفق بود
                        result.Success += responsesToSave.Count;
                        progress?.Report(responsesToSave.Count);
                    }
                    else
                    {
                        // ثبت خطاها برای نمایش به کاربر (با استخراج کاملاً امنِ شماره فاکتور)
                        foreach (var dto in batch)
                        {
                            long num = (long)allRecords[dto].First().NUMBER!;
                            result.Failures[num] = batchErrorMessage;
                        }
                    }
                }
                catch (Exception ex)
                {
                    var friendly = cer.ExpecMsgEr(ex) ?? "خطا در اتصال به سرور مودیان برای ارسال گروهی.";
                    // اگر ارسال یک بسته به کل شکست خورد، برای همه‌ی DTOهای آن بسته خطا بزن
                    foreach (var dto in batch)
                    {
                        long num = (long)allRecords[dto].First().NUMBER!;
                        result.Failures[num] = friendly;
                    }
                }
            }

            return result;
        }

        private (InvoiceDto Dto, List<TAXDTL> Records) BuildDtoAndRecords(long number, int tag, int? Inty_Value = default, int? Setm_Value = default, bool useCustomDate = false, string customDateText = null, bool sendOptionalItemName = true)
        {
            // 1. بارگذاری HEAD_LST_EXTENDED
            var headExt = _db.DoGetDataSQL<HEAD_LST_EXTENDED>($"SELECT * FROM dbo.HEAD_LST_EXTENDED WHERE NUMBER={number} AND TGU={tag}").FirstOrDefault();
            if (headExt == null)
            {
                // ✅ ساخت خودکار رکورد پیش‌فرض برای سربرگ مودیان
                // اگر نوع صورت حساب یا روش تسویه از UI ارسال شده، از اونها استفاده می‌کنیم
                // در غیر این صورت از مقادیر پیش‌فرض استفاده می‌شود
                int defaultInty = Inty_Value ?? 1;  // پیش‌فرض: نوع اول
                int defaultSetm = Setm_Value ?? 1;  // پیش‌فرض: نقدی

                try
                {
                    _db.DoExecuteSQL($@"
                        INSERT INTO dbo.HEAD_LST_EXTENDED (NUMBER, TGU, inty, inp, ins, setm, todam, cut)
                        VALUES ({number}, {tag}, {defaultInty}, 1, 1, {defaultSetm}, 0, '2');");
                }
                catch (Exception insertEx)
                {
                    // PK violation = race condition (همزمان دو بار برای همین فاکتور) — رکورد قبلاً ساخته شده، مشکلی نیست
                    // بقیه خطاها = مشکل واقعی که باید لاگ شود
                    bool isPkViolation = insertEx.Message.Contains("PRIMARY KEY") || insertEx.Message.Contains("duplicate key") || insertEx.Message.Contains("UNIQUE");
                    if (!isPkViolation)
                        Generaly.CL_Generaly.DoGetwriteAppenLog($"HEAD_LST_EXTENDED INSERT failed for invoice {number} tag {tag}: {insertEx.Message}");
                }

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
            {
                var idMsg = $"فاکتور {number}: شناسه کالا/خدمت (sstid) یا واحد اندازه‌گیری (mu) برای بعضی ردیف‌ها خالی است.";
                if (OnValidationWarning == null || !OnValidationWarning($"{idMsg}\n\nآیا با این وجود ادامه می‌دهید؟"))
                {
                    RecordFailedInvoiceLocal(number, tag, idMsg);
                    throw new InvoiceValidationException(number, idMsg);
                }
            }

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
                // نکته: اینجا دیگر روی خالی بودن ECODE بی‌قید و شرط استثنا پرتاب نمی‌شود.
                // جدول ۱۱ ص۳۴ ردیف ۴ برای خریدار حقیقی/اتباع مسیر جایگزین
                // «شماره ملی/کد فراگیر + کد پستی» را مجاز می‌داند؛ پرتاب زودهنگام
                // آن مسیر را غیرقابل دسترس می‌کرد. تصمیم به ValidateBuyer واگذار شد.
                int tobValue = lines.First().tob ?? 2;

                ECODE_M = string.IsNullOrWhiteSpace(srcEcode) ? null : srcEcode.Trim();
                // Bid فقط وقتی فرستاده می‌شود که شماره اقتصادی نداریم و باید از مسیر
                // جایگزین «شماره ملی + کد پستی» استفاده کنیم. تا امروز این فیلد هرگز
                // ارسال نشده و ۱۴۳۹۹ صورتحساب با آن خالی پذیرفته شده‌اند؛ پس روی مسیری
                // که کار می‌کند فیلد جدید اضافه نمی‌کنیم.
                CODEMELI_M = string.IsNullOrWhiteSpace(ECODE_M)
                    ? MoadianRules.SanitizeBid(tobValue, lines.First().MCODEM)
                    : null;

                // کنترل مشترک طبق جدول ۱۱ ص۳۳-۳۴. سامانه این فیلد را با regex دقیق
                // کنترل می‌کند (^\d{14}$ برای حقیقی/اتباع و ^\d{11}$ برای حقوقی/مشارکت)،
                // پس کنترل «فقط بیشتر نباشد» کافی نبود و همان علت خطای 0101204 بود.
                // با همان الگویی اعتبارسنجی می‌کنیم که واقعا ارسال می‌شود (پایین‌تر Inp = 1 ثابت است).
                // اگر با headExt.inp اعتبارسنجی کنیم، یک فاکتور صادراتی از کنترل خریدار معاف
                // می‌شود ولی به سامانه به عنوان الگوی ۱ اعلام می‌گردد که خریدار می‌خواهد.
                if (!MoadianRules.ValidateBuyer(headExt.inty ?? 1, 1, tobValue, ECODE_M, CODEMELI_M, headExt.bpc, out var buyerError))
                {
                    string errMsg = $"فاکتور {number}: {buyerError}";

                    if (OnValidationWarning != null && OnValidationWarning($"{errMsg}\n\nآیا با این وجود ادامه می‌دهید؟"))
                    {
                        // کاربر آگاهانه ادامه داد
                    }
                    else
                    {
                        RecordFailedInvoiceLocal(number, tag, errMsg);
                        throw new InvoiceValidationException(number, errMsg);
                    }
                }
            }


            // 6. FLOATFIXER: گردکردن مقادیر
            bool isReturn = headExt.ins == 4;
            foreach (var ln in lines)
            {
                // توجه: گردکردن مبلغ واحد و تعداد عمداً *دست‌نخورده* باقی مانده است.
                // تغییر آن مبالغ ارسالی حدود ۳۰٪ ردیف‌ها را جابه‌جا می‌کند و تصمیم
                // کسب‌وکاری است، نه فنی. جزئیات در گزارش بررسی آمده است.
                ln.MABL = Math.Truncate(ln.MABL ?? 0);
                ln.N_MOIN = Math.Truncate(ln.N_MOIN ?? 0);

                // جایزه / تخفیف ۱۰۰٪ : فی باید مثبت بماند (جدول ۳۴ ص۵۳) و کل مبلغ
                // به صورت تخفیف ثبت شود (جدول ۴۱ ص۵۹ اجازه dis = prdis می‌دهد).
                bool isGift = (ln.N_KOL == 100 || ln.JAY > 0);
                if (isGift)
                {
                    ln.MABL = 1;
                }

                ln.MEGHk = Math.Round(ln.MEGHk ?? 0, 4);
                if (isReturn)
                {
                    ln.MEGH_MAR = Math.Round(ln.MEGH_MAR ?? 0, 4);
                    ln.MEGHk -= ln.MEGH_MAR;
                }

                ln.MABL_K = Math.Truncate((ln.MABL ?? 0) * (ln.MEGHk ?? 0));

                if (isGift)
                {
                    ln.N_MOIN = ln.MABL_K;
                }

                // کنترل مقادیر صفر — *بعد* از بازمحاسبه MABL_K انجام می‌شود.
                // قبلاً این کنترل بالاتر بود و مقدار کهنهٔ دیتابیس را می‌خواند.
                //   جدول ۳۱ ص۵۱ : am    > 0   (خطای 0103605)
                //   جدول ۳۴ ص۵۳ : fee   > 0   (خطای 0103705)
                //   جدول ۴۰ ص۵۸ : prdis > 0
                // در برگشت از فروش، ردیفی که کامل مرجوع شده مقدارش صفر می‌شود؛ چنین
                // ردیفی خطا نیست و پایین‌تر از فهرست حذف می‌گردد.
                string zeroProblem = null;
                if (isReturn && (ln.MEGHk ?? 0) <= 0)
                    zeroProblem = null;
                else if ((ln.MEGHk ?? 0) <= 0)
                    zeroProblem = $"تعداد/مقدار کالا/خدمت '{ln.KALA}' ({ln.MEGHk}) باید بزرگتر از صفر باشد";
                else if ((ln.MABL ?? 0) <= 0)
                    zeroProblem = $"مبلغ واحد کالا/خدمت '{ln.KALA}' ({ln.MABL}) باید بزرگتر از صفر باشد";
                else if ((ln.MABL_K ?? 0) <= 0)
                    zeroProblem = $"مبلغ قبل از تخفیف کالا/خدمت '{ln.KALA}' ({ln.MABL_K}) باید بزرگتر از صفر باشد";

                if (zeroProblem != null)
                {
                    if (OnValidationWarning != null)
                    {
                        if (!OnValidationWarning($"{zeroProblem}. آیا مایل به ادامه ارسال هستید؟"))
                        {
                            string errMsg = $"ارسال فاکتور {number} به دلیل انصراف کاربر در هشدار قیمت صفر لغو شد.";
                            RecordFailedInvoiceLocal(number, tag, errMsg);
                            throw new InvoiceValidationException(number, errMsg);
                        }
                    }
                    else
                    {
                        string errMsg = $"فاکتور {number}: {zeroProblem}.";
                        RecordFailedInvoiceLocal(number, tag, errMsg);
                        throw new InvoiceValidationException(number, errMsg);
                    }
                }

                // جدول ۴۱ ص۵۹: تخفیف نباید از مبلغ قبل از تخفیف بیشتر باشد.
                // مبلغ را بی‌صدا تغییر نمی‌دهیم — فقط اطلاع می‌دهیم و تصمیم با کاربر است.
                //
                // در برگشت از فروش، ردیفِ کامل‌مرجوع مقدارش صفر می‌شود و پایین‌تر حذف
                // می‌گردد؛ تخفیف کهنه‌اش نباید هشدار بی‌مورد بسازد.
                if (!(isReturn) && (ln.N_MOIN ?? 0) > (ln.MABL_K ?? 0))
                {
                    var disMsg = $"فاکتور {number}: تخفیف کالا/خدمت '{ln.KALA}' ({ln.N_MOIN}) از مبلغ قبل از تخفیف ({ln.MABL_K}) بیشتر است.";
                    if (OnValidationWarning == null || !OnValidationWarning($"{disMsg}\n\nآیا با این وجود ادامه می‌دهید؟"))
                    {
                        RecordFailedInvoiceLocal(number, tag, disMsg);
                        throw new InvoiceValidationException(number, disMsg);
                    }
                }

                ln.mabkbt = (ln.MABL_K ?? 0) - (ln.N_MOIN ?? 0);
                if ((ln.mabkbt ?? 0) > 0 && (ln.vra ?? 0) > 0 && (ln.IMBAA ?? 0) <= 0)
                {
                    var vatMsg = $"فاکتور {number}: کالا/خدمت '{ln.KALA}' نرخ مالیات {ln.vra}٪ دارد ولی مبلغ مالیاتش صفر است.";
                    if (OnValidationWarning == null || !OnValidationWarning($"{vatMsg}\n\nآیا با این وجود ادامه می‌دهید؟"))
                    {
                        RecordFailedInvoiceLocal(number, tag, vatMsg);
                        throw new InvoiceValidationException(number, vatMsg);
                    }
                }
                if ((ln.IMBAA ?? 0) > 0)
                {
                    ln.IMBAA = Math.Truncate((decimal)(ln.mabkbt * (ln.vra ?? 0) / 100));
                }
                else if ((ln.mabkbt ?? 0) <= 0)
                {
                    ln.IMBAA = 0;
                }
                ln.mabkn = (ln.mabkbt ?? 0) + (ln.IMBAA ?? 0);
            }

            // در برگشت از فروش، ردیف‌هایی که کامل مرجوع شده‌اند (مقدار صفر) نباید در
            // بدنه بمانند — جدول ۳۱ ص۵۱ تعداد را بزرگتر از صفر می‌خواهد.
            // مسیر ارسال تکی این کار را انجام می‌داد ولی مسیر گروهی نه.
            if (isReturn)
            {
                lines = lines.Where(l => (l.MEGHk ?? 0) > 0).ToList();

                if (!lines.Any())
                {
                    string errMsg = $"فاکتور {number}: پس از کسر اقلام مرجوعی هیچ ردیفی باقی نمانده است؛ معمولا برای برگشت کامل باید صورتحساب ابطالی صادر شود.";
                    if (OnValidationWarning == null || !OnValidationWarning($"{errMsg}\n\nآیا با این وجود ادامه می‌دهید؟"))
                    {
                        RecordFailedInvoiceLocal(number, tag, errMsg);
                        throw new InvoiceValidationException(number, errMsg);
                    }
                }
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

                decimal Todam_sum = headExt.todam ?? 0;

                // مقادیر دیتابیس را قبل از فراخوانی نگه دار: در صورت خطا، تخصیص تاپل
                // آن‌ها را با null بازنویسی می‌کند و دیگر قابل بازیابی نیستند.
                var capBefore = headExt.cap;
                var inspBefore = headExt.insp;

                (headExt.cap, headExt.insp, string? capInspError) = CalculateCapInsp(
                    (int)headExt.setm, Tbill_sum, capForCalculation, (int)Inty_Value, Tvam_sum, Todam_sum);

                if (capInspError != null)
                {
                    // هشدار، نه سد. قواعد تسویه ممکن است تغییر کنند یا سامانه رفتار
                    // دیگری داشته باشد؛ تصمیم با کاربر است.
                    var capMsg = $"فاکتور {number}: {capInspError}";
                    if (OnValidationWarning == null || !OnValidationWarning($"{capMsg}\n\nآیا با این وجود ادامه می‌دهید؟"))
                    {
                        RecordFailedInvoiceLocal(number, tag, capMsg);
                        throw new InvoiceValidationException(number, capMsg);
                    }

                    // کاربر ادامه داد: مقادیر اصلی دیتابیس برگردانده می‌شوند، نه صفر.
                    headExt.cap = capBefore;
                    headExt.insp = inspBefore;
                }

                // بررسی نهایی: مبنای مقایسه بسته به روش تسویه فرق می‌کند.
                //   setm=3 → cap + insp = tbill − tvam − todam   (جدول ۲۵ ص۴۶ و FAQ ص۳۱)
                //   setm=1/2 → سند قاعده‌ای ندارد و سامانه مبلغ کل را می‌پذیرد.
                decimal expectedSum = ((int)headExt.setm == 3)
                    ? MoadianRules.SettlementBase(Tbill_sum, Tvam_sum, Todam_sum)
                    : Tbill_sum;

                if (Math.Abs((headExt.cap ?? 0) + (headExt.insp ?? 0) - expectedSum) > 0.01m) // تلرانس برای مقایسه decimal
                {
                    var buildResult = $"مجموع مبلغ نقدی ({headExt.cap ?? 0}) و نسیه ({headExt.insp ?? 0}) با مبنای تسویه ({expectedSum}) برای فاکتور {number} همخوانی ندارد.";
                    if (OnValidationWarning == null || !OnValidationWarning($"{buildResult}\n\nآیا با این وجود ادامه می‌دهید؟"))
                    {
                        RecordFailedInvoiceLocal(number, tag, buildResult);
                        throw new InvoiceValidationException(number, buildResult);
                    }
                }
            }

            // 7. کد شعبه: سامانه با ^\d{4}$ کنترل می‌کند (ص۳۳) و همین علت خطای 0101504 بود.
            //    مقدار نامعتبر به جای ارسالِ ناقص، حذف می‌شود (این فیلد اختیاری است).
            headExt.bbc = MoadianRules.NormalizeBranchCode(headExt.bbc);
            headExt.sbc = MoadianRules.NormalizeBranchCode(headExt.sbc);

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

            // تاریخ فاکتور تاریخچه‌ای است و نباید اختلاف ساعت سرور به آن اعمال شود
            // ServerClockSkew فقط برای عملیات زمان‌واقعی (مثل ابطالی/اصلاحی) کاربرد دارد
            var ts = TaxService.ConvertDateToLong(dt);

            // =================================================================
            // ❶ محاسبه زمان "همین الان" بر اساس ساعت دقیق و سینک‌شده‌ی سرور دارایی
            // =================================================================
            var iranTZ = TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time");

            // فرض بر این است که متد/کلاس TimeSync شما TimeOffset را برمی‌گرداند
            // (یا اگر در متغیر دیگری مثل TokenLifeTime.ServerClockSkew ذخیره کردید، از آن استفاده کنید)
            var nowUtcOffset = DateTimeOffset.UtcNow.Add(TokenLifeTime.ServerClockSkew); // یا TimeSync.TimeOffset
            var serverNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtcOffset.UtcDateTime, iranTZ);


            // =================================================================
            // ❷ محاسبه "تاریخ صدور" (Indatim) - بر اساس تاریخ سفارشی یا تاریخ واقعی
            // =================================================================
            DateTime issueDate;
            if (useCustomDate && !string.IsNullOrWhiteSpace(customDateText))
            {
                try
                {
                    // تبدیل تاریخ شمسی سفارشی به میلادی (کاربر می‌خواهد فاکتور قدیمی را جدید جا بزند)
                    var customDateInt = int.Parse(customDateText.Replace("/", ""));
                    issueDate = _fn.GetGregorianDateTime(customDateInt.ToString());
                }
                catch
                {
                    // در صورت خطای تایپی کاربر در تاریخ سفارشی، بازگشت به تاریخ اصلی فاکتور
                    issueDate = _fn.GetGregorianDateTime(lines.First().DATE_N.ToString());
                }
            }
            else
            {
                // استفاده از تاریخ واقعی خود فاکتور از دیتابیس
                issueDate = _fn.GetGregorianDateTime(lines.First().DATE_N.ToString());
            }


            // =================================================================
            // ❸ تولید TaxId و تبدیل تاریخ‌ها به فرمت Unix Timestamp
            // =================================================================

            // 1. TaxId باید حتماً بر اساس تاریخ صدور (issueDate) ساخته شود
            var taxId = _taxService.RequestTaxId(_memoryId, issueDate);

            // 2. زمان صدور معامله (برای گذشته یا امروز)
            var indatim_Timestamp = TaxService.ConvertDateToLong(issueDate);

            // 3. قاعده ارسال (ماده ۹) — جدول ۸۶ ص۹۳
            int? insr = MoadianRules.ResolveInsr(issueDate, serverNow);

            // 4. تاریخ و زمان ثبت صورتحساب (Indati2m)
            //
            // ص۲۸ ردیف ۷: فاصله «تاریخ ثبت صورتحساب» تا ارسال نباید از مهلت مجاز بیشتر
            // باشد. اگر سند موضوع ماده ۹ باشد و Indati2m هم روی تاریخ صدورِ گذشته
            // بماند، همین قاعده نقض می‌شود و Insr به‌تنهایی سند را معتبر نمی‌کند.
            long indati2m_Timestamp = TaxService.ConvertDateToLong(serverNow); //default
            if (useCustomDate && insr != 1)
            {
                indati2m_Timestamp = TaxService.ConvertDateToLong(issueDate);
            }

            // 4. تولید شماره سریال داخلی (Inno)
            long invoiceNum = long.Parse(number.ToString());
            string finalInno = _fn.GenerateFixedLengthInno(_sazman.YEA.ToString(), invoiceNum);

            // =================================================================
            // ❹ آماده‌سازی Header
            // =================================================================
            var header = new InvoiceHeaderDto
            {
                Taxid = taxId,
                Indatim = indatim_Timestamp,     // زمان صدور (ترفند تاریخ سفارشی در اینجا اعمال می‌شود)
                Indati2m = indati2m_Timestamp,   // زمان ساخت فایل (زمان دقیق سرور دارایی)
                Inty = headExt.inty ?? 1,
                Inno = finalInno, //// _fn.InnoAddZeroes($"{_sazman.YEA}00{number}")
                Irtaxid = null,
                Inp = /*headExt.inp ??*/ 1,
                Ins = /*headExt.ins ??*/ 1,
                // قاعده ارسال (ماده ۹) — جدول ۸۶ ص۹۳.
                // داخل مهلت مجاز null می‌ماند (خارج از الگو)، خارج از مهلت مقدار ۱ می‌گیرد.
                Insr = insr,
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

                //Insr = null, // قاعده ارسال صورتحساب (اگر ندارید Null بفرستید)
                //Nti1 = null, // یادداشت 1
                //Nti2 = null  // یادداشت 2

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

            var list_extendedUnits = _db.DoGetDataSQL<TCOD_VAHED_EXTENDED>("SELECT IDD,NAME_MO FROM dbo.TCOD_VAHED_EXTENDED").ToList();
            foreach (var l in lines)
            {
                l.mu = CL_MOADIAN.TheFunctions.GetMoadianUnitByName(l.VNAMES, list_extendedUnits, l.mu);
            }

            // آماده‌سازی Body
            var bodies = lines.Select(l => new InvoiceBodyDto
            {

                Sstid = l.sstid, //شناسه کالا/خدمت //CODE	STUF_DEF
                Sstt = sendOptionalItemName ? l.KALA : null, //شرح اختیاری کالا/خدمت //NAME	STUF_DEF
                Mu = l.mu, //واحد اندازه گیری //VNAMES	TCOD_VAHEDS
                Am = l.MEGHk ?? 0, //تعداد/مقدار //MEGH	INVO_LST
                Fee = l.MABL ?? 0, //مبلغ واحد //MABL	INVO_LST
                Prdis = l.MABL_K ?? 0, //مبلغ قبل از تخفیف //MABL_K	INVO_LST
                Dis = l.N_MOIN ?? 0, //مبلغ تخفیف //N_MOIN	INVO_LST
                Adis = l.mabkbt ?? 0, //مبلغ بعد از تخفیف //Sum(INVO_LST.MABL_K - INVO_LST.N_MOIN AS mabkbt)	INVO_LST
                Vra = l.vra ?? 0, //نرخ مالیات بر ارزش افزوده
                Vam = l.IMBAA ?? 0, //مبلغ مالیات بر ارزش افزوده //IMBAA	 INVO_LST
                Tsstam = l.mabkn ?? 0, //مبلغ کل کالا/خدمت //MABL_K	INVO_LST

                //// مقادیر پیش‌فرض برای فیلدهای جدید بدنه در V7.8 (برای الگوهای خاص)
                //Cfee = 0,
                //Nw = 0,
                //Ssrv = 0,
                //Sscv = 0

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
                Indatim_Sec = header.Indatim,     // زمان صدور
                Indati2m_Sec = header.Indati2m,   // زمان ایجاد (همین الان)
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
                sql = ($@"SELECT DATE_N, NUMBER1, HESAB, ADDRESS, TEL, CODE, ECODE, MCODEM, PCODE, IYALAT, CITY, KALA, MEGH, MABL, MABL_K, VNAMES, N_MOIN, mabkbt, IMBAA, mabkn, BARCODE, MEGHk, MOLAH, DEPART, DEPNAME, NUMBER,tob,DRVD_TBL.sstid,DRVD_TBL.mu,DRVD_TBL.vra, MEGH_MAR, N_KOL, JAY
                  FROM(SELECT dbo.HEAD_BACK_ANBAR.NUMBER1, dbo.HEAD_BACK_ANBAR.NUMBER, dbo.HEAD_BACK_ANBAR.DATE_N, dbo.INVO_LST.NUMBER AS INUMBER, dbo.HEAD_BACK_ANBAR.HTAG, dbo.INVO_LST.ANBAR, dbo.INVO_LST.RADIF, dbo.INVO_LST.CODE, dbo.INVO_LST.MEGH, dbo.INVO_LST.MEGHk, dbo.INVO_LST.MEGH_MAR, dbo.INVO_LST.MANDAH, dbo.INVO_LST.MABL, dbo.INVO_LST.MABL_K, dbo.INVO_LST.FROM_A, dbo.INVO_LST.N_RASID, dbo.INVO_LST.MEGH_R, dbo.INVO_LST.RADAH, dbo.INVO_LST.SANAD_NO, dbo.INVO_LST.ANBARF, dbo.INVO_LST.VAHED_K, dbo.STUF_DEF.NAME, dbo.TCOD_ANBAR.NAMES, dbo.TCOD_VAHEDS.NAMES AS VNAMES, dbo.HEAD_BACK_ANBAR.TAH, dbo.HEAD_BACK_ANBAR.MOLAH, dbo.CUSTKIND.CUSTKNAME, dbo.DEPART.DEPNAME, dbo.SHIFT.SHNAME, dbo.CUST_HESAB.NAME AS HESAB, dbo.CUST_HESAB.ADDRESS, dbo.CUST_HESAB.TEL, ISNULL(dbo.STUF_DEF.NAME, N' ')+N' '+ISNULL(dbo.INVO_LST.MANDAH, N' ') AS KALA, dbo.HEAD_BACK_ANBAR.CUST_NO, dbo.INVO_LST.N_KOL, dbo.INVO_LST.N_MOIN, dbo.HEAD_BACK_ANBAR.FNUMCO, dbo.CUST_HESAB.ECODE, dbo.CUST_HESAB.PCODE, dbo.CUST_HESAB.IYALAT, dbo.CUST_HESAB.MCODEM, dbo.CUST_HESAB.CITY, dbo.INVO_LST.MABL_K-dbo.INVO_LST.N_MOIN AS mabkbt, dbo.INVO_LST.IMBAA, dbo.INVO_LST.MABL_K-dbo.INVO_LST.N_MOIN+dbo.INVO_LST.IMBAA AS mabkn, dbo.CUST_HESAB.CODE_E, dbo.HEAD_BACK_ANBAR.TAKHFIF, dbo.HEAD_BACK_ANBAR.MBAA, dbo.STUF_DEF.N_FANI, dbo.HEAD_BACK_ANBAR.SADER, dbo.HEAD_BACK_ANBAR.ANBARF AS ANBARFF, dbo.OTHER_DTL.REQUEST_NO, dbo.OTHER_DTL.BARNAMEH, dbo.OTHER_DTL.DRIVER, dbo.OTHER_DTL.DRIVER_MOB, dbo.OTHER_DTL.CAMIUN_NUM, dbo.OTHER_DTL.MAGHSAD, dbo.OTHER_DTL.CAM_KHALY, dbo.OTHER_DTL.CAM_POOR, dbo.OTHER_DTL.TOZIH, dbo.OTHER_DTL.CAMIUN, dbo.OTHER_DTL_SUB.CODE AS CODEG, dbo.OTHER_DTL_SUB.CAM_KHALY AS CAM_KHALYG, dbo.OTHER_DTL_SUB.CAM_POOR AS CAM_POORG, dbo.OTHER_DTL_SUB.MEGHk AS MEGHkG, dbo.OTHER_DTL_SUB.TOZIH AS TOZIHG, dbo.OTHER_DTL_SUB.VAZNH, STUF_DEF_1.NAME AS NAMEG, dbo.HEAD_BACK_ANBAR.SHARAYET, dbo.DEPART.DEPART, dbo.INVO_LST.TKHN, dbo.INVO_LST.JAY, dbo.HEAD_BACK_ANBAR.MAS, dbo.HEAD_BACK_ANBAR.HTAG AS TAG, dbo.SALA_DTL.EMZA AS EMZA1, SALA_DTL_1.EMZA AS EMZA2, SALA_DTL_2.EMZA AS EMZA3, dbo.HEAD_BACK_ANBAR.SGN1usid, dbo.HEAD_BACK_ANBAR.sgn2usid, dbo.HEAD_BACK_ANBAR.sgn3usid, dbo.HEAD_LST.SGN1, dbo.HEAD_LST.SGN2, dbo.HEAD_LST.SGN3, dbo.HEAD_LST.SGN4, dbo.STUF_DEF.BARCODE, dbo.CUST_HESAB.tob , dbo.STUF_DEF.sstid,dbo.STUF_DEF.mu , dbo.STUF_DEF.vra
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
                #region MAYBE_LATER
                //این کد کامنت شده رو بعدا اگر سر واحد توی دنافراز مشکل داشت , بیا جایگزین بکن
                //چون میاد روی عنوان متنی نام کالا رو میاره و اجازه میده جستجو فازی روش انجام بشه تا معادل واحد مودیانش پیدا بشه , برای کالا هایی که چندین واحد دارند
                {
                    //sql = ($@"SELECT        dbo.HEAD_LST.NUMBER, dbo.HEAD_LST.TAG, dbo.HEAD_LST.DATE_N, dbo.INVO_LST.MABL, dbo.INVO_LST.MABL_K, dbo.INVO_LST.N_MOIN, dbo.INVO_LST.MABL_K - dbo.INVO_LST.N_MOIN AS mabkbt, 
                    //                dbo.INVO_LST.IMBAA, dbo.INVO_LST.MABL_K - dbo.INVO_LST.N_MOIN + dbo.INVO_LST.IMBAA AS mabkn, dbo.CUST_HESAB.MCODEM, dbo.CUST_HESAB.tob, dbo.INVO_LST.CODE, dbo.STUF_DEF.sstid, dbo.STUF_DEF.mu, 
                    //                dbo.TCOD_VAHEDS.NAMES AS VNAMES, ISNULL(dbo.STUF_DEF.NAME, N' ') + N' ' + ISNULL(dbo.INVO_LST.MANDAH, N' ') AS KALA, dbo.INVO_LST.MEGHk, dbo.STUF_DEF.vra, dbo.CUST_HESAB.ECODE,dbo.INVO_LST.MEGH_MAR, dbo.INVO_LST.N_KOL, dbo.INVO_LST.JAY
                    //                FROM            dbo.HEAD_LST INNER JOIN
                    //                                         dbo.INVO_LST ON dbo.HEAD_LST.NUMBER = dbo.INVO_LST.NUMBER AND dbo.HEAD_LST.TAG = dbo.INVO_LST.TAG INNER JOIN
                    //                                         dbo.CUST_HESAB ON dbo.HEAD_LST.CUST_NO = dbo.CUST_HESAB.hes INNER JOIN
                    //                                         dbo.STUF_DEF ON dbo.INVO_LST.CODE = dbo.STUF_DEF.CODE LEFT OUTER JOIN
                    //                                         dbo.TCOD_VAHEDS ON dbo.INVO_LST.VAHED_K = dbo.TCOD_VAHEDS.CODE
                    //                WHERE        (dbo.HEAD_LST.NUMBER = {number}) AND (dbo.HEAD_LST.TAG = {tag}) ");
                }
                #endregion

                sql = ($@"SELECT        dbo.HEAD_LST.NUMBER, dbo.HEAD_LST.TAG, dbo.HEAD_LST.DATE_N, dbo.INVO_LST.MABL, dbo.INVO_LST.MABL_K, dbo.INVO_LST.N_MOIN, dbo.INVO_LST.MABL_K - dbo.INVO_LST.N_MOIN AS mabkbt, 
                                    dbo.INVO_LST.IMBAA, dbo.INVO_LST.MABL_K - dbo.INVO_LST.N_MOIN + dbo.INVO_LST.IMBAA AS mabkn, dbo.CUST_HESAB.MCODEM, dbo.CUST_HESAB.tob, dbo.INVO_LST.CODE, dbo.STUF_DEF.sstid, dbo.STUF_DEF.mu, 
                                    ISNULL(dbo.STUF_DEF.NAME, N' ') + N' ' + ISNULL(dbo.INVO_LST.MANDAH, N' ') AS KALA, dbo.INVO_LST.MEGHk, dbo.STUF_DEF.vra, dbo.CUST_HESAB.ECODE,dbo.INVO_LST.MEGH_MAR, dbo.INVO_LST.N_KOL, dbo.INVO_LST.JAY
                                    FROM            dbo.HEAD_LST INNER JOIN
                                                             dbo.INVO_LST ON dbo.HEAD_LST.NUMBER = dbo.INVO_LST.NUMBER AND dbo.HEAD_LST.TAG = dbo.INVO_LST.TAG INNER JOIN
                                                             dbo.CUST_HESAB ON dbo.HEAD_LST.CUST_NO = dbo.CUST_HESAB.hes INNER JOIN
                                                             dbo.STUF_DEF ON dbo.INVO_LST.CODE = dbo.STUF_DEF.CODE
                                    WHERE        (dbo.HEAD_LST.NUMBER = {number}) AND (dbo.HEAD_LST.TAG = {tag}) ");
            }

            return _db.DoGetDataSQL<DRV_TBL>(string.Format(sql, number, tag)).ToList();
        }

        private void RecordFailedInvoiceLocal(long number, int tag, string errorMessage)
        {
            try
            {
                var idd = _fn.GetNewIDD();
                var inno = _fn.GenerateFixedLengthInno(_sazman.YEA.ToString(), number);
                byte apiType = (byte)(_isSandbox ? 0 : 1);

                // LOCAL_ERROR : اصلاً به سامانه نرفته (اعتبارسنجی محلی یا انصراف کاربر).
                // با FAILED واقعیِ سامانه یکی نیست و نباید در گزارش‌ها با آن قاطی شود.
                string sql = @"
                    INSERT INTO dbo.TAXDTL
                    (Inno, NUMBER, TAG, TheStatus, TheError, IDD, CRT, ApiTypeSent)
                    VALUES
                    (@Inno, @Number, @Tag, 'LOCAL_ERROR', @Error, @IDD, GETDATE(), @Api)";
                _db.DoExecuteSQL(sql, new { Inno = inno, Number = number, Tag = tag, Error = errorMessage, IDD = idd, Api = apiType });
            }
            catch (Exception ex)
            {
                // Ignored
            }
        }

        private void PersistChunk(List<InvoiceDto> sent, List<List<TAXDTL>> recordsSets, IEnumerable<PacketResponse> responses, int tag, bool outcomeUnknown = false)
        {
            // مرتب‌سازی مطابق با ایندکس
            var pairs = sent.Select((dto, idx) => new { dto, records = recordsSets[idx], resp = responses.ElementAt(idx) });
            Exception firstDbException = null;

            foreach (var pair in pairs)
            {
                var header = pair.dto.Header;
                var uid = pair.resp.Uid;
                var refNum = pair.resp.ReferenceNumber;

                // تفکیک سه حالت — قبلاً هر چیزی که UID نداشت FAILED ثبت می‌شد،
                // از جمله قطعی شبکه که در آن اصلاً نمی‌دانیم سامانه سند را گرفته یا نه.
                //   PENDING : سامانه UID داده، در صف پردازش است.
                //   UNKNOWN : پاسخ نرسیده؛ قبل از هر تلاش مجدد باید استعلام شود.
                //   FAILED  : سامانه صریحاً رد کرده.
                var status = uid != null
                    ? "PENDING"
                    : (outcomeUnknown ? "UNKNOWN" : "FAILED");

                try
                {
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
                            // Inno دیگر «سال + شماره فاکتور» نیست، پس نمی‌توان شماره فاکتور را از آن
                            // استخراج کرد. اگر NUMBER نداشتیم، null می‌نویسیم نه یک عدد ساختگی.
                            ["Number"] = (record?.NUMBER > 0 ? (object)record.NUMBER : DBNull.Value),
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
                catch (Exception dbEx)
                {
                    // این صورتحساب به سامانه رفته ولی در دیتابیس ثبت نشد.
                    MoadianRules.WriteRecoveryFile(new
                    {
                        SavedAt = DateTime.Now,
                        Reason = "ارسال گروهی: ارسال به سامانه انجام شد اما ثبت در دیتابیس ناموفق بود",
                        Taxid = header.Taxid,
                        Inno = header.Inno,
                        Irtaxid = header.Irtaxid,
                        Ins = header.Ins,
                        ReferenceNumber = refNum,
                        Uid = uid,
                        Status = status,
                        Tag = tag,
                        ApiTypeSent = _isSandbox ? 0 : 1,
                        DbError = dbEx.Message
                    });

                    // ثبت بقیه صورتحساب‌های همین بسته را ادامه بده تا اگر دیتابیس برای
                    // آن‌ها هم خطا داد، هر کدام فایل بازیابی مستقل خود را داشته باشند.
                    // بعد از بررسی تمام بسته، اولین استثنا با stack trace اصلی بازپرتاب می‌شود.
                    if (firstDbException == null)
                        firstDbException = dbEx;
                }
            }

            if (firstDbException != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(firstDbException).Throw();
        }

        #region MyRegion
        private (decimal? Cap, decimal? Insp, string? ErrorMessage) CalculateCapInsp(int setm, decimal tbill, decimal? capInput, int inty, decimal tvam = 0, decimal todam = 0)
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

            // جدول ۲۴ ص۴۵ ردیف ۱: روش تسویه فقط ۱ نقدی، ۲ نسیه، ۳ نقدی/نسیه است.
            // مقادیر ۴ تا ۷ (چک، ساتنا، کارتخوان، سایر) در سند وجود ندارند و اگر به
            // سامانه برسند رد می‌شوند؛ پس اینجا هم پذیرفته نمی‌شوند.
            switch (setm)
            {
                case 1: // نقدی
                    // سند برای این حالت قاعده‌ای برای مقدار cap تعیین نکرده و سامانه هم
                    // در عمل مبلغ کل را می‌پذیرد. رفتار موجود حفظ می‌شود.
                    capResult = tbill;
                    inspResult = 0;
                    break;

                case 2: // نسیه
                    capResult = 0;
                    inspResult = tbill;
                    break;

                case 3: // نقدی/نسیه
                    {
                        // جدول ۲۵ ص۴۶ :  C  = Xs − W2 − W − Cr
                        // جدول ۲۶ ص۴۷ :  Cr = Xs − W2 − W − C
                        // FAQ ص۳۱ س۱۰-۵ : «دلیل اصلی این اشتباه کم نکردن مقادیر مالیات
                        //                   از مجموع صورتحساب می‌باشد».
                        decimal basis = MoadianRules.SettlementBase(tbill, tvam, todam);

                        if (basis <= 0)
                            return (null, null, "برای تسویه نقدی/نسیه، مبلغ صورتحساب پس از کسر مالیات و عوارض صفر است.");

                        if (capInput.HasValue && capInput.Value > 0)
                        {
                            capResult = Math.Truncate(capInput.Value);

                            if (capResult >= basis)
                            {
                                // جدول ۲۵ ردیف ۱ و ۳: cap باید از مجموع کوچکتر و از صفر بزرگتر باشد،
                                // و insp هم باید بزرگتر از صفر بماند.
                                return (null, null,
                                    $"مبلغ نقدی ({capResult:N0}) باید از مبنای تقسیم ({basis:N0} = مبلغ کل منهای مالیات و عوارض) کوچکتر باشد " +
                                    "تا مبلغ نسیه بزرگتر از صفر بماند. در غیر این صورت روش تسویه باید «نقدی» انتخاب شود.");
                            }
                        }
                        else
                        {
                            return (null, null, "برای روش تسویه «نقدی/نسیه»، مبلغ پرداخت نقدی باید مشخص شود.");
                        }

                        inspResult = basis - capResult;

                        if (inspResult <= 0)
                            return (null, null, "در تسویه نقدی/نسیه، مبلغ نسیه باید بزرگتر از صفر باشد.");
                    }
                    break;

                default:
                    errorMessage = $"روش تسویه با کد {setm} نامعتبر است. جدول ۲۴ ص۴۵ فقط ۱ (نقدی)، ۲ (نسیه) و ۳ (نقدی/نسیه) را می‌پذیرد.";
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
