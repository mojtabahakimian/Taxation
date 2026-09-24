/* ------------------------------------------------------------------
   پاک کردن همه داده تستی «ارسال گروهی سنگین».

   فقط ردیف‌هایی که seed_bulk_test.sql ساخته را برمی‌دارد:
       HEAD_LST  : TAG = 88   (چون HEAD_BACK_ANBAR یعنی TAG - 11)
       INVO_LST  : TAG = 77
       TAXDTL    : TAG = 77
       NUMBER بین 1000001 و 1000120
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
-- دیتابیس عمدا اینجا hardcode نشده. اجراکننده باید با -d بگوید کدام دیتابیس.
-- قبلا اینجا «USE YAZDSEPAR1405_06_25;» بود و باعث می‌شد داده تستی در یک
-- دیتابیس ساخته شود در حالی که هارنس تست (از روی C:\correct\CNR.udl)
-- دیتابیس دیگری را می‌خواند — یعنی تست‌ها هیچ داده‌ای پیدا نمی‌کردند.

DECLARE @HTAG INT = 77, @HEADTAG INT = 88, @LO BIGINT = 1000001, @HI BIGINT = 1000120;

DELETE FROM dbo.TAXDTL            WHERE TAG = @HTAG AND NUMBER BETWEEN @LO AND @HI;
PRINT 'TAXDTL deleted: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

DELETE FROM dbo.HEAD_LST_EXTENDED WHERE TGU = @HTAG AND NUMBER BETWEEN @LO AND @HI;
PRINT 'HEAD_LST_EXTENDED deleted: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

DELETE FROM dbo.INVO_LST          WHERE TAG = @HTAG AND NUMBER BETWEEN @LO AND @HI;
PRINT 'INVO_LST deleted: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

DELETE FROM dbo.HEAD_LST          WHERE TAG IN (@HTAG, @HEADTAG) AND NUMBER BETWEEN @LO AND @HI;
PRINT 'HEAD_LST deleted: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

SELECT 'HEAD_LST' AS TBL, COUNT(*) AS REMAINING FROM dbo.HEAD_LST WHERE TAG IN (@HTAG, @HEADTAG)
UNION ALL SELECT 'INVO_LST', COUNT(*) FROM dbo.INVO_LST WHERE TAG = @HTAG
UNION ALL SELECT 'TAXDTL',   COUNT(*) FROM dbo.TAXDTL   WHERE TAG = @HTAG;
