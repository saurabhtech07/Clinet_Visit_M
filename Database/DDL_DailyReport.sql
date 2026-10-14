-- ============================================================
--  Daily Report  (daily lead activity entry)
--  Ek din me kisne kitni Calls / Visit / New Leads kiye +
--  Summary / Remark / Status. EntryTime auto.
--  Idempotent: table pehle se ho to kuch nahi karta.
-- ============================================================
IF OBJECT_ID('dbo.DailyReport', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DailyReport
    (
        DailyReportId INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_DailyReport PRIMARY KEY,
        ReportDate    DATE          NOT NULL
            CONSTRAINT DF_DailyReport_ReportDate DEFAULT (CAST(GETDATE() AS DATE)),
        AssignedTo    INT           NULL,                       -- Users.UserId
        Calls         INT           NULL CONSTRAINT DF_DailyReport_Calls    DEFAULT (0),
        Visit         INT           NULL CONSTRAINT DF_DailyReport_Visit    DEFAULT (0),
        NewLeads      INT           NULL CONSTRAINT DF_DailyReport_NewLeads DEFAULT (0),
        Summary       NVARCHAR(1000) NULL,
        Remark        NVARCHAR(1000) NULL,
        Status        NVARCHAR(20)  NOT NULL
            CONSTRAINT DF_DailyReport_Status DEFAULT ('Pending'),
        EntryTime     DATETIME      NOT NULL
            CONSTRAINT DF_DailyReport_EntryTime DEFAULT (GETDATE()),
        CreatedBy     INT           NULL
    );
END
GO
