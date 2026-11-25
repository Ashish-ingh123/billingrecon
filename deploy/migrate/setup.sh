#!/bin/bash
# The migration step (ADR 0045 point 3): the logins, the migrations as the migrator, the application's role. Every step
# can run again: a second `docker compose up` changes nothing that is already there.
set -euo pipefail

sqlcmd=/opt/mssql-tools18/bin/sqlcmd
server="${SQL_SERVER:-sql}"

# In production (ADR 0055) the three passwords are files of the host's secret store, not variables.
if [ -n "${SECRETS_DIR:-}" ]; then
    MSSQL_SA_PASSWORD="$(cat "$SECRETS_DIR/mssql_sa_password")"
    MIGRATOR_PASSWORD="$(cat "$SECRETS_DIR/migrator_password")"
    APP_PASSWORD="$(cat "$SECRETS_DIR/app_password")"
fi

echo "Database and logins, as the administrator"
SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" "$sqlcmd" -S "$server" -U sa -C -b -i /migrate/logins.sql \
    -v MigratorPassword="$MIGRATOR_PASSWORD" AppPassword="$APP_PASSWORD"

echo "Migrations, as billingrecon_migrator"
SQLCMDPASSWORD="$MIGRATOR_PASSWORD" "$sqlcmd" -S "$server" -d BillingRecon -U billingrecon_migrator -C -b -I -i /migrate/migrate.sql

echo "The application's role, as the administrator"
SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" "$sqlcmd" -S "$server" -d BillingRecon -U sa -C -b -i /migrate/membership.sql

echo "The database is ready."
