-- Step 1 on Azure SQL Database (setup.sh with AZURE_SQL=true), in place of logins.sql. Azure SQL has no sa, and from the
-- application's database there is no CREATE DATABASE, no CREATE LOGIN and no USE: the database is made in the portal,
-- and the migrator and the application are users of the database itself with their own passwords (contained users),
-- in the same roles as the logins of logins.sql. Run as the server's administrator, connected to that database; safe
-- to run at every start.
IF (SELECT is_read_committed_snapshot_on FROM sys.databases WHERE database_id = DB_ID()) = 0
    ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON;
GO
IF USER_ID(N'billingrecon_migrator') IS NULL
    CREATE USER [billingrecon_migrator] WITH PASSWORD = N'$(MigratorPassword)';
ELSE
    ALTER USER [billingrecon_migrator] WITH PASSWORD = N'$(MigratorPassword)';
IF USER_ID(N'billingrecon_app') IS NULL
    CREATE USER [billingrecon_app] WITH PASSWORD = N'$(AppPassword)';
ELSE
    ALTER USER [billingrecon_app] WITH PASSWORD = N'$(AppPassword)';
GO
IF IS_ROLEMEMBER(N'db_ddladmin', N'billingrecon_migrator') = 0 ALTER ROLE [db_ddladmin] ADD MEMBER [billingrecon_migrator];
IF IS_ROLEMEMBER(N'db_datareader', N'billingrecon_migrator') = 0 ALTER ROLE [db_datareader] ADD MEMBER [billingrecon_migrator];
IF IS_ROLEMEMBER(N'db_datawriter', N'billingrecon_migrator') = 0 ALTER ROLE [db_datawriter] ADD MEMBER [billingrecon_migrator];
IF IS_ROLEMEMBER(N'db_securityadmin', N'billingrecon_migrator') = 0 ALTER ROLE [db_securityadmin] ADD MEMBER [billingrecon_migrator];
GO
