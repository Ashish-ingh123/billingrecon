# BillingRecon

A multi-tenant billing, payments and accounts-receivable platform for organisations in India,
built as a .NET modular monolith with a React front end. The core promise is ledger-grade
financial correctness: gapless document numbering, immutable postings, maker-checker approval
and an append-only audit trail.

> **Status: skeleton, cross-cutting infrastructure, the persistence foundation and the test infrastructure.** Configuration,
> structured logging, the error contract, health checks and API versioning are in place, and so is
> the EF Core context with its conventions (typed ids, tenant isolation, audit columns) and the
> first table, `Tenants`. There are no use cases yet.
> The move to .NET 10 and the persistence code were built and tested on .NET 10 on 2026-10-17, and
> the first migration, `0001_Tenants`, is in the repository. The test infrastructure of Prompt 1.4
> (SQL Server 2022 in Testcontainers, Respawn, Bogus, the module rule with its exceptions) was written
> the same day; its database tests await their first run on a machine with Docker
> ([CLAUDE.md](CLAUDE.md), section 9).
> Start with [CLAUDE.md](CLAUDE.md) (project memory: stack, architecture, financial rules, working
> agreement) and the design documents in [docs/](docs/).

## Release plan

| Release | Theme |
|---|---|
| R1 | MVP |
| R2 | Reconciliation |
| R3 | Recovery |
| R4 | Insurance / Corporate |
| R5 | Maturity |

## Layout

```
BillingRecon.sln
Directory.Build.props         shared compiler and analyzer settings; the single TargetFramework
Directory.Packages.props      central package management (every package version)
global.json                   pins the .NET SDK
.editorconfig                 code style, enforced at build time
docker-compose.yml            local SQL Server 2022 and Seq
.env.example                  template for .env (licence acceptance, database password, ports)
.config/dotnet-tools.json     local tools (dotnet-ef)
src/
  BannedSymbols.txt             APIs production code must not call (a call is a build error)
  BillingRecon.Domain/          entities, value objects, Result and Error, invariants (no dependencies)
  BillingRecon.Application/     use cases, validators, ports, DTOs
  BillingRecon.Infrastructure/  EF Core context and migrations, port implementations, options, logging, health checks
  BillingRecon.Api/             controllers, error contract, correlation id, health, OpenAPI + Scalar (Development)
tests/
  BillingRecon.Domain.Tests/  BillingRecon.Application.Tests/
  BillingRecon.IntegrationTests/  BillingRecon.ArchitectureTests/
frontend/                     React + TypeScript + Vite: sign-in and the shell (Prompt 7.1, ADR 0038)
docs/                         requirements, domain model, database, API, security, testing, runbook, ADRs
```

Each source project contains one folder per module: Common, Payers, Billing, Payments, Receivables,
Adjustments, Reporting, Identity, Audit.

## Getting started

Prerequisites: a .NET 10 SDK (`global.json` accepts any 10.0.x), Docker Desktop for the local SQL
Server and Seq (and for the database tests to come), Node 22.22 or later for the frontend (docs/runbook.md section 12).

Restore the local tools once per clone (`dotnet-ef`, at the version of the EF Core packages):

```bash
dotnet tool restore
```

Build needs nothing else. The tests need a running Docker engine for the database tests, which start
SQL Server 2022 themselves ([docs/runbook.md](docs/runbook.md), section 4):

```bash
dotnet build
```

```bash
dotnet test
```

To run the API you need a SQL Server and a connection string. Copy `.env.example` to `.env`, fill it
in (you accept the SQL Server and Seq licences there and choose the database password), then start
the containers:

```bash
docker compose up -d
```

Give the API its connection string once, with user-secrets, using the password from your `.env`:

```bash
dotnet user-secrets set "Database:ConnectionString" "Server=localhost,1433;Database=BillingRecon;User Id=sa;Password=<your-sa-password>;TrustServerCertificate=True" --project src/BillingRecon.Api
```

```bash
dotnet run --project src/BillingRecon.Api
```

The API listens on http://localhost:5080 (see `Properties/launchSettings.json`) and refuses to
start without the connection string. In Development it creates the database on first start, applies
the migrations and adds the row of the development tenant; adding a migration and the other database
commands are in
[docs/runbook.md](docs/runbook.md), section 3.

- Health: `/health/live` (the process is up) and `/health/ready` (the database is reachable and
  every migration has been applied).
- Service info: `/api/v1/system/info`. Every API route is under `/api/v1`.
- Patients and payers: `/api/v1/patients` and `/api/v1/payers` (register or create, read, list, correct,
  deactivate, reactivate). A change of an existing record sends its ETag in `If-Match`. The contract is
  in [docs/api.md](docs/api.md).
- Logs: JSON on the console and, in Development, in Seq at http://localhost:5341. Every request has
  a correlation id: send `X-Correlation-Id` (a GUID) or let the API generate one; it comes back in
  the response header and is on every log line of the request.
- Errors: always `application/problem+json` with `errorCode`, `traceId` and `correlationId`.
- The OpenAPI document (`/openapi/v1.json`) and the Scalar UI (`/scalar`) are available in
  Development only.

The compose file was first run on 2026-10-17; the checklist in [docs/runbook.md](docs/runbook.md)
says what to verify, and its results are still to be recorded there.

Continuous integration is a GitHub Actions workflow (`.github/workflows/ci.yml`): format, build, every
test including the database tests, coverage, a vulnerability report, the migration script and the API
container image. What each gate is and how to run it locally is in
[docs/runbook.md](docs/runbook.md), section 5; the image itself is section 6.

## Documentation

- [docs/requirements.md](docs/requirements.md)
- [docs/domain-model.md](docs/domain-model.md)
- [docs/database.md](docs/database.md)
- [docs/api.md](docs/api.md)
- [docs/security.md](docs/security.md)
- [docs/testing-strategy.md](docs/testing-strategy.md)
- [docs/runbook.md](docs/runbook.md)
- [docs/adr/](docs/adr/): Architecture Decision Records, starting with ADR 0000
