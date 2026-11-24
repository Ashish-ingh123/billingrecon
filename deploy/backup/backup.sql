-- A full backup of the application database, checked after it is written (docs/runbook.md section 15). Run as the
-- administrator with sqlcmd: -v Database=BillingRecon BackupFile=<path on the SQL Server's own disk>.
BACKUP DATABASE [$(Database)] TO DISK = N'$(BackupFile)' WITH CHECKSUM, INIT;
GO
RESTORE VERIFYONLY FROM DISK = N'$(BackupFile)' WITH CHECKSUM;
GO
