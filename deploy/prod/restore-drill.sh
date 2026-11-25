#!/bin/bash
# The monthly restore drill (ADR 0055, docs/runbook.md section 19): the newest full backup restored under another name
# on the same server, checked page by page, its ledger held to balance, then dropped. The live database is not touched.
#   docker compose -f docker-compose.prod.yml --env-file /etc/billingrecon/env exec backup /bin/bash /prod/restore-drill.sh
# Exit code 0 and the line "RESTORE DRILL PASSED" are the proof; write the date into the drill log of the runbook.
set -euo pipefail

sqlcmd=/opt/mssql-tools18/bin/sqlcmd
target=BillingRecon_Drill
backup="$(ls -1t /var/opt/backups/full/*.bak | head -n 1)"
echo "Restoring $backup as $target"

SQLCMDPASSWORD="$(cat /run/secrets/mssql_sa_password)" "$sqlcmd" -S sql -U sa -C -b -W \
    -v BackupFile="$backup" Target="$target" -i /prod/restore-drill.sql

echo "RESTORE DRILL PASSED: $backup"
