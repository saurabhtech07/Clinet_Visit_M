-- Audit history for DailySupport tickets.
-- Har create/edit ka record: kisne, kya badla, purana -> naya value, kab.
IF OBJECT_ID('dbo.DailySupportHistory','U') IS NULL
BEGIN
    CREATE TABLE dbo.DailySupportHistory(
        HistoryId      INT IDENTITY(1,1) PRIMARY KEY,
        SupportId      INT            NOT NULL,   -- -> DailySupport.SupportId
        Action         VARCHAR(20)    NOT NULL,   -- Created / Edited
        FieldName      VARCHAR(50)    NULL,       -- AssignedTo, Status, Issue ...
        OldValue       NVARCHAR(200)  NULL,
        NewValue       NVARCHAR(200)  NULL,
        ChangedBy      INT            NULL,       -- -> Users.UserId
        ChangedByName  NVARCHAR(100)  NULL,       -- us waqt ka naam (snapshot)
        ChangedOn      DATETIME       NOT NULL DEFAULT GETDATE()
    );
    CREATE INDEX IX_DSH_SupportId ON dbo.DailySupportHistory(SupportId);
END

-- Backfill: purane tickets ka "Created" record (creator pata nahi isliye
-- AssignedTo ko creator maana, ChangedOn = CreatedOn).
IF NOT EXISTS (SELECT 1 FROM dbo.DailySupportHistory)
    INSERT INTO dbo.DailySupportHistory(SupportId,Action,FieldName,NewValue,ChangedBy,ChangedByName,ChangedOn)
    SELECT d.SupportId,'Created','Ticket',LEFT(ISNULL(d.Issue,''),200),
           d.AssignedTo,u.FullName,ISNULL(d.CreatedOn,d.SupportDate)
    FROM dbo.DailySupport d
    LEFT JOIN dbo.Users u ON d.AssignedTo=u.UserId;
