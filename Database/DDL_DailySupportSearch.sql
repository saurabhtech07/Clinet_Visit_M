/*
   Daily Support search optimization
   ----------------------------------
   Run this script once on the application's SQL Server database.
   It does not change any table data. It only creates Full-Text Search
   metadata/indexes when they do not already exist.

   After this script is deployed, the controller can use CONTAINS for
   Issue/ClientName searches while retaining LIKE for TicketNo/Mobile.
*/

IF FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') <> 1
BEGIN
    THROW 51000, 'SQL Server Full-Text Search is not installed on this instance.', 1;
END;

IF OBJECT_ID(N'dbo.DailySupport', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM sys.fulltext_catalogs
        WHERE name = N'DailySupportSearchCatalog'
    )
    BEGIN
        CREATE FULLTEXT CATALOG DailySupportSearchCatalog AS DEFAULT;
    END;

    DECLARE @DailySupportKeyIndex sysname;
    SELECT TOP (1) @DailySupportKeyIndex = i.name
    FROM sys.indexes AS i
    WHERE i.object_id = OBJECT_ID(N'dbo.DailySupport')
      AND i.is_primary_key = 1;

    IF @DailySupportKeyIndex IS NULL
    BEGIN
        THROW 51001, 'DailySupport must have a primary key before creating Full-Text Search.', 1;
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.fulltext_indexes
        WHERE object_id = OBJECT_ID(N'dbo.DailySupport')
    )
    BEGIN
        DECLARE @DailySupportSql nvarchar(max) =
            N'CREATE FULLTEXT INDEX ON dbo.DailySupport '
          + N'(Issue LANGUAGE 1033, TicketNo LANGUAGE 1033, Mobile LANGUAGE 1033) '
          + N'KEY INDEX ' + QUOTENAME(@DailySupportKeyIndex)
          + N' ON DailySupportSearchCatalog WITH CHANGE_TRACKING AUTO;';
        EXEC sys.sp_executesql @DailySupportSql;
    END;
END;
ELSE
BEGIN
    THROW 51002, 'Table dbo.DailySupport was not found.', 1;
END;

IF OBJECT_ID(N'dbo.Client', N'U') IS NOT NULL
BEGIN
    DECLARE @ClientKeyIndex sysname;
    SELECT TOP (1) @ClientKeyIndex = i.name
    FROM sys.indexes AS i
    WHERE i.object_id = OBJECT_ID(N'dbo.Client')
      AND i.is_primary_key = 1;

    IF @ClientKeyIndex IS NULL
    BEGIN
        THROW 51003, 'Client must have a primary key before creating Full-Text Search.', 1;
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.fulltext_indexes
        WHERE object_id = OBJECT_ID(N'dbo.Client')
    )
    BEGIN
        DECLARE @ClientSql nvarchar(max) =
            N'CREATE FULLTEXT INDEX ON dbo.Client '
          + N'(ClientName LANGUAGE 1033) '
          + N'KEY INDEX ' + QUOTENAME(@ClientKeyIndex)
          + N' ON DailySupportSearchCatalog WITH CHANGE_TRACKING AUTO;';
        EXEC sys.sp_executesql @ClientSql;
    END;
END;
ELSE
BEGIN
    THROW 51004, 'Table dbo.Client was not found.', 1;
END;

PRINT 'Daily Support Full-Text Search setup completed.';
