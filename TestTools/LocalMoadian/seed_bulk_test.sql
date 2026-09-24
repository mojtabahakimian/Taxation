/* ------------------------------------------------------------------
   داده تستی برای «ارسال گروهی سنگین با شماره فاکتور بالای یک میلیون»

   نکته مهم:  dbo.HEAD_BACK_ANBAR یک VIEW روی HEAD_LST است با
              «TAG - 11 AS HTAG»  و  WHERE TAG - 11 > 0.
   مسیر ارسال گروهی از همین view می‌خواند، پس:

       HEAD_LST  →  TAG = 88     (یعنی HTAG = 77)
       INVO_LST  →  TAG = 77
       ارسال     →  tag = 77

   همان نسبتی که در داده واقعی بین TAG=13 و TAG=2 برقرار است.

   پاک‌سازی: cleanup_bulk_test.sql
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
-- دیتابیس عمدا اینجا hardcode نشده. اجراکننده باید با -d بگوید کدام دیتابیس.
-- قبلا اینجا «USE YAZDSEPAR1405_06_25;» بود و باعث می‌شد داده تستی در یک
-- دیتابیس ساخته شود در حالی که هارنس تست (از روی C:\correct\CNR.udl)
-- دیتابیس دیگری را می‌خواند — یعنی تست‌ها هیچ داده‌ای پیدا نمی‌کردند.

SET XACT_ABORT ON;   -- هر خطایی کل تراکنش را برمی‌گرداند
BEGIN TRANSACTION;

DECLARE @HTAG    INT    = 77;
DECLARE @HEADTAG INT    = 88;
DECLARE @FIRST   BIGINT = 1000001;
DECLARE @COUNT   INT    = 120;

/* ------- اول هر باقی‌مانده قبلی را پاک کن (اسکریپت idempotent باشد) -------
   بدون این، اجرای دوباره ردیف‌ها را دو برابر می‌کند و همه مبالغ دو برابر
   می‌شوند — دقیقا همان چیزی که تست Golden گرفت.                          */
DELETE FROM dbo.TAXDTL            WHERE TAG = @HTAG    AND NUMBER BETWEEN @FIRST AND @FIRST + @COUNT - 1;
DELETE FROM dbo.HEAD_LST_EXTENDED WHERE TGU = @HTAG    AND NUMBER BETWEEN @FIRST AND @FIRST + @COUNT - 1;
DELETE FROM dbo.INVO_LST          WHERE TAG = @HTAG    AND NUMBER BETWEEN @FIRST AND @FIRST + @COUNT - 1;
DELETE FROM dbo.HEAD_LST          WHERE TAG IN (@HTAG, @HEADTAG) AND NUMBER BETWEEN @FIRST AND @FIRST + @COUNT - 1;

/* ------- الگو: یک فاکتور واقعی و سالم از همان ساختار ------- */
DECLARE @SrcNumber FLOAT, @SrcHeadTag FLOAT, @SrcLineTag FLOAT;

SELECT TOP 1
       @SrcNumber   = H.NUMBER,
       @SrcHeadTag  = H.TAG,
       @SrcLineTag  = I.TAG
FROM dbo.HEAD_LST  H
JOIN dbo.INVO_LST  I ON I.NUMBER = H.NUMBER AND I.TAG = H.TAG - 11
JOIN dbo.STUF_DEF  S ON S.CODE   = I.CODE
JOIN dbo.CUST_HESAB C ON C.hes   = H.CUST_NO
JOIN dbo.TCOD_ANBAR A ON A.CODE  = I.ANBAR
WHERE S.sstid IS NOT NULL AND LTRIM(RTRIM(S.sstid)) <> ''
  AND S.mu    IS NOT NULL AND LTRIM(RTRIM(S.mu))    <> ''
  AND C.ECODE IS NOT NULL AND LEN(LTRIM(RTRIM(C.ECODE))) IN (11, 14)
  AND ISNULL(S.vra, 0) > 0      -- کالای مالیات‌دار، تا مسیر محاسبه مالیات هم آزموده شود
