-- The restore drill's SQL (deploy/prod/restore-drill.sh): -v BackupFile=<path> Target=<name of the restored copy>.
-- Any failure ends sqlcmd with an error (-b), which fails the drill. The copy is dropped at the end, and at the start
-- if an earlier drill left one.
IF DB_ID(N'$(Target)') IS NOT NULL
BEGIN
    ALTER DATABASE [$(Target)] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$(Target)];
END
GO
RESTORE DATABASE [$(Target)] FROM DISK = N'$(BackupFile)' WITH CHECKSUM,
    MOVE N'BillingRecon' TO N'/var/opt/mssql/data/$(Target).mdf',
    MOVE N'BillingRecon_log' TO N'/var/opt/mssql/data/$(Target)_log.ldf';
GO
DBCC CHECKDB ([$(Target)]) WITH NO_INFOMSGS;
GO
USE [$(Target)];
SELECT COUNT(*) AS Migrations, MAX(MigrationId) AS LastMigration FROM dbo.__EFMigrationsHistory;
SELECT (SELECT COUNT(*) FROM dbo.Bills) AS Bills, (SELECT COUNT(*) FROM dbo.Payments) AS Payments,
       (SELECT COUNT(*) FROM dbo.LedgerPostings) AS LedgerPostings, (SELECT COUNT(*) FROM dbo.AuditRecords) AS AuditRecords;
-- The ledger of the restored copy balances: every rupee debited was credited (BR-401). The twelve identities of the
-- reconciliation report are run against a restored copy by the scheduled reconciliation of Prompt 10.6.
DECLARE @difference decimal(38, 2) = (SELECT COALESCE(SUM(Debit), 0) - COALESCE(SUM(Credit), 0) FROM dbo.LedgerEntries);
IF @difference <> 0
    THROW 51000, N'The ledger of the restored database does not balance.', 1;
SELECT @difference AS LedgerDifference;
GO
USE [master];
ALTER DATABASE [$(Target)] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
DROP DATABASE [$(Target)];
GO
