# syntax=docker/dockerfile:1
# The migration step of the one-command local run (ADR 0045 point 3), as docs/runbook.md section 10 does it by hand:
# as the administrator, the database and the two logins; as billingrecon_migrator, the idempotent script of every
# migration; as the administrator again, the application's login into its role. Build context: the repository root.

ARG SDK_TAG=10.0

# The idempotent script of every migration, written by the repository's own dotnet-ef from the Infrastructure project.
FROM mcr.microsoft.com/dotnet/sdk:${SDK_TAG} AS script
WORKDIR /source
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY .config/ .config/
COPY src/ src/
# dotnet ef reads the project's restore output before it builds (NETSDK1004 without it): restore first.
RUN dotnet tool restore \
    && dotnet restore src/BillingRecon.Infrastructure/BillingRecon.Infrastructure.csproj \
    && dotnet ef migrations script --idempotent --project src/BillingRecon.Infrastructure --output /out/migrate.sql

# sqlcmd comes with the SQL Server image the compose file already pulls.
FROM mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04
COPY --from=script /out/migrate.sql /migrate/migrate.sql
COPY deploy/migrate/logins.sql deploy/migrate/membership.sql deploy/migrate/setup.sh /migrate/
ENTRYPOINT ["/bin/bash", "/migrate/setup.sh"]