GROUP BY H.NUMBER, H.TAG, I.TAG
HAVING COUNT(*) BETWEEN 2 AND 6
ORDER BY H.NUMBER DESC;

IF @SrcNumber IS NULL
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR('No healthy template invoice found.', 16, 1);
    RETURN;
END

PRINT 'Template: NUMBER=' + CAST(@SrcNumber AS VARCHAR(30))
    + '  HEAD.TAG=' + CAST(@SrcHeadTag AS VARCHAR(10))
    + '  INVO.TAG=' + CAST(@SrcLineTag AS VARCHAR(10));

/* ------- شماره‌های هدف ------- */
IF OBJECT_ID('tempdb..#N') IS NOT NULL DROP TABLE #N;

/* ------------------------------------------------------------------
   حساس کردن داده مرجع.

   اگر همه مبالغ گرد باشند، Truncate و Round نتیجه یکسان می‌دهند و تست
   Golden تفاوتی نمی‌بیند. پس چند فاکتور اول را عمدا به شکل‌هایی در می‌آوریم
   که هر کدام یک مسیر محاسباتی متفاوت را حساس کنند.
   ------------------------------------------------------------------ */

-- ۱۰۰۰۰۰۱ : مالیات کسردار  →  Truncate در برابر Round را لو می‌دهد
UPDATE dbo.INVO_LST SET MABL = 33333, MEGHk = 3, N_MOIN = 0
WHERE NUMBER = @FIRST AND TAG = @HTAG;

-- ۱۰۰۰۰۰۲ : تعداد کسردار  →  Round(...,4) روی مقدار را حساس می‌کند
UPDATE dbo.INVO_LST SET MEGHk = 2.33333, MABL = 10007
WHERE NUMBER = @FIRST + 1 AND TAG = @HTAG;

-- ۱۰۰۰۰۰۳ : تخفیف کسردار  →  مسیر Adis = Prdis - Dis
UPDATE dbo.INVO_LST SET MABL = 12345, MEGHk = 7, N_MOIN = 1111
WHERE NUMBER = @FIRST + 2 AND TAG = @HTAG;

-- ۱۰۰۰۰۰۴ : قلم هدیه  →  قاعده «مبلغ واحد یک، تخفیف برابر کل»
UPDATE dbo.INVO_LST SET N_KOL = 100
WHERE NUMBER = @FIRST + 3 AND TAG = @HTAG
  AND RADIF = (SELECT MIN(RADIF) FROM dbo.INVO_LST WHERE NUMBER = @FIRST + 3 AND TAG = @HTAG);

-- توجه: قبلا اینجا برای فاکتور ۱۰۰۰۰۰۵ مالیات را صفر می‌کردیم تا دروازه
-- «فقط اگر IMBAA>0» را حساس کنیم. ولی نتیجه‌اش یک صورتحساب *نامعتبر* بود
-- (نرخ غیرصفر با مالیات صفر) که سرور به‌درستی ردش می‌کرد و تست پذیرش دسته را
-- قرمز می‌کرد. همان دروازه در گروه ۲۳ به شکل واحدی پوشش دارد.

-- MABL_K را با ورودی‌های جدید هماهنگ کن
UPDATE dbo.INVO_LST SET MABL_K = MABL * MEGHk
WHERE TAG = @HTAG AND NUMBER BETWEEN @FIRST AND @FIRST + 4;

-- مالیات را غیرصفر بگذار تا دروازه «فقط اگر IMBAA>0» باز شود و کد واقعا
-- دوباره حسابش کند. مقدار دقیقش مهم نیست؛ خود کد بازنویسی‌اش می‌کند.
UPDATE dbo.INVO_LST SET IMBAA = 1
WHERE TAG = @HTAG AND NUMBER BETWEEN @FIRST AND @FIRST + 3;
CREATE TABLE #N (NUMBER BIGINT PRIMARY KEY);

