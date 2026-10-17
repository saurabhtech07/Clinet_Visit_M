-- =========================================================
--  LEADS -> PROPER CRM UPGRADE
--  Ye script Leads module ko "proper CRM" banata hai.
--
--  Naye columns (Leads):
--    LeadSource        -> lead kahan se aaya (Website / Referral / ...)
--    Priority          -> Hot / Warm / Cold
--    NextFollowUpDate  -> agli follow-up date (reminder ke liye)
--    LastContactedOn   -> aakhri baar kab contact hua
--
--  Nayi table:
--    LeadActivity      -> call / meeting / email history (timeline)
--
--  Safe script: jitni baar chalao, koi problem nahi. Data safe.
-- =========================================================

IF OBJECT_ID(N'dbo.Leads', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Leads','LeadSource') IS NULL
        ALTER TABLE dbo.Leads ADD LeadSource NVARCHAR(50) NULL;

    IF COL_LENGTH('dbo.Leads','Priority') IS NULL
        ALTER TABLE dbo.Leads ADD Priority NVARCHAR(20) NULL;

    IF COL_LENGTH('dbo.Leads','NextFollowUpDate') IS NULL
        ALTER TABLE dbo.Leads ADD NextFollowUpDate DATE NULL;

    IF COL_LENGTH('dbo.Leads','LastContactedOn') IS NULL
        ALTER TABLE dbo.Leads ADD LastContactedOn DATETIME NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Leads_Priority' AND object_id = OBJECT_ID('dbo.Leads'))
        CREATE INDEX IX_Leads_Priority ON dbo.Leads(Priority);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Leads_NextFollowUpDate' AND object_id = OBJECT_ID('dbo.Leads'))
        CREATE INDEX IX_Leads_NextFollowUpDate ON dbo.Leads(NextFollowUpDate);
END
GO

-- =========================================================
--  LEAD ACTIVITY  (activity / interaction history)
-- =========================================================
IF OBJECT_ID(N'dbo.LeadActivity', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.LeadActivity
    (
        ActivityId       INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        LeadId           INT            NULL,   -- -> Leads(LeadId)
        ActivityType     NVARCHAR(30)   NULL,   -- Call / Meeting / Email / Visit / WhatsApp / Note
        ActivityDate     DATETIME       NOT NULL CONSTRAINT DF_LeadActivity_Date DEFAULT (GETDATE()),
        Summary          NVARCHAR(500)  NULL,   -- kya baat hui
        Outcome          NVARCHAR(50)   NULL,   -- Positive / Neutral / Negative / No Response / Converted
        NextFollowUpDate DATE           NULL,   -- is activity ke baad agli follow-up
        AssignedTo       INT            NULL,   -- -> Users(UserId)
        CreatedBy        INT            NULL,   -- -> Users(UserId)
        CreatedOn        DATETIME       NOT NULL CONSTRAINT DF_LeadActivity_CreatedOn DEFAULT (GETDATE())
    );

    CREATE INDEX IX_LeadActivity_LeadId ON dbo.LeadActivity(LeadId);
    CREATE INDEX IX_LeadActivity_Date   ON dbo.LeadActivity(ActivityDate);
END
GO
