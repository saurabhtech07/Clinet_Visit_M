-- =========================================================
--  FOLLOW-UP CENTER: TABLE + MIGRATION  (FIXED)
--  Column ka naam "Status" reserved keyword tha - ab "FollowUpStatus"
--  Safe script - jitni baar chalao, koi problem nahi
-- =========================================================

IF OBJECT_ID('dbo.FollowUp') IS NULL
BEGIN
    CREATE TABLE dbo.FollowUp
    (
        FollowUpId       INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ClientId         INT NOT NULL,
        VisitId          INT NULL,
        UserId           INT NULL,
        SourceType       NVARCHAR(10) NOT NULL DEFAULT 'Visit',
        Description      NVARCHAR(300) NULL,
        NextFollowUpDate DATE NULL,
        FollowUpStatus   NVARCHAR(20)  NOT NULL DEFAULT 'Pending',  -- Pending / Done (naam badla)
        Remarks          NVARCHAR(500) NULL,
        DoneOn           DATETIME NULL,
        CreatedBy        INT NULL,
        CreatedOn        DATETIME NOT NULL DEFAULT GETDATE()
    );

    CREATE INDEX IX_FollowUp_NextDate ON dbo.FollowUp(NextFollowUpDate);
    CREATE INDEX IX_FollowUp_Status   ON dbo.FollowUp(FollowUpStatus);
    CREATE INDEX IX_FollowUp_Client   ON dbo.FollowUp(ClientId);
END

-- =========================================================
--  MIGRATION: Purani Pending visits ko FollowUp me
-- =========================================================
INSERT INTO dbo.FollowUp (ClientId, VisitId, UserId, SourceType, Description, NextFollowUpDate, FollowUpStatus, Remarks, CreatedOn)
SELECT v.ClientId,
       v.VisitId,
       v.UserId,
       'Visit',
       N'Follow-up – visit ke baad: ' + ISNULL(c.ClientName, N'CL-' + CAST(v.ClientId AS NVARCHAR(20))),
       v.NextFollowUpDate,
       'Pending',
       v.Remarks,
       v.CreatedOn
FROM dbo.ClientVisit v
LEFT JOIN dbo.Client c ON v.ClientId = c.ClientId
WHERE v.NextFollowUpDate IS NOT NULL
  AND ISNULL(v.FollowUpStatus, '') IN ('Pending', 'Overdue')
  AND NOT EXISTS (SELECT 1 FROM dbo.FollowUp f WHERE f.VisitId = v.VisitId);