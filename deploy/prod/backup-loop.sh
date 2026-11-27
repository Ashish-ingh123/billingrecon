#!/bin/bash
# The backups of production (ADR 0055, docs/runbook.md section 19): the log every 15 minutes, a full backup once a day
# at BACKUP_FULL_HOUR_UTC, each checked after it is written, kept BACKUP_RETENTION_DAYS on the server. The database is
# encrypted by TDE, so every backup of it is encrypted with the same certificate. SQL Server writes the files itself,
# into the volume it shares with this container. The off-site copy is the `offsite` service's.
# For the watchdog (ADR 0056) it writes into /ops the instant of the last verified full backup and the use of the
# transaction log, which only a container with sqlcmd can read.
set -uo pipefail

sqlcmd=/opt/mssql-tools18/bin/sqlcmd
dir=/var/opt/backups
ops=/ops
mkdir -p "$ops"
last_full=""

run() {
    SQLCMDPASSWORD="$(cat /run/secrets/mssql_sa_password)" "$sqlcmd" -S sql -U sa -C -b -Q "$1"
}

while true; do
    stamp="$(date -u +%Y%m%dT%H%M%SZ)"
    today="$(date -u +%Y%m%d)"
    hour="$(date -u +%-H)"

    # The first round after a start, and the first round of the hour of the day: a full backup.
    if [ "$last_full" != "$today" ] && { [ -z "$last_full" ] || [ "$hour" -ge "${BACKUP_FULL_HOUR_UTC:-20}" ]; }; then
        file="$dir/full/BillingRecon-$stamp.bak"
        if run "BACKUP DATABASE [BillingRecon] TO DISK = N'$file' WITH CHECKSUM, COMPRESSION, MAXTRANSFERSIZE = 131072, INIT; RESTORE VERIFYONLY FROM DISK = N'$file' WITH CHECKSUM;"; then
            last_full="$today"
            date -u +%s > "$ops/backup-last-ok"
            echo "$stamp full backup written and verified: $file"
        else
            echo "$stamp FULL BACKUP FAILED" >&2
        fi
    else
        file="$dir/log/BillingRecon-$stamp.trn"
        if run "BACKUP LOG [BillingRecon] TO DISK = N'$file' WITH CHECKSUM, COMPRESSION, MAXTRANSFERSIZE = 131072, INIT;"; then
            echo "$stamp log backup written: $file"
        else
            echo "$stamp LOG BACKUP FAILED" >&2
        fi
    fi

    # How much of the transaction log is used: near full, and the database stops taking postings.
    SQLCMDPASSWORD="$(cat /run/secrets/mssql_sa_password)" "$sqlcmd" -S sql -U sa -C -b -d BillingRecon -h -1 -W \
        -Q "SET NOCOUNT ON; SELECT CAST(used_log_space_in_percent AS int) FROM sys.dm_db_log_space_usage;" \
        > "$ops/db-log-used-percent.new" 2>/dev/null \
        && mv "$ops/db-log-used-percent.new" "$ops/db-log-used-percent"

    find "$dir/full" "$dir/log" -type f -mtime "+${BACKUP_RETENTION_DAYS:-35}" -delete 2>/dev/null
    sleep 900
done
