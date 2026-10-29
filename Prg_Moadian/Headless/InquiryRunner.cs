using Dapper;
using Microsoft.Data.SqlClient;
using Prg_Moadian.CNNMANAGER;
using Prg_Moadian.FUNCTIONS;

namespace Prg_Moadian.Headless
{
    /// <summary>
    /// استعلامِ وضعیتِ ارسال‌ها بدونِ پنجره — همان کارِ BGWorker_DoWork ِ Prg_TrackSentInvoice (هر ۵ دقیقه) و دکمه‌ی
    /// استعلام: هر کدِ رهگیری با CL_ESTELAM.GETESTELAM_REFCODE_UPDATE استعلام می‌شود و همان تابع TAXDTL را به‌روز و در
    /// SUCCESS شماره‌ی مالیاتی را در HEAD_LST_EXTENDED.irtaxid می‌گذارد. ارسالِ بی‌تکلیفی که بیش از ۳۱ روز گذشته
    /// EXPIRED می‌شود.
    ///
    /// تفاوتِ عمدی با Prg_TrackSentInvoice: آنجا EXPIRED با «WHERE IDD = …» ِ ردیفِ گروه‌بندی‌شده زده می‌شد، ولی کوئریِ
    /// گرید ستونِ IDD را نمی‌خواند (همیشه ۰)، پس هیچ ردیفی منقضی نمی‌شد. اینجا با کدِ رهگیری، همه‌ی ردیف‌های همان ارسال.
    /// </summary>
    public static class InquiryRunner
    {
        /// <summary>همان «DateTime.Today - CRT &gt; 31» ِ Prg_TrackSentInvoice.</summary>
        public const int ExpireAfterDays = 31;

        public sealed class Item
        {
            public string Reference { get; set; } = "";
            /// <summary>وضعیت بعد از استعلام: SUCCESS، FAILED، PENDING، IN_PROGRESS، NOT_FOUND، EXPIRED، …</summary>
            public string? Status { get; set; }
            /// <summary>خطاهای سامانه («کد | پیام»، جداشده با ویرگول) یا خطای خودِ استعلام.</summary>
            public string? Error { get; set; }
        }

        public sealed class Result
        {
            public List<Item> Items { get; } = new List<Item>();
            /// <summary>خطای کلی (کلید، حافظه، اتصال)؛ آن‌وقت هیچ استعلامی انجام نشده است.</summary>
            public string? Error { get; set; }
        }

        private sealed class Row
        {
            public string? RefrenceNumber { get; set; }
            public DateTime? Crt { get; set; }
            public string? TheStatus { get; set; }
            public string? TheError { get; set; }
        }

        public static Result Run(string connectionString, bool mainSystem, IEnumerable<string> references, string? taxUrlOverride = null)
        {
            var refs = references.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).Distinct().ToList();
            var result = new Result();
            if (string.IsNullOrWhiteSpace(connectionString)) { result.Error = "رشتهٔ اتصال دیتابیس خالی است."; return result; }
            if (refs.Count == 0) return result;

            using (HeadlessGate.Enter(connectionString))
            {
                var url = taxUrlOverride ?? (mainSystem ? SingleSendRunner.MainUrl : SingleSendRunner.SandboxUrl);
                var rows = Load(connectionString, refs);

                // منقضی‌ها بی‌استعلام
                var stale = rows.Where(r => Undecided(r.TheStatus) && r.Crt is { } c && (DateTime.Today - c).TotalDays > ExpireAfterDays)
                                .Select(r => r.RefrenceNumber!).ToList();
                if (stale.Count > 0)
                {
                    using var cn = new SqlConnection(connectionString);
                    cn.Execute("UPDATE dbo.TAXDTL SET TheStatus = N'EXPIRED' WHERE RefrenceNumber IN @stale", new { stale });
                }

                CL_ESTELAM? estelam = null;
                foreach (var reference in refs)
                {
                    if (stale.Contains(reference)) { result.Items.Add(new Item { Reference = reference, Status = "EXPIRED" }); continue; }
                    try
                    {
                        if (estelam is null)
                        {
                            var e = new CL_ESTELAM();
                            e.GET_INIT_TAX(url);   // توکن؛ خطایش کلی است
                            estelam = e;
                        }
                        estelam.GETESTELAM_REFCODE_UPDATE(reference);
                        result.Items.Add(new Item { Reference = reference });
                    }
                    catch (Exception ex) when (estelam is null || !ReferenceLevel(ex))
                    {
                        // کلید، حافظه یا اتصال: بقیه هم نمی‌روند
                        result.Error = new CustomExceptErMsg().ExpecMsgEr(ex) ?? "خطا در انجام عملیات استعلام.";
                        return result;
                    }
                    catch (Exception ex)
                    {
                        result.Items.Add(new Item { Reference = reference, Error = new CustomExceptErMsg().ExpecMsgEr(ex) ?? "خطا در استعلام." });
                    }
                }

                // وضعیتِ نهایی همان است که CL_ESTELAM در TAXDTL نوشته است
                var after = Load(connectionString, refs).GroupBy(r => r.RefrenceNumber!, StringComparer.OrdinalIgnoreCase)
                                                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                foreach (var item in result.Items)
                {
                    if (!after.TryGetValue(item.Reference, out var r)) continue;
                    item.Status ??= r.TheStatus;
                    if (item.Error is null && !string.IsNullOrWhiteSpace(r.TheError) && r.TheError != "NULL") item.Error = r.TheError;
                }
                return result;
            }
        }

        /// <summary>ارسالی که هنوز تکلیفش روشن نیست (همان فیلترِ UndecidedData ِ Prg_TrackSentInvoice؛ UNKNOWN عمداً بیرون است).</summary>
        public static bool Undecided(string? status) => status is null or "" or "NULL" or "PENDING";

        /// <summary>خطایی که فقط همان یک کدِ رهگیری را می‌گیرد، نه کلِ استعلام را.</summary>
        private static bool ReferenceLevel(Exception ex) => ex is not SqlException && ex is not HttpRequestException && ex is not NullyExceptiony;

        private static List<Row> Load(string cs, List<string> refs)
        {
            using var cn = new SqlConnection(cs);
            return cn.Query<Row>(@"SELECT RefrenceNumber, MAX(CRT) Crt, MAX(TheStatus) TheStatus, MAX(TheError) TheError
                                   FROM dbo.TAXDTL WHERE RefrenceNumber IN @refs GROUP BY RefrenceNumber", new { refs }).ToList();
        }
    }
}
