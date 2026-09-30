/*
  DDL_Import.sql
  -------------
  Backup of the schema that ClientVisitManagement.Data.ImportRepository creates
  automatically on the first Import request (see its BuildSchemaDdl method).
  This file only exists so the tables can be created up-front (or repaired)
  by hand.

  IMPORTANT
  - This is a separate file on purpose. Database/DDL.sql is the original, stale
    schema script and is NOT modified by the Import feature.
  - Every statement is guarded, so running it more than once is harmless.
  - Nothing here touches any pre-existing table, and there are no foreign keys
    between our own tables either - the feature stays fully isolated from
    Client / ClientVisit / DailySupport / Users.
  - The Import tables are deliberately NOT added to DynamicRepository's
    AllowedTables, so the generic dynamic-table screens cannot reach them.

  Run with a login that may create tables (the app login already has this).
*/

SET NOCOUNT ON;
GO

/* ---------------------------------------------------------------- settings */

IF OBJECT_ID(N'ImportConfig', N'U') IS NULL
CREATE TABLE [ImportConfig](
    ConfigKey    NVARCHAR(100) NOT NULL PRIMARY KEY,
    ConfigValue  NVARCHAR(MAX)  NULL,
    Description  NVARCHAR(300) NULL
);
GO

/* ------------------------------------------------------------ saved files */
/* One row per uploaded file. All of its rows live in DataJson as one JSON
   array, so there is no second table, no foreign key and no cascade delete. */

IF OBJECT_ID(N'ImportFile', N'U') IS NULL
CREATE TABLE [ImportFile](
    FileId      INT IDENTITY(1,1) PRIMARY KEY,
    FileName    NVARCHAR(260) NOT NULL,
    Headers     NVARCHAR(MAX)  NULL,
    DataJson    NVARCHAR(MAX)  NULL,
    /* Not "RowCount": ROWCOUNT is a reserved T-SQL keyword. */
    RowTotal    INT NOT NULL DEFAULT 0,
    ColumnCount INT NOT NULL DEFAULT 0,
    SavedByName NVARCHAR(100) NULL,
    /* Filled in by the database, never by the app. */
    SavedOn     DATETIME NOT NULL DEFAULT GETDATE()
);
GO

/* ------------------------------------------------------------ default config */
/* Seeded only when the key is missing, so later edits in the table survive. */

DECLARE @seed TABLE (ConfigKey NVARCHAR(100), ConfigValue NVARCHAR(MAX), Description NVARCHAR(300));

INSERT INTO @seed (ConfigKey, ConfigValue, Description) VALUES
 (N'MaxRowsPerFile',    N'10000',               N'Ek file me kitni rows save ho sakti hain'),
 (N'PageSizeOptions',   N'25,50,100,200',       N'Rows-per-page dropdown ke options'),
 (N'DefaultPageSize',   N'50',                  N'Default rows per page'),
 (N'AllowedExtensions', N'xlsx,xls,xlsm,csv',   N'Kaunsi file types allowed'),
 (N'MaxFileSizeMB',     N'10',                  N'Kitna bada file (MB)'),
 (N'LargeFileWarnRows', N'50000',               N'Is se zyada rows par warning'),
 (N'TabSavedLabel',     N'Saved Files',         N'Tab 1 ka label'),
 (N'TabCurrentLabel',   N'Current File',        N'Tab 2 ka label'),
 (N'AllowDelete',       N'1',                   N'1 = delete button dikhega, 0 = nahi'),
 (N'DefaultSortDir',    N'ASC',                 N'Default column sort'),
 (N'DateFormat',        N'dd MMM yyyy hh:mm tt',N'Saved-on date ka format'),
 (N'MaxFilesInList',    N'50',                  N'Ek page par kitni saved files');

INSERT INTO [ImportConfig] (ConfigKey, ConfigValue, Description)
SELECT s.ConfigKey, s.ConfigValue, s.Description
FROM @seed s
WHERE NOT EXISTS (SELECT 1 FROM [ImportConfig] c WHERE c.ConfigKey = s.ConfigKey);
GO

PRINT 'Import schema is ready.';
GO
