-- Once per environment, as the administrator, after the first `migrate` (docs/runbook.md section 19): encryption at
-- rest by SQL Server (TDE, Standard edition of SQL Server 2022 and above) and the full recovery model, which the log
-- backups need. Safe to run again.
--   sqlcmd -v CertificatePassword=<the password of the certificate's backup, from the operator's vault>
-- THE CERTIFICATE'S BACKUP (two files under /var/opt/backups/tde) MUST LEAVE THE SERVER, with its password: without it
-- no backup of this database can ever be restored on another server. Remove the two files from the server afterwards.
USE [master];
IF NOT EXISTS (SELECT 1 FROM sys.symmetric_keys WHERE name = N'##MS_DatabaseMasterKey##')
    CREATE MASTER KEY ENCRYPTION BY PASSWORD = N'$(CertificatePassword)';
GO
IF NOT EXISTS (SELECT 1 FROM sys.certificates WHERE name = N'BillingReconTde')
BEGIN
    CREATE CERTIFICATE [BillingReconTde] WITH SUBJECT = N'BillingRecon TDE', EXPIRY_DATE = '2036-12-31';
    BACKUP CERTIFICATE [BillingReconTde] TO FILE = N'/var/opt/backups/tde/BillingReconTde.cer'
        WITH PRIVATE KEY (FILE = N'/var/opt/backups/tde/BillingReconTde.key', ENCRYPTION BY PASSWORD = N'$(CertificatePassword)');
END
GO
USE [BillingRecon];
IF NOT EXISTS (SELECT 1 FROM sys.dm_database_encryption_keys WHERE database_id = DB_ID())
    CREATE DATABASE ENCRYPTION KEY WITH ALGORITHM = AES_256 ENCRYPTION BY SERVER CERTIFICATE [BillingReconTde];
GO
ALTER DATABASE [BillingRecon] SET ENCRYPTION ON;
GO
ALTER DATABASE [BillingRecon] SET RECOVERY FULL;
GO
SELECT name, is_encrypted, recovery_model_desc, is_read_committed_snapshot_on FROM sys.databases WHERE name = N'BillingRecon';
GO
