-- =========================================================
--  DAILY FOLLOW UP TABLE
--  Daily Follow Up module ka base table.
--  Lead Management ka hissa - har din ka follow-up track karta hai.
--  Safe script: jitni baar chalao, koi problem nahi.
-- =========================================================

IF OBJECT_ID(N'dbo.DailyFollowUp', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DailyFollowUp
    (
        DailyFollowUpId  INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        LeadId           INT            NULL,   -- -> Leads(LeadId)
        FollowUpDate     DATE           NOT NULL CONSTRAINT DF_DFU_FollowUpDate DEFAULT (CAST(GETDATE() AS DATE)),
        NextFollowUpDate DATE           NULL,   -- agli follow-up date
        ContactPerson    NVARCHAR(100)  NULL,   -- Lead Person
        Company          NVARCHAR(150)  NULL,
        Mobile           NVARCHAR(20)   NULL,
        City             NVARCHAR(100)  NULL,
        Status           NVARCHAR(20)   NOT NULL CONSTRAINT DF_DFU_Status DEFAULT ('Pending'), -- Pending / Done
        Priority         NVARCHAR(20)   NULL,   -- Low / Medium / High
        AssignedTo       INT            NULL,   -- -> Users(UserId)
        Remarks          NVARCHAR(500)  NULL,
        CreatedBy        INT            NULL,   -- -> Users(UserId) jo entry kar raha hai
        CreatedOn        DATETIME       NOT NULL CONSTRAINT DF_DFU_CreatedOn DEFAULT (GETDATE())
    );

    CREATE INDEX IX_DFU_FollowUpDate ON dbo.DailyFollowUp(FollowUpDate);
    CREATE INDEX IX_DFU_NextDate     ON dbo.DailyFollowUp(NextFollowUpDate);
    CREATE INDEX IX_DFU_Status       ON dbo.DailyFollowUp(Status);
    CREATE INDEX IX_DFU_Priority     ON dbo.DailyFollowUp(Priority);
    CREATE INDEX IX_DFU_AssignedTo   ON dbo.DailyFollowUp(AssignedTo);
    CREATE INDEX IX_DFU_LeadId       ON dbo.DailyFollowUp(LeadId);
END
GO
