/* =====================================================================
   Daily Support System - one-time data merge (Description -> Issue)
   ---------------------------------------------------------------------
   App me ab sirf ek hi field hai: ISSUE (DB column: Issue).
   Purane records ka data "Description" me pada hai, usko "Issue" me
   copy kar dete hain taaki grid/form me purana data bhi dikhe.

   Safe / Idempotent: sirf wahi rows update hoti hain jahan Issue khaali
   hai aur Description bhara hua hai. Dobara chalane par koi asar nahi.

   Chalane ka tarika: SQL Server Management Studio me ye file khol ke
   Execute (F5) kar dein. (Is machine se DB access nahi hai.)
   ===================================================================== */

USE Db_Saurabh;
GO

SET NOCOUNT ON;
GO

PRINT '--- Description -> Issue merge START ---';
GO

DECLARE @pending INT;
SELECT @pending = COUNT(*)
FROM dbo.DailySupport
WHERE (Issue IS NULL OR LTRIM(RTRIM(Issue)) = '')
  AND Description IS NOT NULL
  AND LTRIM(RTRIM(Description)) <> '';

PRINT 'Total rows to merge (Issue khaali + Description bhara): ' + CAST(@pending AS VARCHAR(20));
GO

BEGIN TRANSACTION;

UPDATE dbo.DailySupport
SET Issue = Description
WHERE (Issue IS NULL OR LTRIM(RTRIM(Issue)) = '')
  AND Description IS NOT NULL
  AND LTRIM(RTRIM(Description)) <> '';

DECLARE @updated INT = @@ROWCOUNT;

COMMIT TRANSACTION;

PRINT 'Rows updated (Description copied to Issue): ' + CAST(@updated AS VARCHAR(20));
GO

PRINT '--- Description -> Issue merge DONE ---';
GO
