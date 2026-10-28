/* 1. TICKET NUMBER GENERATION - GenerateTicketNo() */

-- Aaj ke tickets count karke next sequence nikalta hai.
SELECT COUNT(*)
FROM DailySupport WITH (UPDLOCK, HOLDLOCK)
WHERE SupportDate >= @DayStart
  AND SupportDate < @DayEnd;

-- Generated TicketNo duplicate hai ya nahi check karta hai.
SELECT COUNT(*)
FROM DailySupport WITH (UPDLOCK, HOLDLOCK)
WHERE TicketNo = @T;


/* 
   2. DAILY SUPPORT INDEX - Dashboard/grid
   */

-- My Pending badge
SELECT COUNT(*)
FROM DailySupport
WHERE AssignedTo = @M
  AND (Status IS NULL OR Status <> 'Closed');


-- Admin/Manager ke User dropdown ke liye
SELECT UserId, FullName
FROM Users
WHERE IsActive = 1
ORDER BY FullName;




-- Dashboard cards: Today, Total, Open, Closed Today, Pending

    SELECT
        COALESCE(SUM(CASE WHEN 1=1 /* today/date filter */ THEN 1 ELSE 0 END),0),
        COALESCE(SUM(CASE WHEN 1=1 /* range filter */ THEN 1 ELSE 0 END),0),
        COALESCE(SUM(CASE WHEN d.Status IN ('Open','In Progress','On Hold','Pending')
                           /* range filter */ THEN 1 ELSE 0 END),0),
        COALESCE(SUM(CASE WHEN d.Status = 'Closed'
                           /* today/date filter */ THEN 1 ELSE 0 END),0),
        COALESCE(SUM(CASE WHEN d.Status IN ('In Progress','Pending','On Hold')
                           /* range filter */ THEN 1 ELSE 0 END),0)
    FROM DailySupport d
    WHERE 1=1
  

-- Grid total count. Search blank hone par Client join skip hota hai.
SELECT COUNT(*)
FROM DailySupport d
WHERE 1=1


-- Daily Support grid data (server-side pagination)
SELECT d.SupportId, d.TicketNo, d.Status, d.Priority, d.SupportDate,
       d.ClientId, d.Mobile, d.Channel, d.CallType, d.Category,
       d.AssignedTo, d.Issue, d.CreatedOn,
       c.ClientName,
       c.ClientName AS CompanyName,
       u.FullName AS AssignedName
FROM DailySupport d
LEFT JOIN Client c ON d.ClientId = c.ClientId
LEFT JOIN Users u ON d.AssignedTo = u.UserId
WHERE 1=1
  /* search/date/status/user filters */
ORDER BY d.SupportId DESC
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;



-- Index export (CSV/Excel)
SELECT d.TicketNo, d.SupportDate, c.ClientName, d.Mobile,
       d.Channel, d.CallType, d.Category, d.Priority, d.Status,
       u.FullName AS AssignedTo, d.Issue
FROM DailySupport d
LEFT JOIN Client c ON d.ClientId = c.ClientId
LEFT JOIN Users u ON d.AssignedTo = u.UserId
WHERE 1=1
  /* search/date/status/user filters */
ORDER BY d.SupportId DESC;


-- My Work ke six cards
SELECT
    ISNULL(SUM(CASE WHEN AssignedTo=@M AND (Status IS NULL OR Status<>'Closed') THEN 1 ELSE 0 END),0) AS MyPending,
    ISNULL(SUM(CASE WHEN AssignedTo=@M AND CAST(SupportDate AS DATE)=CAST(GETDATE() AS DATE) THEN 1 ELSE 0 END),0) AS TodayCalls,
    ISNULL(SUM(CASE WHEN AssignedTo=@M THEN 1 ELSE 0 END),0) AS TotalCalls,
    ISNULL(SUM(CASE WHEN AssignedTo=@M AND Status IN ('Open','In Progress','On Hold','Pending') THEN 1 ELSE 0 END),0) AS OpenCalls,
    ISNULL(SUM(CASE WHEN AssignedTo=@M AND Status='Closed' AND CAST(SupportDate AS DATE)=CAST(GETDATE() AS DATE) THEN 1 ELSE 0 END),0) AS ClosedToday,
    ISNULL(SUM(CASE WHEN AssignedTo=@M AND Status IN ('In Progress','Pending','On Hold') THEN 1 ELSE 0 END),0) AS PendingCalls
FROM DailySupport;



-- My Work open tickets + latest reassignment reason
SELECT TOP 200 d.*, c.ClientName, u.FullName AS AssignedName,
       hr.Reason AS ReassignReason,
       hr.ChangedByName AS ReassignedBy,
       hr.ChangedOn AS ReassignedOn
