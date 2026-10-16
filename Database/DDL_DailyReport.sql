-- ============================================================
--  Daily Report  (daily lead activity entry)
--  Manual: Work Summary + Issues/Concerns.
--  AUTO (LIVE): Calls (DailySupport), Visit (ClientVisit), New Leads (Leads).
--  EntryTime auto. Koi status / approval workflow nahi.
--  Idempotent: jitni baar chalao, koi problem nahi.
-- ============================================================

----------------------------------------------------------------
-- 1) FRESH CREATE (table na ho to — company standard schema)
----------------------------------------------------------------
IF OBJECT_ID('dbo.DailyReport', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DailyReport
    (
        DailyReportId INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_DailyReport PRIMARY KEY,
        ReportDate    DATE          NOT NULL
            CONSTRAINT DF_DailyReport_ReportDate DEFAULT (CAST(GETDATE() AS DATE)),
        AssignedTo    INT           NOT NULL,                       -- Users.UserId (report kis employee ke naam hai)
        -- Calls / Visit / NewLeads: ye AUTO hote hain (DailySupport / ClientVisit / Leads se
        -- LIVE count). Columns sirf grid display ke liye retained hain — store value use nahi hoti.
        Calls         INT           NULL CONSTRAINT DF_DailyReport_Calls    DEFAULT (0),
        Visit         INT           NULL CONSTRAINT DF_DailyReport_Visit    DEFAULT (0),
        NewLeads      INT           NULL CONSTRAINT DF_DailyReport_NewLeads DEFAULT (0),
        Summary       NVARCHAR(1000) NULL,                          -- Work Summary
        Remark        NVARCHAR(1000) NULL,                          -- Issues / Concerns
        EntryTime     DATETIME      NOT NULL CONSTRAINT DF_DailyReport_EntryTime DEFAULT (GETDATE()),
        CreatedBy     INT           NULL,                           -- Users.UserId (kisne enter kiya)
        ModifiedBy    INT           NULL,                           -- Users.UserId (kisne edit kiya)
        ModifiedOn    DATETIME      NULL,

        -- company standard: ek employee = ek din = ek report
        CONSTRAINT UQ_DailyReport_Date_Assigned UNIQUE (ReportDate, AssignedTo),
        -- counts kabhi negative nahi hote
        CONSTRAINT CK_DailyReport_Calls    CHECK (ISNULL(Calls,0)    >= 0),
        CONSTRAINT CK_DailyReport_Visit    CHECK (ISNULL(Visit,0)    >= 0),
        CONSTRAINT CK_DailyReport_NewLeads CHECK (ISNULL(NewLeads,0) >= 0)
    );
END
GO

----------------------------------------------------------------
-- 2) UPGRADE (purani table ho to) — har step guarded hai,
--    koi bhi step fail ho to baaki chalte rehte hain.
----------------------------------------------------------------
IF OBJECT_ID('dbo.DailyReport', 'U') IS NOT NULL
BEGIN
    -- 2a. naye audit columns (purani DB me agar abhi na ho)
    IF COL_LENGTH('dbo.DailyReport', 'ModifiedBy') IS NULL
        ALTER TABLE dbo.DailyReport ADD ModifiedBy INT NULL;
    IF COL_LENGTH('dbo.DailyReport', 'ModifiedOn') IS NULL
        ALTER TABLE dbo.DailyReport ADD ModifiedOn DATETIME NULL;

    -- 2b. AssignedTo backfill: jisne report enter ki thi wahi zyada tar assigned hai
    UPDATE dbo.DailyReport SET AssignedTo = CreatedBy
     WHERE AssignedTo IS NULL AND CreatedBy IS NOT NULL;

    -- 2c. AssignedTo required banao (jab tak NULL rows hain to skip — app fir bhi required rakhta hai)
    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'DailyReport'
                  AND COLUMN_NAME = 'AssignedTo' AND IS_NULLABLE = 'YES')
       AND NOT EXISTS (SELECT 1 FROM dbo.DailyReport WHERE AssignedTo IS NULL)
        ALTER TABLE dbo.DailyReport ALTER COLUMN AssignedTo INT NOT NULL;

    -- 2d. ek employee-ek-din-ek-report (purani duplicate rows ho to index skip)
    IF NOT EXISTS (SELECT 1 FROM sys.indexes
                    WHERE name = 'UQ_DailyReport_Date_Assigned'
                      AND object_id = OBJECT_ID('dbo.DailyReport'))
       AND NOT EXISTS (SELECT ReportDate, AssignedTo FROM dbo.DailyReport
                        GROUP BY ReportDate, AssignedTo HAVING COUNT(*) > 1)
        CREATE UNIQUE INDEX UQ_DailyReport_Date_Assigned
            ON dbo.DailyReport(ReportDate, AssignedTo);

    -- 2e. counts negative na ho (purani negative rows ho to skip — app validate karta hi hai)
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
                    WHERE parent_object_id = OBJECT_ID('dbo.DailyReport')
                      AND name IN ('CK_DailyReport_Calls','CK_DailyReport_Visit','CK_DailyReport_NewLeads'))
       AND NOT EXISTS (SELECT 1 FROM dbo.DailyReport
                        WHERE ISNULL(Calls,0) < 0 OR ISNULL(Visit,0) < 0 OR ISNULL(NewLeads,0) < 0)
    BEGIN
        ALTER TABLE dbo.DailyReport ADD CONSTRAINT CK_DailyReport_Calls
            CHECK (ISNULL(Calls,0) >= 0);
        ALTER TABLE dbo.DailyReport ADD CONSTRAINT CK_DailyReport_Visit
            CHECK (ISNULL(Visit,0) >= 0);
        ALTER TABLE dbo.DailyReport ADD CONSTRAINT CK_DailyReport_NewLeads
            CHECK (ISNULL(NewLeads,0) >= 0);
    END

    -- NOTE: Purani DB me agar 'Status' column maujood hai to woh ab use nahi hota
    -- (module se hata diya gaya hai). Data safe rakhne ke liye drop nahi kiya.
    -- Agar aap column bilkul hi hatana chahte hain (data chala jayega) to neeche
    -- wali line se comment hata kar chala dein:
    -- IF COL_LENGTH('dbo.DailyReport', 'Status') IS NOT NULL
    --     ALTER TABLE dbo.DailyReport DROP COLUMN Status;
END
GO