WITH seq AS (
    SELECT TOP (@COUNT) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS i
    FROM sys.all_objects
)
INSERT INTO #N (NUMBER) SELECT @FIRST + i FROM seq;

DECLARE @cols NVARCHAR(MAX), @sel NVARCHAR(MAX), @sql NVARCHAR(MAX);

/* ------- HEAD_LST با TAG = 88 ------- */
SELECT @cols = STUFF((
    SELECT ', ' + QUOTENAME(c.name) FROM sys.columns c
    WHERE c.object_id = OBJECT_ID('dbo.HEAD_LST') AND c.is_computed = 0 AND c.is_identity = 0
    ORDER BY c.column_id FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 2, '');

SELECT @sel = STUFF((
    SELECT ', ' + CASE c.name
                    WHEN 'NUMBER' THEN 'N.NUMBER'
                    WHEN 'TAG'    THEN CAST(@HEADTAG AS NVARCHAR(10))
                    ELSE 'H.' + QUOTENAME(c.name) END
    FROM sys.columns c
    WHERE c.object_id = OBJECT_ID('dbo.HEAD_LST') AND c.is_computed = 0 AND c.is_identity = 0
    ORDER BY c.column_id FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 2, '');

SET @sql = N'INSERT INTO dbo.HEAD_LST (' + @cols + N')
             SELECT ' + @sel + N'
             FROM dbo.HEAD_LST H CROSS JOIN #N N
             WHERE H.NUMBER = @sn AND H.TAG = @st;';
EXEC sp_executesql @sql, N'@sn FLOAT, @st FLOAT', @SrcNumber, @SrcHeadTag;
PRINT 'HEAD_LST (TAG=88) inserted: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

/* ------- HEAD_LST با TAG = 77 (کلید خارجی INVO_LST به آن اشاره می‌کند) ------- */
SELECT @sel = STUFF((
    SELECT ', ' + CASE c.name
                    WHEN 'NUMBER' THEN 'N.NUMBER'
                    WHEN 'TAG'    THEN CAST(@HTAG AS NVARCHAR(10))
                    ELSE 'H.' + QUOTENAME(c.name) END
    FROM sys.columns c
    WHERE c.object_id = OBJECT_ID('dbo.HEAD_LST') AND c.is_computed = 0 AND c.is_identity = 0
    ORDER BY c.column_id FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 2, '');

SET @sql = N'INSERT INTO dbo.HEAD_LST (' + @cols + N')
             SELECT ' + @sel + N'
             FROM dbo.HEAD_LST H CROSS JOIN #N N
             WHERE H.NUMBER = @sn AND H.TAG = @st;';
EXEC sp_executesql @sql, N'@sn FLOAT, @st FLOAT', @SrcNumber, @SrcLineTag;
PRINT 'HEAD_LST (TAG=77) inserted: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

/* ------- INVO_LST با TAG = 77 ------- */
SELECT @cols = STUFF((
    SELECT ', ' + QUOTENAME(c.name) FROM sys.columns c
    WHERE c.object_id = OBJECT_ID('dbo.INVO_LST') AND c.is_computed = 0 AND c.is_identity = 0
    ORDER BY c.column_id FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 2, '');

SELECT @sel = STUFF((
    SELECT ', ' + CASE c.name
                    WHEN 'NUMBER' THEN 'N.NUMBER'
                    WHEN 'TAG'    THEN CAST(@HTAG AS NVARCHAR(10))
                    ELSE 'I.' + QUOTENAME(c.name) END
    FROM sys.columns c
    WHERE c.object_id = OBJECT_ID('dbo.INVO_LST') AND c.is_computed = 0 AND c.is_identity = 0
    ORDER BY c.column_id FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 2, '');

SET @sql = N'INSERT INTO dbo.INVO_LST (' + @cols + N')
             SELECT ' + @sel + N'
             FROM dbo.INVO_LST I CROSS JOIN #N N
             WHERE I.NUMBER = @sn AND I.TAG = @st;';
EXEC sp_executesql @sql, N'@sn FLOAT, @st FLOAT', @SrcNumber, @SrcLineTag;
PRINT 'INVO_LST inserted: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

DROP TABLE #N;

/* ------------------------------------------------------------------
   حساس کردن داده مرجع.

   اگر همه مبالغ گرد باشند، Truncate و Round نتیجه یکسان می‌دهند و تست
   Golden تفاوتی نمی‌بیند. پس چند فاکتور اول را عمدا به شکل‌هایی در می‌آوریم
   که هر کدام یک مسیر محاسباتی متفاوت را حساس کنند.
   ------------------------------------------------------------------ */

-- ۱۰۰۰۰۰۱ : مالیات کسردار  →  Truncate در برابر Round را لو می‌دهد
UPDATE dbo.INVO_LST SET MABL = 33333, MEGHk = 3, N_MOIN = 0
WHERE NUMBER = @FIRST AND TAG = @HTAG;

-- ۱۰۰۰۰۰۲ : تعداد کسردار  →  Round(...,4) روی مقدار را حساس می‌کند
UPDATE dbo.INVO_LST SET MEGHk = 2.33333, MABL = 10007
WHERE NUMBER = @FIRST + 1 AND TAG = @HTAG;

-- ۱۰۰۰۰۰۳ : تخفیف کسردار  →  مسیر Adis = Prdis - Dis
UPDATE dbo.INVO_LST SET MABL = 12345, MEGHk = 7, N_MOIN = 1111
WHERE NUMBER = @FIRST + 2 AND TAG = @HTAG;

-- ۱۰۰۰۰۰۴ : قلم هدیه  →  قاعده «مبلغ واحد یک، تخفیف برابر کل»
UPDATE dbo.INVO_LST SET N_KOL = 100
WHERE NUMBER = @FIRST + 3 AND TAG = @HTAG
  AND RADIF = (SELECT MIN(RADIF) FROM dbo.INVO_LST WHERE NUMBER = @FIRST + 3 AND TAG = @HTAG);

-- توجه: قبلا اینجا برای فاکتور ۱۰۰۰۰۰۵ مالیات را صفر می‌کردیم تا دروازه
-- «فقط اگر IMBAA>0» را حساس کنیم. ولی نتیجه‌اش یک صورتحساب *نامعتبر* بود
-- (نرخ غیرصفر با مالیات صفر) که سرور به‌درستی ردش می‌کرد و تست پذیرش دسته را
-- قرمز می‌کرد. همان دروازه در گروه ۲۳ به شکل واحدی پوشش دارد.

-- MABL_K را با ورودی‌های جدید هماهنگ کن
UPDATE dbo.INVO_LST SET MABL_K = MABL * MEGHk
WHERE TAG = @HTAG AND NUMBER BETWEEN @FIRST AND @FIRST + 4;

-- مالیات را غیرصفر بگذار تا دروازه «فقط اگر IMBAA>0» باز شود و کد واقعا
-- دوباره حسابش کند. مقدار دقیقش مهم نیست؛ خود کد بازنویسی‌اش می‌کند.
UPDATE dbo.INVO_LST SET IMBAA = 1
WHERE TAG = @HTAG AND NUMBER BETWEEN @FIRST AND @FIRST + 3;

COMMIT TRANSACTION;

/* ------- بررسی اینکه مسیر واقعی ارسال گروهی داده می‌بیند ------- */
SELECT COUNT(*) AS LINES_VISIBLE_TO_BULK
FROM dbo.HEAD_BACK_ANBAR B
JOIN dbo.INVO_LST   I ON I.NUMBER = B.NUMBER AND I.TAG  = B.HTAG
JOIN dbo.TCOD_ANBAR A ON A.CODE   = I.ANBAR
JOIN dbo.HEAD_LST   H ON H.NUMBER = B.NUMBER AND H.TAG  = B.TAG
WHERE B.HTAG = @HTAG AND B.NUMBER >= @FIRST;
