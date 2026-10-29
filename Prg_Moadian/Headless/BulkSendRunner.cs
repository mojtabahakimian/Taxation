using Prg_Moadian.Bulk;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.FUNCTIONS;
using Prg_Moadian.Generaly;

namespace Prg_Moadian.Headless
{
    /// <summary>
    /// ارسالِ گروهی بدونِ پنجره — همان کارِ BTN_SENDGRP_Click ِ Prg_Grpsend برای برنامه‌ای که پنجره ندارد.
    /// منطقِ ساخت و ارسال همه در SendInvoiceBulk است؛ این کلاس فقط محیطِ آن را مثلِ Grpsend می‌چیند:
    /// رشته‌ی اتصال از میزبان، CL_MOADIAN.TaxURL، پاک‌کردنِ حالتِ استاتیک، و قفلِ مشترکِ HeadlessGate.
    ///
    /// پرسش‌ها مثلِ Grpsend دو جورند و هر دو به میزبان می‌رسند:
    /// • Confirm: هشدارِ «قبلاً ارسال شده» (یک پرسش برای همه). false = آن‌ها کنار بروند، بقیه بروند.
    /// • ReviewWarnings: هشدارهای ساختِ فاکتور یکجا (WIN_BULKWARNINGS). خروجی شماره‌های کنارگذاشته؛ null = لغوِ کل.
    /// میزبان می‌تواند در این دو callback منتظرِ پاسخِ کاربر بماند؛ ارسال تا آن موقع جلو نمی‌رود.
    ///
    /// پرسش‌های پیش از ارسالِ Grpsend (تأییدِ نهایی، تاریخِ آینده، هشدارِ «شامل ارسال‌شده‌ها») کارِ رابطِ میزبان است.
    /// </summary>
    public static class BulkSendRunner
    {
        public sealed class Request
        {
            public string ConnectionString { get; set; } = "";
            public List<long> Numbers { get; set; } = new List<long>();
            /// <summary>تگِ ارسال؛ در هر دو محصول ۲ (حواله‌ی فروش).</summary>
            public int Tag { get; set; } = 2;
            public bool MainSystem { get; set; }
            /// <summary>«m» = MrCorrect، «d» = دناافراز.</summary>
            public string Caller { get; set; } = "m";
            /// <summary>نوعِ صورتحساب برای همه؛ null = از سربرگِ هر فاکتور.</summary>
            public int? Inty { get; set; }
            /// <summary>روشِ تسویه برای همه؛ null = از سربرگِ هر فاکتور.</summary>
            public int? Setm { get; set; }
            public bool UseCustomDate { get; set; }
            /// <summary>مثلاً 1405/08/07.</summary>
            public string? CustomDate { get; set; }
            public bool SendOptionalItemName { get; set; } = true;
            /// <summary>فقط برای تست با سرورِ ساختگی.</summary>
            public string? TaxUrlOverride { get; set; }
            public Func<string, bool>? Confirm { get; set; }
            public Func<IReadOnlyList<BulkWarning>, ISet<long>?>? ReviewWarnings { get; set; }
            /// <summary>درصدِ پیشرفت (۰ تا ۱۰۰)، همان که Grpsend نشان می‌دهد.</summary>
            public IProgress<int>? Progress { get; set; }
        }

        public sealed class Result
        {
            /// <summary>تعدادِ صورتحساب‌هایی که به صفِ سامانه رسیدند.</summary>
            public int Success { get; set; }
            /// <summary>شماره‌ی فاکتور ← علتِ نرفتن.</summary>
            public Dictionary<long, string> Failures { get; } = new Dictionary<long, string>();
            /// <summary>خطای کلی (اتصال، کلید، حافظه‌ی مالیاتی)؛ آن‌وقت هیچ فاکتوری نرفته است.</summary>
            public string? Error { get; set; }
        }

        public static async Task<Result> RunAsync(Request request, CancellationToken ct = default)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.ConnectionString))
                return new Result { Error = "رشتهٔ اتصال دیتابیس خالی است." };
            if (request.Numbers.Count == 0)
                return new Result { Error = "هیچ فاکتوری انتخاب نشده است." };

            using (await HeadlessGate.EnterAsync(request.ConnectionString, ct).ConfigureAwait(false))
                return await RunCoreAsync(request).ConfigureAwait(false);
        }

        private static async Task<Result> RunCoreAsync(Request request)
        {
            var result = new Result();
            string? savedUrl = CL_MOADIAN.TaxURL;
            string savedMode = CL_Generaly.MrCorrect;
            try
            {
                CL_CCNNMANAGER db;
                try
                {
                    db = new CL_CCNNMANAGER();
                    CL_MOADIAN.dbms = db;
                }
                catch (Exception)
                {
                    result.Error = "خطا در ارتباط با دیتابیس";
                    return result;
                }

                SingleSendRunner.ResetState();
                CL_MOADIAN.TaxURL = request.TaxUrlOverride
                                    ?? (request.MainSystem ? SingleSendRunner.MainUrl : SingleSendRunner.SandboxUrl);
                CL_ScriptUpdateDB.Go();

                SendInvoiceBulk bulk;
                try
                {
                    bulk = new SendInvoiceBulk(db, CL_MOADIAN.TaxURL)
                    {
                        CALLER_NAME = request.Caller,
                        OnValidationWarning = request.Confirm ?? (_ => false),
                        OnValidationWarningsBatch = request.ReviewWarnings
                    };
                }
                catch (Exception ex)
                {
                    // سازنده توکن می‌گیرد: کلید، حافظه‌ی مالیاتی یا شبکه
                    result.Error = new CustomExceptErMsg().ExpecMsgEr(ex) ?? "خطا در انجام عملیات، ارتباط با سامانه مقدور نیست.";
                    return result;
                }

                var r = await bulk.SendAsync(request.Numbers, request.Tag, request.Inty, request.Setm, request.Progress,
                                             request.UseCustomDate, request.CustomDate ?? "", request.SendOptionalItemName)
                                  .ConfigureAwait(false);
                result.Success = r.Success;
                foreach (var kv in r.Failures) result.Failures[kv.Key] = kv.Value;
                return result;
            }
            catch (Exception ex)
            {
                result.Error = new CustomExceptErMsg().ExpecMsgEr(ex) ?? "خطا در انجام عملیات، ارتباط با سامانه مقدور نیست.";
                return result;
            }
            finally
            {
                CL_MOADIAN.TaxURL = savedUrl;
                CL_Generaly.MrCorrect = savedMode;
            }
        }
    }
}
