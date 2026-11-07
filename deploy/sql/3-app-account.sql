-- FNRC Service Desk: let the app sign in to its database. First installation only, after 2-database-update.sql.
--
-- Run in SQL Server Management Studio as a SQL Server administrator, with the service desk database (FnrcItsm)
-- selected. The app gets the itsm_app role only: it can read and change data, not tables, and cannot rewrite history.
--
-- How the app signs in:
--   SQL login (as set below): the script creates the login itsm_app_login with a random password and shows that
--     password once, in the results. Copy it into the app's connection string. SQL Server must allow SQL logins
--     ("SQL Server and Windows Authentication mode").
--   Windows account: set @WindowsAccount to the account the IIS application pool runs as, such as a group managed
--     service account N'FNRC\svc-itsm$'. The connection string then has Integrated Security=True and no password.
-- Running it again changes nothing, and never changes an existing login's password.

DECLARE @WindowsAccount sysname = N'';
DECLARE @SqlLogin sysname = N'itsm_app_login';

IF DB_NAME() IN (N'master', N'model', N'msdb', N'tempdb')
    THROW 50000, 'Select the service desk database before running this script.', 1;
IF DATABASE_PRINCIPAL_ID(N'itsm_app') IS NULL
    THROW 50000, 'Run 2-database-update.sql first: it creates the itsm_app role.', 1;

DECLARE @Login sysname = CASE WHEN @WindowsAccount <> N'' THEN @WindowsAccount ELSE @SqlLogin END;
DECLARE @Name nvarchar(300) = QUOTENAME(@Login);
DECLARE @Password nvarchar(128) = NULL;

IF SUSER_ID(@Login) IS NULL
BEGIN
    IF @WindowsAccount <> N''
        EXEC (N'CREATE LOGIN ' + @Name + N' FROM WINDOWS;');
    ELSE
    BEGIN
        IF CAST(SERVERPROPERTY('IsIntegratedSecurityOnly') AS int) = 1
            THROW 50000, 'This SQL Server accepts Windows accounts only. Either allow SQL logins (server Properties > Security > "SQL Server and Windows Authentication mode", then restart SQL Server) or set @WindowsAccount.', 1;
        -- 192 random bits; the prefix adds the upper and lower case letters and symbol the password policy asks for.
        -- No quotes or semicolons, so it can go into a connection string as it is. It never expires: the app
        -- would stop working.
        SET @Password = N'Itsm-' + CONVERT(nvarchar(64), CRYPT_GEN_RANDOM(24), 2);
        DECLARE @Create nvarchar(max) = N'CREATE LOGIN ' + @Name + N' WITH PASSWORD = ' + QUOTENAME(@Password, N'''')
            + N', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF, DEFAULT_DATABASE = ' + QUOTENAME(DB_NAME()) + N';';
        EXEC (@Create);
    END;
END;
IF DATABASE_PRINCIPAL_ID(@Login) IS NULL
    EXEC (N'CREATE USER ' + @Name + N' FOR LOGIN ' + @Name + N';');
EXEC (N'ALTER ROLE [itsm_app] ADD MEMBER ' + @Name + N';');

SELECT @Login AS [Login],
       CASE WHEN @WindowsAccount <> N'' THEN N'(none: a Windows account)'
            ELSE ISNULL(@Password, N'(unchanged: the login already existed)') END AS [Password: copy it now, it is not shown again],
       CASE WHEN @WindowsAccount <> N''
            THEN N'Integrated Security=True'
            ELSE N'User ID=' + @Login + N';Password=' + ISNULL(@Password, N'<its password>') END AS [For the connection string];
GO
