-- =========================================================
--  LEADS TABLE
--  Lead Management module ka base table.
--  Safe script: jitni baar chalao, koi problem nahi.
-- =========================================================

IF OBJECT_ID(N'dbo.Leads', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Leads
    (
        LeadId            INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        LeadPerson        NVARCHAR(100)  NULL,   -- contact person ka naam
        Company           NVARCHAR(150)  NULL,   -- company name
        Mobile            NVARCHAR(20)   NULL,
        Email             NVARCHAR(100)  NULL,
        City              NVARCHAR(100)  NULL,
        StateId           INT            NULL,   -- -> StateMaster(StateId)
        Status            NVARCHAR(30)   NULL,   -- New / Contacted / Qualified / Won / Lost ...
        DealValue         DECIMAL(18,2)  NULL,
        Requirement       NVARCHAR(1000) NULL,   -- customer ki requirement / need
        AssignedTo        INT            NULL,   -- -> Users(UserId)
        ClientId          INT            NULL,   -- -> Client(ClientId) : Company Master link
        CreatedOn         DATETIME       NOT NULL CONSTRAINT DF_Leads_CreatedOn  DEFAULT (GETDATE()),
        EntryTime         DATETIME       NOT NULL CONSTRAINT DF_Leads_EntryTime  DEFAULT (GETDATE()),
        IsConverted       BIT            NOT NULL CONSTRAINT DF_Leads_IsConverted DEFAULT (0),
        ConvertedClientId INT            NULL,   -- -> Client(ClientId) after conversion
        ConvertedOn       DATETIME       NULL
    );

    CREATE INDEX IX_Leads_Status     ON dbo.Leads(Status);
    CREATE INDEX IX_Leads_AssignedTo ON dbo.Leads(AssignedTo);
    CREATE INDEX IX_Leads_StateId    ON dbo.Leads(StateId);
END
GO

-- =========================================================
--  UPGRADE (purani table ho to) — idempotent, data safe
-- =========================================================
IF OBJECT_ID(N'dbo.Leads', N'U') IS NOT NULL
BEGIN
    -- Requirement column (customer requirement / need)
    IF COL_LENGTH('dbo.Leads','Requirement') IS NULL
        ALTER TABLE dbo.Leads ADD Requirement NVARCHAR(1000) NULL;

    -- ClientId column (Company Master / Client link)
    IF COL_LENGTH('dbo.Leads','ClientId') IS NULL
        ALTER TABLE dbo.Leads ADD ClientId INT NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Leads_ClientId' AND object_id = OBJECT_ID('dbo.Leads'))
        CREATE INDEX IX_Leads_ClientId ON dbo.Leads(ClientId);
END
GO
