/* =====================================================================
   CLEANUP — Unused tables & columns drop  (idempotent)
   Db_Saurabh

   17 Oct 2026 · ClientVisitManagement

   DROPPED TABLES (kisi module me use nahi hote):
       - AppMasters
       - CategoryMaster
       - CustomerMaster
       - CallLog   (CustomerMaster par FK tha)

   DROPPED COLUMNS (legacy / duplicate):
       - Client.CompanyType     (ab CompanyTypeId use hota hai)
       - Users.RoleName         (role Roles join se aata hai)
       - DailyReport.Status     (module se hata diya gaya)

   NOTE: DailyReport.Calls / Visit / NewLeads NAHI drop kiye gaye —
         ye grid me LIVE counts ke anchor columns hain.

   Backup: Desktop\DB_DropUnused_Backup_2026-10-16.sql
   ===================================================================== */

-- ---------------------------------------------------------------
-- 1) TABLES
-- ---------------------------------------------------------------
IF OBJECT_ID('dbo.FK_CallLog_CustomerMaster','F') IS NOT NULL
    ALTER TABLE dbo.CallLog DROP CONSTRAINT FK_CallLog_CustomerMaster;
GO

IF OBJECT_ID('dbo.CallLog','U')        IS NOT NULL DROP TABLE dbo.CallLog;
GO
IF OBJECT_ID('dbo.CustomerMaster','U') IS NOT NULL DROP TABLE dbo.CustomerMaster;
GO
IF OBJECT_ID('dbo.AppMasters','U')     IS NOT NULL DROP TABLE dbo.AppMasters;
GO
IF OBJECT_ID('dbo.CategoryMaster','U') IS NOT NULL DROP TABLE dbo.CategoryMaster;
GO

-- ---------------------------------------------------------------
-- 2) COLUMNS
-- ---------------------------------------------------------------
-- Client.CompanyType
IF COL_LENGTH('dbo.Client','CompanyType') IS NOT NULL
    ALTER TABLE dbo.Client DROP COLUMN CompanyType;
GO

-- Users.RoleName
IF COL_LENGTH('dbo.Users','RoleName') IS NOT NULL
    ALTER TABLE dbo.Users DROP COLUMN RoleName;
GO

-- DailyReport.Status  (default constraint pehle hatana zaroori hai)
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_DailyReport_Status')
    ALTER TABLE dbo.DailyReport DROP CONSTRAINT DF_DailyReport_Status;
GO
IF COL_LENGTH('dbo.DailyReport','Status') IS NOT NULL
    ALTER TABLE dbo.DailyReport DROP COLUMN Status;
GO

PRINT 'Unused tables & columns dropped successfully.';
GO