FROM DailySupport d
LEFT JOIN Client c ON d.ClientId = c.ClientId
LEFT JOIN Users u ON d.AssignedTo = u.UserId
OUTER APPLY (
    SELECT TOP 1 h.Reason, h.ChangedByName, h.ChangedOn
    FROM DailySupportHistory h
    WHERE h.SupportId = d.SupportId
      AND h.Action = 'Reassigned'
      AND NULLIF(LTRIM(RTRIM(ISNULL(h.Reason,''))),'') IS NOT NULL
    ORDER BY h.HistoryId DESC
) hr
WHERE d.AssignedTo = @M
  AND (d.Status IS NULL OR d.Status <> 'Closed')
ORDER BY CASE LOWER(ISNULL(d.Priority,''))
             WHEN 'urgent' THEN 0 WHEN 'high' THEN 1
             WHEN 'medium' THEN 2 ELSE 3 END,
         d.SupportId DESC;

-- Fallback query when DailySupportHistory/Reason is unavailable
SELECT TOP 200 d.*, c.ClientName, u.FullName AS AssignedName
FROM DailySupport d
LEFT JOIN Client c ON d.ClientId = c.ClientId
LEFT JOIN Users u ON d.AssignedTo = u.UserId
WHERE d.AssignedTo = @M
  AND (d.Status IS NULL OR d.Status <> 'Closed')
ORDER BY CASE LOWER(ISNULL(d.Priority,''))
             WHEN 'urgent' THEN 0 WHEN 'high' THEN 1
             WHEN 'medium' THEN 2 ELSE 3 END,
         d.SupportId DESC;

-- My Work tickets ke latest comment aur comment count
SELECT c.SupportId, c.CommentText, c.CreatedByName, c.CreatedOn, x.Cnt
FROM dbo.DailySupportComment c
INNER JOIN (
    SELECT SupportId, MAX(CommentId) AS LastId, COUNT(*) AS Cnt
    FROM dbo.DailySupportComment
    WHERE SupportId IN (/* current My Work SupportIds */)
    GROUP BY SupportId
) x ON x.LastId = c.CommentId;

-- Mere unread comments
SELECT u.SupportId, u.UnreadCnt, c.CommentText, c.CreatedByName, c.CreatedOn,
       d.TicketNo, d.Status, cl.ClientName
FROM (
    SELECT SupportId, COUNT(*) AS UnreadCnt, MAX(CommentId) AS LastId
    FROM dbo.DailySupportComment
    WHERE ReplyToUserId=@M
      AND ReadOn IS NULL
      AND (CreatedBy IS NULL OR CreatedBy<>@M)
    GROUP BY SupportId
) u
INNER JOIN dbo.DailySupportComment c ON c.CommentId=u.LastId
INNER JOIN DailySupport d ON d.SupportId=u.SupportId
LEFT JOIN Client cl ON d.ClientId=cl.ClientId
ORDER BY u.LastId DESC;



 /* 4. DSS REPORTS / EXPORT*/


-- Report total count
SELECT COUNT(*)
FROM DailySupport d
LEFT JOIN Client c ON d.ClientId=c.ClientId
WHERE 1=1
  /* search/date/status/priority filters */
  /* default: AND (d.Status IS NULL OR d.Status <> 'Closed') */;

-- Report grid
SELECT d.*, c.ClientName AS CompanyName,
       u.FullName AS AssignedToName,
       u.FullName AS AssignedName
FROM DailySupport d
LEFT JOIN Client c ON d.ClientId=c.ClientId
LEFT JOIN Users u ON d.AssignedTo=u.UserId
WHERE 1=1
  /* search/date/status/priority filters */
ORDER BY d.SupportId DESC
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;

-- Report Excel export
SELECT d.TicketNo, d.SupportDate AS [DateTime],
       c.ClientName AS Customer, d.Mobile, d.CallerName,
       d.Issue, d.Priority, d.Status,
       u.FullName AS AssignedTo, d.CreatedOn
FROM DailySupport d
LEFT JOIN Client c ON d.ClientId=c.ClientId
LEFT JOIN Users u ON d.AssignedTo=u.UserId
WHERE 1=1
  /* search/date/status/priority filters */
ORDER BY d.SupportId DESC;


/*
   5. CREATE / EDIT / VIEW DETAILS */

-- Create se pehle duplicate TicketNo check
SELECT COUNT(*)
FROM DailySupport
WHERE TicketNo=@T;

-- Edit/View ke liye ticket record
SELECT *
FROM DailySupport
WHERE SupportId=@Id;

