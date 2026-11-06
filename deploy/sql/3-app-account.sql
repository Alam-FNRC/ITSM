-- FNRC Service Desk: let the app's account use its database. First installation only, after 2-database-update.sql.
--
-- Run in SQL Server Management Studio as a SQL Server administrator, with the service desk database (FnrcItsm)
-- selected. Set @Account to the account the IIS application pool runs as:
--   a group managed service account, such as N'FNRC\svc-itsm$'  (recommended: no password to keep or rotate)
--   a domain service account,         such as N'FNRC\svc-itsm'
--   the web server's computer account, such as N'FNRC\WEBSTG01$' (pool identity ApplicationPoolIdentity)
-- The account gets the itsm_app role only: it can read and change data, not tables, and cannot rewrite history.

DECLARE @Account sysname = N'FNRC\svc-itsm$';

IF DB_NAME() IN (N'master', N'model', N'msdb', N'tempdb')
    THROW 50000, 'Select the service desk database before running this script.', 1;
IF DATABASE_PRINCIPAL_ID(N'itsm_app') IS NULL
    THROW 50000, 'Run 2-database-update.sql first: it creates the itsm_app role.', 1;

DECLARE @Name nvarchar(300) = QUOTENAME(@Account);
IF SUSER_ID(@Account) IS NULL
    EXEC (N'CREATE LOGIN ' + @Name + N' FROM WINDOWS;');
IF DATABASE_PRINCIPAL_ID(@Account) IS NULL
    EXEC (N'CREATE USER ' + @Name + N' FOR LOGIN ' + @Name + N';');
EXEC (N'ALTER ROLE [itsm_app] ADD MEMBER ' + @Name + N';');
GO

-- Only if the SQL Server cannot use Windows accounts: a SQL login instead. Its password then goes in the
-- connection string (User ID=itsm_app_login;Password=...;) in place of Integrated Security=True.
-- CREATE LOGIN [itsm_app_login] WITH PASSWORD = N'<a long random password>', CHECK_POLICY = ON;
-- CREATE USER [itsm_app_login] FOR LOGIN [itsm_app_login];
-- ALTER ROLE [itsm_app] ADD MEMBER [itsm_app_login];
