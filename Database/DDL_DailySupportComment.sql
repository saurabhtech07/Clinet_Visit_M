-- Comments / replies on DailySupport tickets.
-- Any user can write a comment on a ticket; at that moment the comment is
-- attached as a "reply" to the user the ticket is assigned to.
-- No delete/edit (permanent record, same as history).
IF OBJECT_ID('dbo.DailySupportComment','U') IS NULL
BEGIN
    CREATE TABLE dbo.DailySupportComment(
        CommentId      INT IDENTITY(1,1) PRIMARY KEY,
        SupportId      INT            NOT NULL,   -- -> DailySupport.SupportId
        CommentText    NVARCHAR(2000) NOT NULL,
        ReplyToUserId  INT            NULL,       -- assigned user at the time of comment
        ReplyToName    NVARCHAR(100)  NULL,       -- name snapshot (safe if assignment changes later)
        CreatedBy      INT            NULL,       -- -> Users.UserId
        CreatedByName  NVARCHAR(100)  NULL,
        CreatedOn      DATETIME       NOT NULL DEFAULT GETDATE(),
        ReadOn         DATETIME       NULL        -- when the recipient opened the ticket (NULL = new/unread)
    );
    CREATE INDEX IX_DSSComment_SupportId ON dbo.DailySupportComment(SupportId);
END

-- Older databases: add the read-tracking column + index if missing.
IF COL_LENGTH('dbo.DailySupportComment','ReadOn') IS NULL
    ALTER TABLE dbo.DailySupportComment ADD ReadOn DATETIME NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_DSSComment_ReplyTo' AND object_id=OBJECT_ID('dbo.DailySupportComment'))
    CREATE INDEX IX_DSSComment_ReplyTo ON dbo.DailySupportComment(ReplyToUserId, ReadOn);
