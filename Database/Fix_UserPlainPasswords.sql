/* =============================================================
   Fix_UserPlainPasswords.sql
   -------------------------------------------------------------
   Plain password system ke liye:
   1) Users jinka PasswordHash bcrypt format ka hai (ya kharab hai)
      unke passwords reset hote hain -> PasswordHash = Username
   2) Username 'rupesh' ke liye password = 'Rupesh' (DB jaisa)

   Ye script IDEMPOTENT hai - ek se zyada baar chalao to koi dikkat nahi.
   Pehle rakhne wale cases ke liye bhi preview SELECT diya hai.

   LIVE DB pe chalane se PEHLE preview SELECT zaroor chalao.
   ============================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

/* ---- PREVIEW: kya kya badlega (kuch bhi UPDATE se pehle isse dekho) ---- */
SELECT  UserId, Username, IsActive,
        CASE
            WHEN PasswordHash IS NULL              THEN '<NULL>'
            WHEN PasswordHash = ''                 THEN '<EMPTY>'
            WHEN PasswordHash LIKE '$2%'           THEN 'BCRYPT -> reset to Username'
            WHEN PasswordHash <> Username          THEN 'MISMATCH -> reset to Username'
            ELSE 'OK (already username)'
        END AS ActionTaken,
        PasswordHash AS OldValue
FROM    dbo.Users
ORDER BY UserId;

/* ---- UPDATE: kharab / bcrypt wale passwords ko Username bana do ----
   Condition: hash bcrypt format ka ho, NULL/empty ho, ya username se alag ho.
   (Jo already PasswordHash = Username hai, wo dobara update nahi hoga.) */
UPDATE u
SET    u.PasswordHash = u.Username
FROM   dbo.Users u
WHERE  u.PasswordHash IS NULL
   OR  u.PasswordHash = ''
   OR  u.PasswordHash LIKE '$2%'
   OR  u.PasswordHash <> u.Username;

/* ---- VERIFY: result ---- */
SELECT  UserId, Username, PasswordHash,
        CASE WHEN PasswordHash = Username
             THEN 'OK - password = username'
             ELSE 'CHECK NEEDED'
        END AS Status
FROM    dbo.Users
ORDER BY UserId;

COMMIT TRANSACTION;

PRINT 'Done: all user passwords are now plain text (= Username).';