-- View details: ticket + assigned user
SELECT d.*, c.ClientName AS CompanyName,
       u.FullName AS AssignedToName
FROM DailySupport d
LEFT JOIN Client c ON d.ClientId=c.ClientId
LEFT JOIN Users u ON d.AssignedTo=u.UserId
WHERE d.SupportId=@Id;

-- View details me mobile se client resolve
SELECT TOP 1 ClientId
FROM Client
WHERE MobileNo LIKE @M
ORDER BY ClientId;

-- Selected ticket ka short history
SELECT TicketNo, SupportDate, Issue, Status, Priority
FROM DailySupport
WHERE SupportId=@Id;

-- Client ki latest 10 calls
SELECT TOP 10 TicketNo, SupportDate, Issue, Status, Priority
FROM DailySupport
WHERE ClientId=@Cid
ORDER BY SupportId DESC;

-- Client ki complete calls
SELECT d.SupportId, d.TicketNo, d.SupportDate, d.Issue, d.Status,
       d.Priority, d.Category, d.CreatedOn,
       u.FullName AS AssignedToName
FROM DailySupport d
LEFT JOIN Users u ON d.AssignedTo=u.UserId
WHERE d.ClientId=@Cid
ORDER BY d.SupportId DESC;

-- Client ki audit history
SELECT h.HistoryId, h.SupportId, d.TicketNo, h.Action, h.FieldName,
       h.OldValue, h.NewValue, h.ChangedByName, h.ChangedOn, h.Reason
FROM DailySupportHistory h
INNER JOIN DailySupport d ON d.SupportId=h.SupportId
WHERE d.ClientId=@Cid
ORDER BY h.ChangedOn, h.HistoryId;

-- Selected ticket ka audit history
SELECT h.HistoryId, h.Action, h.FieldName, h.OldValue, h.NewValue,
       ISNULL(NULLIF(LTRIM(RTRIM(h.ChangedByName)),''),u.FullName) AS ChangedByName,
       h.ChangedOn, h.Reason
FROM DailySupportHistory h
LEFT JOIN Users u ON h.ChangedBy=u.UserId
WHERE h.SupportId=@Id
ORDER BY h.HistoryId;




-- Grid ke Re-Assign dropdown ke liye
SELECT UserId, FullName
FROM Users
ORDER BY FullName;

-- Re-Assign se pehle current assignment
SELECT AssignedTo, TicketNo
FROM DailySupport
WHERE SupportId=@Id;

-- Re-Assign update
UPDATE DailySupport
SET AssignedTo=@A
WHERE SupportId=@Id;

-- Status change se pehle current status
SELECT TicketNo, ISNULL(Status,'Open')
FROM DailySupport
WHERE SupportId=@Id;

-- Status update
UPDATE DailySupport
SET Status=@Status
WHERE SupportId=@Id;


/* 7. ACCESS / COMMENT  */

-- User ko ticket access hai ya nahi
SELECT COUNT(*)
FROM DailySupport
WHERE SupportId=@Id
  AND (@CanAssign=1 OR AssignedTo=@UserId);

-- Ticket comments + assigned user
SELECT d.AssignedTo, u.FullName AS AssignedName,
       c.CommentId, c.SupportId, c.CommentText,
       c.ReplyToUserId, c.ReplyToName,
       c.CreatedBy, c.CreatedByName, c.CreatedOn
FROM DailySupport d
LEFT JOIN Users u ON d.AssignedTo=u.UserId
LEFT JOIN dbo.DailySupportComment c ON c.SupportId=d.SupportId
WHERE d.SupportId=@Id
ORDER BY c.CommentId;

-- Ticket open karne par current user's unread comments read mark
UPDATE dbo.DailySupportComment
SET ReadOn=GETDATE()
WHERE SupportId=@Id
  AND ReplyToUserId=@M
  AND ReadOn IS NULL
  AND (CreatedBy IS NULL OR CreatedBy<>@M);

-- Comment add karne se pehle assigned user resolve
SELECT d.AssignedTo, u.FullName
FROM DailySupport d
LEFT JOIN Users u ON d.AssignedTo=u.UserId
WHERE d.SupportId=@Id;

-- New comment insert
INSERT INTO dbo.DailySupportComment
    (SupportId, CommentText, ReplyToUserId, ReplyToName, CreatedBy, CreatedByName)
VALUES
    (@Sid, @Txt, @Rt, @Rn, @By, @ByName);

-- Inserted comment id
SELECT CAST(SCOPE_IDENTITY() AS INT);


/* 8. MOBILE LOOKUP*/

SELECT TOP 1 *
FROM Client
WHERE MobileNo LIKE @M;

