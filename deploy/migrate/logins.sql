-- Step 1 of docs/runbook.md section 10, made safe to run at every start: the database with READ_COMMITTED_SNAPSHOT on,
-- the migrator's and the application's logins with the passwords of .env, and their users. Run as the administrator.
IF DB_ID(N'BillingRecon') IS NULL
    CREATE DATABASE [BillingRecon];
GO
IF (SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = N'BillingRecon') = 0
    ALTER DATABASE [BillingRecon] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
GO
IF SUSER_ID(N'billingrecon_migrator') IS NULL
    CREATE LOGIN [billingrecon_migrator] WITH PASSWORD = N'$(MigratorPassword)';
ELSE
    ALTER LOGIN [billingrecon_migrator] WITH PASSWORD = N'$(MigratorPassword)';
IF SUSER_ID(N'billingrecon_app') IS NULL
    CREATE LOGIN [billingrecon_app] WITH PASSWORD = N'$(AppPassword)';
ELSE
    ALTER LOGIN [billingrecon_app] WITH PASSWORD = N'$(AppPassword)';
GO
USE [BillingRecon];
IF USER_ID(N'billingrecon_migrator') IS NULL
    CREATE USER [billingrecon_migrator] FOR LOGIN [billingrecon_migrator];
IF IS_ROLEMEMBER(N'db_ddladmin', N'billingrecon_migrator') = 0 ALTER ROLE [db_ddladmin] ADD MEMBER [billingrecon_migrator];
IF IS_ROLEMEMBER(N'db_datareader', N'billingrecon_migrator') = 0 ALTER ROLE [db_datareader] ADD MEMBER [billingrecon_migrator];
IF IS_ROLEMEMBER(N'db_datawriter', N'billingrecon_migrator') = 0 ALTER ROLE [db_datawriter] ADD MEMBER [billingrecon_migrator];
IF IS_ROLEMEMBER(N'db_securityadmin', N'billingrecon_migrator') = 0 ALTER ROLE [db_securityadmin] ADD MEMBER [billingrecon_migrator];
IF USER_ID(N'billingrecon_app') IS NULL
    CREATE USER [billingrecon_app] FOR LOGIN [billingrecon_app];
GO
