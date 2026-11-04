namespace Prg_Moadian.FUNCTIONS
{
    /// <summary>
    /// کوئری‌های پنجرهٔ پیگیری (Prg_TrackSentInvoice) و استعلام بی‌پنجره.
    ///
    /// این متن‌ها قبلاً داخل MainWindow بودند و هیچ تستی به آن‌ها نمی‌رسید. آن موقع
    /// دو باگ داشتند که هر دو با دادهٔ واقعی ثابت شد (بررسی ۱۴۰۵/۰۸/۱۳):
    ///
    /// ۱. گرید ردیف‌ها را با «GROUP BY Taxid, TheStatus, UID, RefrenceNumber, …» جمع
    ///    می‌کرد. ردیف‌های LOCAL_ERROR (اعتبارسنجی پیش از ارسال) هیچ‌کدام از این‌ها را
    ///    ندارند و SQL Server مقادیر NULL را در GROUP BY برابر می‌گیرد؛ پس همهٔ آن‌ها یک
    ///    ردیف می‌شدند. آبادیس: ۵۱ ردیف از ۳۰ فاکتور (۸۹۰ تا ۱۲۴۹) فقط یک ردیف با
    ///    شمارهٔ ۱۲۴۹ نشان داده می‌شد و ۲۹ فاکتور خطادار در پنجره دیده نمی‌شد.
    ///
    /// ۲. منقضی‌کردن ارسال بی‌تکلیفِ بیش از ۳۱ روز با «WHERE IDD = {row.IDD}» بود ولی
    ///    گرید ستون IDD را نمی‌خواند؛ IDD برابر null بود و دستور می‌شد «WHERE IDD = »
    ///    که خطای نحوی است و catch آن را می‌بلعید. هیچ ردیفی EXPIRED نشده بود (هر دو
    ///    دیتابیس صفر)، در حالی که PENDING ِ ۱۵۱ روزه در یزدسپار هست.
    ///
    /// گروه ۳۴ تست‌ها هر دو را قفل می‌کند.
    /// </summary>
    public static class TrackingQueries
    {
        /// <summary>
        /// ارسالی که هنوز تکلیفش روشن نیست و استعلام پس‌زمینه دوباره می‌پرسد.
        /// UNKNOWN اینجا نیست: آن ارسال کد رهگیری ندارد و فقط با uid پرسیده می‌شود
        /// (<see cref="CL_ESTELAM.ResolveUnknownByUid"/>).
        /// </summary>
        public static bool IsUndecided(string? status) => status is null or "" or "NULL" or "PENDING";

        /// <summary>همان شرط <see cref="IsUndecided"/> به زبان SQL.</summary>
        public const string UndecidedSqlCondition =
            "(TheStatus IS NULL OR TheStatus IN (N'', N'NULL', N'PENDING'))";

        /// <summary>
        /// منقضی‌کردن یک ارسال: همهٔ ردیف‌های همان کد رهگیری، و فقط اگر هنوز بی‌تکلیف‌اند.
        /// ارسال بدون کد رهگیری منقضی نمی‌شود؛ کلیدی ندارد که فقط خودش را بگیرد.
        ///
        /// EXPIRED یعنی «دیگر نپرس»، نه «نرفته». همه‌جا مثل PENDING «ارسال‌شده» حساب
        /// می‌شود (لیست ارسال‌نشده‌های گروهی، هشدار ارسال قبلی تکی و گروهی، IsLiveStatus) —
        /// وگرنه فاکتوری که شاید در کارپوشه نشسته به لیست ارسال‌نشده‌ها برمی‌گشت.
        /// </summary>
        public const string ExpireSql =
            "UPDATE dbo.TAXDTL SET TheStatus = N'EXPIRED' " +
            "WHERE RefrenceNumber = @RefrenceNumber AND " + UndecidedSqlCondition;

        /// <summary>
        /// کوئری گرید پنجرهٔ پیگیری.
        ///
        /// یک ردیف برای هر ارسال (Taxid + کد رهگیری + …). ردیفی که شماره مالیاتی ندارد
        /// (LOCAL_ERROR) با IDD ِ خودش جدا می‌ماند، چون هر کدام یک تلاش جداست با خطای
        /// خودش. برای بقیه این کلید NULL است و گروه‌بندی عوض نمی‌شود.
        /// </summary>
        /// <param name="extraWhere">فقط برای تست: محدود کردن به دادهٔ تستی.</param>
        public static string GridSql(int apiTypeSent, bool onlyLast30Days, string? extraWhere = null)
        {
            var where = $"T.ApiTypeSent = {apiTypeSent}";
            if (onlyLast30Days) where += " AND T.CRT >= DATEADD(DAY, -30, GETDATE())";
            if (!string.IsNullOrWhiteSpace(extraWhere)) where += " AND (" + extraWhere + ")";

            return $@"SELECT
                         ROW_NUMBER() OVER (ORDER BY MAX(T.CRT) DESC) AS RowNumber,
                         T.Taxid,
                         MAX(T.Inno) AS Inno,
                         MAX(T.NUMBER) AS NUMBER,
                         MAX(T.TAG) AS TAG,
                         MAX(T.Inty) AS Inty,
                         MAX(T.Inp) AS Inp,
                         MAX(T.Ins) AS Ins,
                         MAX(T.IDD) AS IDD,
                         COUNT(*) AS LineCount,
                         T.UID,
                         T.RefrenceNumber,
                         T.TheConfirmationReferenceId,
                         T.SentTaxMemory,
                         MAX(T.CRT) AS CRT,
                         MAX(CAST(T.TheSuccess AS INT)) AS TheSuccess,
                         T.TheStatus,
                         MAX(T.DATE_N) AS DATE_N,
                         MAX(CAST(T.ApiTypeSent AS INT)) AS ApiTypeSent,
                         MAX(T.TheError) AS TheError,
                         MAX(T.TheWarning) AS TheWarning,
                         MAX(H.CUST_NO) AS CUST_NO,
                         MAX(H.MOLAH) AS MOLAH,
                         MAX(H.SHARAYET) AS SHARAYET
                     FROM dbo.TAXDTL T
                     -- اتصال مستقیم با شماره فاکتور، نه با تجزیه Inno.
                     -- Inno حالا سریال هگزادسیمال حافظه مالیاتی است و
                     -- شش کاراکتر آخرش دیگر شماره فاکتور نیست.
                     LEFT OUTER JOIN dbo.HEAD_LST H ON
                         T.NUMBER = H.NUMBER
                         AND H.TAG = 2
                     WHERE {where}
                     GROUP BY
                         T.Taxid, T.TheStatus, T.UID, T.RefrenceNumber, T.TheConfirmationReferenceId, T.SentTaxMemory,
                         CASE WHEN NULLIF(LTRIM(RTRIM(T.Taxid)), '') IS NULL THEN T.IDD END
                     ORDER BY
                         CRT DESC;";
        }
    }
}
