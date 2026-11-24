-- A backup restored under another name and checked, without touching the live database (docs/runbook.md section 15).
-- Run as the administrator with sqlcmd: -v Database=BillingRecon BackupFile=<path> Target=BillingRecon_Restored
-- DataDir=<a folder of the SQL Server's own disk, no trailing slash>. Drop the target afterwards.
RESTORE DATABASE [$(Target)] FROM DISK = N'$(BackupFile)' WITH CHECKSUM,
    MOVE N'$(Database)' TO N'$(DataDir)/$(Target).mdf',
    MOVE N'$(Database)_log' TO N'$(DataDir)/$(Target)_log.ldf';
GO
DBCC CHECKDB ([$(Target)]) WITH NO_INFOMSGS;
GO
USE [$(Target)];
SELECT COUNT(*) AS Migrations, MAX(MigrationId) AS LastMigration FROM dbo.__EFMigrationsHistory;
SELECT (SELECT COUNT(*) FROM dbo.Bills) AS Bills, (SELECT COUNT(*) FROM dbo.Payments) AS Payments,
       (SELECT COUNT(*) FROM dbo.LedgerPostings) AS LedgerPostings, (SELECT COUNT(*) FROM dbo.AuditRecords) AS AuditRecords;
GO
