-- =========================================================
--  CLIENT (Company Master) — upgrades
--  Safe script: jitni baar chalao, koi problem nahi.
-- =========================================================

IF OBJECT_ID(N'dbo.Client', N'U') IS NOT NULL
BEGIN
    -- Contact Person Name — "Suntec me kaun baat karta hai (Rahul / Amit)"
    IF COL_LENGTH('dbo.Client', 'ContactPerson') IS NULL
        ALTER TABLE dbo.Client ADD ContactPerson NVARCHAR(150) NULL;
END
GO

-- =========================================================
--  Company name duplicate na bane (unique index)
--  Index sirf tab banao jab koi duplicate ClientName na ho
--  (warna index creation fail karega — isliye pehle check).
-- =========================================================
IF OBJECT_ID(N'dbo.Client', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Client_ClientName' AND object_id = OBJECT_ID('dbo.Client'))
   AND NOT EXISTS
   (
       SELECT LTRIM(RTRIM(ClientName))
       FROM dbo.Client
       WHERE ClientName IS NOT NULL
       GROUP BY LTRIM(RTRIM(ClientName))
       HAVING COUNT(*) > 1
   )
BEGIN
    CREATE UNIQUE INDEX UX_Client_ClientName ON dbo.Client(ClientName);
END
GO
