#!/bin/bash
# The migration step (ADR 0045 point 3): the logins, the migrations as the migrator, the application's role. Every step
# can run again: a second `docker compose up` changes nothing that is already there.
#
# With AZURE_SQL=true the same three steps run against Azure SQL Database (the free hosting of docs/runbook.md): the
# database is the one made in the portal, the server's administrator (SQL_ADMIN_USER, its password in
# MSSQL_SA_PASSWORD) takes the place of sa, contained-users.sql the place of logins.sql, every step connects to the
# database itself, and the server's certificate is checked instead of trusted as it is.
set -euo pipefail

sqlcmd="${SQLCMD:-/opt/mssql-tools18/bin/sqlcmd}"
scripts="${MIGRATE_DIR:-/migrate}"
server="${SQL_SERVER:-sql}"
admin="${SQL_ADMIN_USER:-sa}"

# In production (ADR 0055) the three passwords are files of the host's secret store, not variables.
if [ -n "${SECRETS_DIR:-}" ]; then
    MSSQL_SA_PASSWORD="$(cat "$SECRETS_DIR/mssql_sa_password")"
    MIGRATOR_PASSWORD="$(cat "$SECRETS_DIR/migrator_password")"
    APP_PASSWORD="$(cat "$SECRETS_DIR/app_password")"
fi

if [ "${AZURE_SQL:-false}" = "true" ]; then
    if [ "$admin" = "sa" ]; then
        echo "AZURE_SQL=true needs SQL_ADMIN_USER, the administrator named when the Azure SQL server was made." >&2
        exit 2
    fi

    echo "Users of the database, as the server's administrator"
    SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" "$sqlcmd" -S "$server" -d BillingRecon -U "$admin" -b -i "$scripts/contained-users.sql" \
        -v MigratorPassword="$MIGRATOR_PASSWORD" AppPassword="$APP_PASSWORD"

    echo "Migrations, as billingrecon_migrator"
    SQLCMDPASSWORD="$MIGRATOR_PASSWORD" "$sqlcmd" -S "$server" -d BillingRecon -U billingrecon_migrator -b -I -i "$scripts/migrate.sql"

    echo "The application's role, as the server's administrator"
    SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" "$sqlcmd" -S "$server" -d BillingRecon -U "$admin" -b -i "$scripts/membership.sql"

    echo "The database is ready."
    exit 0
fi

echo "Database and logins, as the administrator"
SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" "$sqlcmd" -S "$server" -U "$admin" -C -b -i "$scripts/logins.sql" \
    -v MigratorPassword="$MIGRATOR_PASSWORD" AppPassword="$APP_PASSWORD"

echo "Migrations, as billingrecon_migrator"
SQLCMDPASSWORD="$MIGRATOR_PASSWORD" "$sqlcmd" -S "$server" -d BillingRecon -U billingrecon_migrator -C -b -I -i "$scripts/migrate.sql"

echo "The application's role, as the administrator"
SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" "$sqlcmd" -S "$server" -d BillingRecon -U "$admin" -C -b -i "$scripts/membership.sql"

echo "The database is ready."
