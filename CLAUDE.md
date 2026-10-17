# CLAUDE.md — Project memory

This file is the persistent memory for anyone, human or Claude Code, working in this repository.
Read it fully before touching code. When anything here changes, update this file **and** record the
decision as an ADR in `docs/adr/` (see ADR 0000 for the format).

---

## 1. Project

**Purpose.** A multi-tenant billing, payments and accounts-receivable platform for hospitals and
clinics in India. It raises bills and credit notes for patients, records and allocates payments,
keeps a double-entry receivables subledger per payer, applies controlled adjustments and refunds,
and reports on outstanding balances. Its core promise is ledger-grade financial correctness:
gapless numbering, immutable postings, maker-checker approval and a complete audit trail.

**Product name:** **BillingRecon**. Root namespace `BillingRecon`, solution `BillingRecon.sln`.

**Domain and vocabulary (confirmed 2026-09-29, `docs/requirements.md`).** Hospital and clinic
patient billing. A **bill** (never "invoice") is raised *for* a **patient** and *owed by* a
**payer**; in R1 every payer is self-pay (the patient or a guarantor), insurers and corporate
accounts arrive in R4. A **payment** (never "receipt") is money received; an **allocation** applies
it to a bill. Business rules are numbered **BR-nnn** in `docs/requirements.md` §3 and cited from
the domain model, the schema and the tests.

### Release plan

| Release | Theme | Scope (R1 confirmed in `docs/requirements.md` §2; R2–R5 indicative) |
|---|---|---|
| **R1** | MVP | Tenants, users and roles, patients and self-pay payers, service catalogue, bills and credit notes, payments and allocation, reversals, refunds, adjustments, receivables subledger with cached balances, gapless numbering, maker-checker, idempotency, audit, basic reports |
| **R2** | Reconciliation | Bank and payment-gateway statement import, automatic and manual matching, unapplied and suspense cash, reconciliation reports |
| **R3** | Recovery | Ageing, dunning schedules and reminders, promises to pay, disputes, write-off workflow under maker-checker |
| **R4** | Insurance / Corporate | Third-party payers (insurers/TPAs, corporate accounts), claims and pre-authorisation, payer statements, payer-specific terms and pricing |
| **R5** | Maturity | Hardening, performance, observability, backup/DR drills, compliance reporting, extensibility |

---

## 2. Tech stack

### Backend

| Concern | Choice | Notes |
|---|---|---|
| Runtime | **.NET 10 (LTS)** | `Directory.Build.props` holds the single `TargetFramework` (`net10.0`); `global.json` pins the SDK to the **10.0.400** feature band with `latestPatch` (the personal machine's band, read from its build outputs on 2026-10-17). Supported to November 2028. Framework-bound packages (ASP.NET Core, EF Core, `Serilog.AspNetCore`) stay on the same major version. **The office machine has no .NET 10 SDK and cannot build the repository** (dev machine notes below). See ADR 0014, which supersedes ADR 0001. |
| Web API | **ASP.NET Core Web API with controllers** | No minimal APIs. Controllers derive from `ApiControllerBase`, are `sealed`, end in `Controller` (architecture test). Every route is under `/api/v1` (ADR 0013). Every error is an RFC 9457 ProblemDetails (ADR 0011). Health checks at `/health/live` (process is up) and `/health/ready` (the application database is reachable and has no pending migration). |
| OpenAPI | **Built-in OpenAPI + Scalar UI**, Development only | `AddOpenApi`/`MapOpenApi` (document at `/openapi/v1.json`) and `MapScalarApiReference` (`/scalar`), mapped only when the environment is Development. |
| Data | **EF Core 10 + SQL Server** | One context, `BillingReconDbContext`, whose conventions do the repetitive work (ADR 0015, section 5 below). Migrations live in `Infrastructure/Common/Migrations`, are applied at startup in Development only, and are a deployment step elsewhere. `dotnet-ef` is a local tool (`.config/dotnet-tools.json`, `dotnet tool restore`) at the same version as the EF Core packages; it uses the Infrastructure project and its design-time factory, never the API. SQL Server 2022 in Docker for dev and tests. |
| Banned APIs | **Microsoft.CodeAnalysis.BannedApiAnalyzers** | `src/BannedSymbols.txt` lists what production code must not call (system time and the machine's time zone, `IgnoreQueryFilters`, `EntityEntry.Reload`/`GetDatabaseValues`, `ExecuteUpdate`/`ExecuteDelete`); a call is build error RS0030. Tests guard the list and where the rule may be suppressed (ADR 0015). |
| Validation | **FluentValidation** | One validator per command / request DTO, run in the Application layer. |
| Logging | **Serilog** | Compact JSON on the console, plus Seq when `Seq:ServerUrl` is set. Every event of a request carries CorrelationId (from `X-Correlation-Id` or generated, BR-703), and TenantId and UserId when the request is authenticated. Phone numbers and e-mail addresses are masked before any sink (ADR 0012). |
| Configuration | **appsettings + environment variables + user-secrets** | One strongly typed options class per section, validated at startup: the API refuses to start when misconfigured. Secrets (`Database:ConnectionString`, `Seq:ApiKey`) come from user-secrets or environment variables, never from a file in git. |

### Testing

| Tool | Role |
|---|---|
| **xUnit** | Test framework |
| **Shouldly** | Assertions (`x.ShouldBe(y)`) |
| **NSubstitute** | Mocks and stubs for ports |
| **Testcontainers.MsSql** | Real SQL Server 2022 in Docker, one container per test run, for the `Database` collection of the integration tests (ADR 0016) |
| **Respawn** | Empties the test databases between tests; schema and migrations history stay (ADR 0016) |
| **NetArchTest** | Enforce the dependency and module rules in `tests/BillingRecon.ArchitectureTests` |
| **Bogus** | Realistic test data from one seeded generator (`TestData`) |

### Frontend

React + TypeScript + Vite in `frontend/`.

### Delivery

Docker Compose for local runs: `sql` (SQL Server 2022 Developer) and `seq` exist in `docker-compose.yml`; `api`
and `frontend` join later. Licence acceptance and the SQL Server password come from `.env` (copy `.env.example`),
which git ignores. The API has a multi-stage image, `src/BillingRecon.Api/Dockerfile` (Debian-based runtime with
time zone data, non-root user, health check; ADR 0017). GitHub Actions, `.github/workflows/ci.yml`, runs on every
push to `main` and every pull request: restore with the NuGet audit, `dotnet format --verify-no-changes`, build
with warnings as errors, every test including the database tests (the runners have Docker), coverage and a
vulnerability report as artifacts, the idempotent migration script, and the image built and smoke-run.
Dependabot keeps NuGet and the actions current (`.github/dependabot.yml`). Line endings are LF everywhere
(`.gitattributes`, `.editorconfig`), CRLF only for `.sln`, `.ps1`, `.cmd` and `.bat`. Commands and the gates are
in `docs/runbook.md` sections 5 and 6.

### Banned for now (lifting any of these needs an ADR)

- **MediatR**: use-case handlers are plain classes injected directly into controllers.
- **AutoMapper**: mapping is explicit, hand-written code.
- **FluentAssertions**: use Shouldly.
- **EF Core InMemory provider for tests**: integration tests hit real SQL Server via Testcontainers.
- **Microservices**, **message brokers**, **distributed caches**: this is a modular monolith with one database and in-process calls.

### Dev machine notes (recorded 2026-09-26)

There are two machines. The notes below describe the owner's **office machine**, where the code up to and
including the infrastructure step was written. The owner's **personal machine** has the .NET 10 SDK and Docker
Desktop (per the owner, 2026-10-03) and is where the project is run and verified.

Personal machine, recorded 2026-10-17 from the build outputs it produced and the owner's report:

- .NET SDK feature band **10.0.4xx** (`SdkAnalysisLevel` 10.0.400 in the build outputs; the exact patch and the
  output of `dotnet --list-sdks` are still to be recorded). `global.json` is pinned to that band.
- Docker Desktop, with the compose containers set up (per the owner; the checklist results are not recorded yet).
- First build and test run on .NET 10 on 2026-10-17: clean build, 493 tests green (1 Application, 79 Domain,
  74 Architecture, 339 Integration).
- Node and Docker versions: still to record.

Office machine:

- `dotnet --list-sdks`: 5.0.207, 8.0.405, 9.0.102.
- `node --version`: **v6.10.1**. Far too old for Vite, which needs Node 20.19+ or 22.12+. Install a current Node LTS before any frontend work.
- Git 2.54.0.windows.1. Default branch `main`.
- **No Docker on this machine (the owner's office machine), and none will be installed** (recorded 2026-10-03).
  Anything that needs containers is run and verified on the owner's personal machine, which has Docker Desktop:
  `docker-compose.yml` (checklist in `docs/runbook.md`) and the `Database` collection of the integration tests
  (Testcontainers, ADR 0016), which fails here with one message and runs there. A test that needs Docker belongs
  to that collection and nowhere else, and its first run is on the personal machine.
- SQL Server LocalDB 2019 is installed here (`Server=(localdb)\MSSQLLocalDB;Integrated Security=true`): enough
  for a manual check in the .NET 9 copy (apply a throwaway migration, inspect the tables, run the context
  against a real database), not a substitute for SQL Server 2022. LocalDB 2022 is not installed: its installer
  needs a licence acceptance, which is the owner's to give.
- **No .NET 10 SDK on this machine either, and the owner decided not to add it** (recorded 2026-10-03): it is
  installed on the owner's personal machine, which is where the project is run. The repository targets .NET 10
  (ADR 0014), so **`dotnet build` and `dotnet test` do not work here**. Code written here is pre-checked in a
  throwaway copy moved down to .NET 9 (target framework `net9.0`, ASP.NET Core 9.0.20 and EF Core 9.0.1 packages
  from the local NuGet cache). That shows the code compiles and the tests pass on .NET 9; it does not show the
  C# 14 compiler, the .NET 10 analyzers, or behaviour that differs in ASP.NET Core 10 and EF Core 10. Say
  "written, pre-checked on .NET 9, not built on .NET 10" until the owner has run it on the personal machine.
- There is no Claude Code on the personal machine. The owner copies the project there, runs the commands and
  sends the output back; keep the commands for that machine exact and few.

---

## 3. Architecture

**Style:** modular monolith (ADR 0003). One deployable API, one SQL Server database, modules
isolated by folder and namespace, isolation enforced by architecture tests.

### Projects

```
BillingRecon.sln
Directory.Build.props          Shared compiler/analyzer settings and the single TargetFramework (ADR 0002).
Directory.Packages.props       Central package management: every package version lives here.
global.json                    Pins the .NET 10 SDK feature band (10.0.400, latestPatch; ADR 0014).
.config/dotnet-tools.json      Local tools: dotnet-ef, same version as the EF Core packages. Restore with dotnet tool restore.
.editorconfig                  Code style and naming, enforced in the build (EnforceCodeStyleInBuild).
docker-compose.yml             Local infrastructure: SQL Server 2022 Developer and Seq (first run 2026-10-17; checklist results pending, see section 9).
.env.example                   Template for .env (licence acceptance, sa password, ports). .env itself is git-ignored.
.dockerignore                  What the API image build never sees: build output, tests, docs, .env, the git history.
.github/workflows/ci.yml       GitHub Actions: format, build, tests with Docker, coverage, vulnerability report,
                               migration script, API image built and smoke-run (ADR 0017).
.github/dependabot.yml         Weekly NuGet and GitHub Actions update pull requests.
src/
  Directory.Build.props         Production-code settings: the banned-API analyzer and its list.
  BannedSymbols.txt             APIs production code must not call (RS0030 is a build error; ADR 0015).
  BillingRecon.Domain/          Entities, value objects (Money, DocumentNumber, FiscalYear, TenantId), Result and
                                Error (Common, ADR 0011), domain events, invariants, domain services. No I/O, no
                                project or package references. Today: the base types (Common: Entity<TId>,
                                AggregateRoot<TId>, ITypedId, ITenantScoped, the two audit interfaces,
                                IHasRowVersion, TenantId, UserId), the value objects Money, UnitPrice, Quantity
                                and Percent (Common, ADR 0004), and the Tenant aggregate with IdentityErrors (Identity).
  BillingRecon.Application/     Use cases (command/query handlers as plain classes), FluentValidation validators,
                                port interfaces (IClock, IUnitOfWork, repositories, INumberSeries,
                                IIdempotencyStore, ITenantContext, ICurrentUser), DTOs and explicit mappers.
                                Today: IClock, ITenantContext, ICurrentUser (Common).
  BillingRecon.Infrastructure/  EF Core DbContext, entity configurations, migrations, port implementations,
                                ledger posting, number series, idempotency store, audit writer, JWT wiring. Today:
                                typed options (Common/Settings), the Serilog pipeline and PII mask (Common/Logging),
                                the context with its conventions, interceptors, startup step, design-time
                                factory and the value-object converters (Common/Persistence), SystemClock (Common/Time), the database health
                                check (Common/Health), the identities and the Tenants table (Identity),
                                AddInfrastructure. Migrations go to Common/Migrations.
  BillingRecon.Api/             Controllers, middleware and filters (correlation id, idempotency, tenant resolution),
                                the error contract (Common/Errors: exception handler, Error to ProblemDetails mapper),
                                authentication/authorisation, OpenAPI + Scalar, Program.cs (composition root),
                                and the Dockerfile of the API image (ADR 0017).
tests/
  Directory.Build.props         Test defaults: xUnit, Shouldly, test SDK/runner, global usings.
  BillingRecon.Domain.Tests/  BillingRecon.Application.Tests/
  BillingRecon.IntegrationTests/   In-process API host (WebApplicationFactory, Production environment by default)
                                   with a test-only ProbeController, a log-capturing sink and a stub Seq endpoint.
                                   Persistence/: the context, its conventions and interceptors, tested without a
                                   database (SQL Server provider, a connection string nothing listens on, probe
                                   entities in a derived context). Database/: the `Database` collection on a real
                                   SQL Server 2022 (one Testcontainers container per run, a migrated and a probe
                                   database, Respawn resets; ADR 0016) and DatabaseApiFactory, which hosts the API
                                   against it acting for a tenant. Support/: FakeClock, FixedIdentity,
                                   TestRequestIdentity, the CreateClientFor helper, the Bogus-based TestData.
  BillingRecon.ArchitectureTests/  NetArchTest and file scans: layer rule, module rule with its listed exceptions,
                                   every type in a known module, banned packages in every project file, controller
                                   conventions, no minimal APIs; and the guard of the banned-API list
                                   (BannedSymbolsTests).
frontend/                      React + TypeScript + Vite (planned)
docs/                          Design documents and ADRs (see section 7)
```

Each source project has `AssemblyMarker` (Domain, Application, Infrastructure) or `ApiControllerBase`
(Api) as its anchor type for architecture tests and assembly scanning.

### Modules (a folder and namespace inside every project)

`Common` · `Payers` · `Billing` · `Payments` · `Receivables` · `Adjustments` · `Reporting` · `Identity` · `Audit`

Namespace pattern: `BillingRecon.<Layer>.<Module>`, for example `BillingRecon.Domain.Billing`,
`BillingRecon.Application.Payments`, `BillingRecon.Infrastructure.Receivables`. The folder must match
the namespace (IDE0130 is a build error). Every module folder that has no code yet holds a `README.md`
placeholder describing what belongs there; delete it when real code arrives.

### Dependency rule (layers)

- `Domain` references nothing: no other project, no NuGet packages.
- `Application` references `Domain` only.
- `Infrastructure` references `Application` and `Domain`, and implements the Application ports.
- `Api` references `Application` and `Infrastructure`. It uses `Infrastructure` only for DI
  registration in `Program.cs`; controllers depend on `Application` types.
- Forbidden: `Domain` referencing anything; `Application` referencing `Infrastructure` or `Api`;
  `Infrastructure` referencing `Api`.

### Dependency rule (modules)

- A module may depend on `Common` and on another module's **public Application contracts**
  (interfaces, DTOs, domain events). It must never reference another module's entities,
  repositories or `DbSet`s.
- Cross-module writes happen in one transaction through Application services. Example: posting a
  bill in `Billing` calls the `Receivables` ledger port; both are saved by the same `IUnitOfWork`.
  The one use case that commands two aggregates (allocation: `Payment` and `Bill`) is documented in
  `docs/domain-model.md` §6.
- Cross-cutting modules (`Common`, `Identity`, `Audit`) may be used by any module and must not
  reference business modules.
- All of the above are asserted by NetArchTest tests in `tests/BillingRecon.ArchitectureTests`. A violation fails the build.

---

## 4. Financial correctness rules (non-negotiable)

These twelve rules apply to every feature. A change that breaks one is a bug, not a trade-off.
Each rule names the ADR that records why it is shaped this way and the BR-ids that make it testable.

1. **Money is `decimal` with explicit rounding** (ADR 0004; BR-001, BR-202, BR-203). All monetary
   values are `decimal`, never `float` or `double`, wrapped in a `Money` value object (INR only).
   Rounding is explicit: `Math.Round(x, 2, MidpointRounding.AwayFromZero)` applied at the **line
   level** (each bill line, tax line, allocation) through `Money.Round`. Document totals are sums of
   already-rounded lines and are never rounded independently. Never rely on the default
   `MidpointRounding.ToEven` (banker's rounding). EF column precision and scale are always
   configured explicitly, never defaulted: `decimal(18,2)` amounts, `decimal(19,4)` unit prices,
   `decimal(9,3)` quantities. A decimal without both does not make it past startup: the EF model
   refuses to build (ADR 0015).

2. **Posted records are immutable** (BR-005, BR-006, BR-209). Once a bill, credit note, payment,
   adjustment, refund or ledger entry is posted, its financial fields never change. Corrections are
   new documents that reference the original: a **reversal** (payments, allocations, ledger
   entries), a **credit note** (bills) or an **adjustment** (write-offs, rounding, discounts). No
   `UPDATE` of posted amounts, ever.

3. **Ledger entry in the same transaction** (ADR 0005; BR-007, BR-401). Every financial posting
   writes its balanced double-entry ledger lines in the same database transaction as the business
   document, via one `IUnitOfWork` commit per use case. A document without ledger lines, or ledger
   lines without a document, must be impossible. The posting templates are in `docs/database.md` §5.3.

4. **Cached balances must equal the ledger** (ADR 0005; BR-404, BR-405). Denormalised balances
   (`Bill.BalanceDue`, `Payment.UnappliedAmount`, `CreditNote.AvailableAmount`, `PayerBalances`) are
   caches derived from the receivables subledger. They are updated in the same transaction as the
   ledger lines. The balance identities in `docs/database.md` §5.5 are recomputed by a
   reconciliation query that fails loudly on drift; integration tests assert cache equals ledger
   after every scenario.

5. **`Idempotency-Key` on financial POSTs** (ADR 0006; BR-803). Every POST that creates or posts a
   financial document requires an `Idempotency-Key` header (UUID), scoped to the tenant. The key
   is reserved before and completed inside the business transaction, with a hash of the request
   and the response. A replay returns the original response unchanged. The same key with a
   different payload returns 422 `idempotency_key_reuse`; an in-flight key returns 409
   `idempotency_in_progress`; a missing header returns 400 `idempotency_key_missing`.

6. **`rowversion` optimistic concurrency** (ADR 0007; BR-804). Every mutable aggregate root has a
   `rowversion` column mapped as an EF concurrency token. `DbUpdateConcurrencyException` maps to
   409 `concurrency_conflict`. Clients send the version they read (ETag / `If-Match`, required on
   every mutation of an existing aggregate, 428 if missing) and retry on 409.

7. **Gapless document numbers** (ADR 0008; BR-801, BR-802). Bill, credit note, payment, adjustment
   and refund numbers are gapless and strictly sequential per **(TenantId, DocumentType,
   FiscalYear)**, formatted `PREFIX/YY-YY/NNNNNN` (`BIL/26-27/000123`, at most 16 characters per
   CGST rule 46). The Indian fiscal year runs 1 April to 31 March (FY 2026-27 is 2026-04-01 to
   2027-03-31) and is derived from the document's business date. Numbers are taken from a
   `NumberSeries` row locked with `UPDLOCK, HOLDLOCK` inside the posting transaction, after all
   validation, never from identity columns or sequences, never pre-allocated outside the
   transaction. A rolled-back posting rolls back its number, so a failed post cannot leave a gap.

8. **UTC instants, IST business dates, via `IClock`** (ADR 0010; BR-003, BR-004). Instants
   (created, posted, approved) are `DateTimeOffset` stored in UTC as `datetimeoffset` and read only
   from `IClock.UtcNow`, once per use case. Business dates (bill date, payment date, due date,
   fiscal year, ageing buckets) are `DateOnly` values in IST (Asia/Kolkata, UTC+05:30, no DST)
   derived through `IClock.TodayIst`. `DateTime.Now`, `DateTime.UtcNow`, `DateTime.Today`,
   `DateTimeOffset.Now`, `DateTimeOffset.UtcNow` and `TimeProvider.System` are banned in production
   code, and so is everything that uses the time zone of the machine (`TimeZoneInfo.Local`,
   `ToLocalTime`, `LocalDateTime`, `TimeProvider.LocalTimeZone` and `GetLocalNow`): calling one is a
   build error (RS0030, `src/BannedSymbols.txt`, ADR 0015). Only `SystemClock` may read the system
   time. Instants are whole milliseconds, the precision of the `datetimeoffset(3)` columns.

9. **Maker-checker** (BR-603 – BR-606). Sensitive actions take financial effect only after
   approval by a second, different user: reversals of payments and allocations, adjustments
   (write-offs, discounts, rounding), credit notes, refunds, payer credit-limit changes (R4),
   number-series prefix changes and bill postings at or above a tenant-configured threshold. Maker
   not equal to checker is enforced server-side and by a CHECK constraint; the pending state is
   visible; the checker's approval *is* the posting; both steps are audited.

10. **No hard deletes of financial data** (BR-005, BR-102, BR-105). No `DELETE` statements and no
    `DbSet.Remove` against financial tables; the application login has no DELETE grant on them.
    `ExecuteDelete` is banned outright (ADR 0015).
    Lifecycle is by status (Draft, PendingApproval, Posted, PartiallyPaid, Settled, Voided,
    Reversed) and by reversal documents. Drafts are voided, not deleted. Patients, payers and
    service items are deactivated by status, never deleted.

11. **Append-only audit** (BR-701 – BR-703). Every state change to a financial or security-relevant
    entity writes an audit record (who, when in UTC, tenant, entity, action, before/after JSON,
    correlation id) in the same transaction. Audit and ledger tables are insert-only for the
    application login: no UPDATE, no DELETE.

12. **TenantId from the JWT only** (ADR 0009, ADR 0015; BR-002, BR-602). The tenant comes from the
    validated JWT claim, resolved once per request into `ITenantContext`. Never read TenantId from
    the body, query string, route or a custom header. Every tenant-scoped entity implements
    `ITenantScoped` and carries TenantId as the leading column of every index. A global EF query
    filter applied by convention filters reads; a save interceptor sets the tenant on inserts and
    refuses any write for another tenant, without a tenant, or that changes the tenant; and
    `TenantId` is in the WHERE clause of every UPDATE and DELETE. The context attaches these guards
    to itself, so a context built by hand has them too. The tenant row itself (`Tenants`) is filtered to the
    current tenant's own row and may be updated or deleted by that tenant only; system work, which has no
    tenant, sees and may write every tenant row. `IgnoreQueryFilters()`, `ExecuteUpdate`,
    `ExecuteDelete`, and `EntityEntry.Reload` / `GetDatabaseValues` (which read a row back with the
    filters switched off) are banned (build error RS0030), because they go around those guards: a
    concurrency-conflict handler reads the current row with a normal query. Application code never
    assigns `TenantId`. Cross-tenant access is tested as a defect.

---

## 5. Coding conventions

- **Compiler settings** (in `Directory.Build.props`): `<Nullable>enable</Nullable>`,
  `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<ImplicitUsings>enable</ImplicitUsings>`,
  `latest-recommended` analyzers, `EnforceCodeStyleInBuild`, deterministic builds, NuGet audit.
  Style and naming rules live in `.editorconfig`; a rule is relaxed only with a comment saying why.
  No `#pragma warning disable` without a justifying comment.
- **Line endings are LF** in every file, on every operating system (`.gitattributes` `eol=lf` and
  `.editorconfig` `end_of_line = lf`), with CRLF only for `.sln`, `.ps1`, `.cmd` and `.bat`, so that
  `dotnet format --verify-no-changes` gives one answer on Windows and in CI (ADR 0017).
- **Value objects, and no `Guard` class.** `Money`, `UnitPrice`, `Quantity` and `Percent` are `readonly record
  struct`s built through `Of` (an invalid value is a bug and throws) or `TryOf` (for a validator or use case that
  reports a business failure); a `decimal` is never money, and only `Money.Round` turns an arithmetic result into
  money (ADR 0004). Guards are the BCL throw helpers (`ArgumentNullException.ThrowIfNull`,
  `ArgumentOutOfRangeException.ThrowIfNegative` and friends); there is no `Guard` class of our own.
- **Error codes** are constants in a static `<Module>Errors` class in `BillingRecon.Domain.<Module>`, one factory
  per failure (`IdentityErrors.TenantInvalid`); `ErrorCodesTests` keeps them snake_case and unique.
- **File-scoped namespaces**: `namespace BillingRecon.Domain.Billing;` on one line, no braces.
- **`Result<T>` for business failures.** Expected failures (validation, not found, conflict,
  business rule) return `Result` / `Result<T>` with an `Error { Code, Message, Kind }`. Exceptions
  are for bugs and infrastructure faults only. Never use exceptions for control flow. The types
  live in `BillingRecon.Domain.Common` (ADR 0011).
- **ProblemDetails with `errorCode`.** API errors are RFC 9457 ProblemDetails
  (`application/problem+json`) with an `errorCode` extension (stable snake_case, for example
  `bill_already_posted`), a `traceId` and a `correlationId`.
  Mapping: Validation 400 (with `errors`), NotFound 404, Forbidden 403, Conflict 409,
  BusinessRule 422, unhandled 500 `internal_error` with no internal details leaked (exception
  details appear only in Development). Controllers return `Problem(error)`; never build an error
  response by hand (ADR 0011).
- **Routes.** Every controller route starts with `ApiRoutes.V1` (`api/v1`); a test fails otherwise
  (ADR 0013).
- **Logging.** Pass values as message-template arguments, never by string interpolation, and never
  put patient data into an exception message: the PII mask covers event properties only (ADR 0012).
- **Options.** One `sealed` options class per configuration section, bound with `BindConfiguration`
  and validated with `ValidateOnStart`. A validation message never repeats a secret value.
- **Persistence** (ADR 0015). A new table is an `IEntityTypeConfiguration` in its module's
  Infrastructure folder that states lengths, precision, indexes and table-specific CHECK
  constraints, and nothing the context already does by convention: ids, the tenant filter and
  guard, audit columns, the row version, `datetimeoffset(3)`, `date`, enums as text with their
  CHECK. The context has no `DbSet` properties; repositories use `Set<T>()`. A typed id is
  `readonly record struct XId(Guid Value) : ITypedId`. Entities declare `Id`, `TenantId`, the audit
  columns and `RowVersion` with non-public setters and never assign them. A text column that is
  unbounded on purpose says so with `IsUnbounded()`. Migrations are scaffolded (the command is in
  section 8), never edited by hand to add what the model could say, and never changed once applied.
- **Explicit DTO mapping.** Static `ToDto()` / `ToDomain()` methods or small mapper classes.
  No reflection-based mappers.
- **Async all the way.** No `.Result`, `.Wait()` or `GetAwaiter().GetResult()`. Async methods end
  in `Async`. `ValueTask` only where a measurement justifies it.
- **`CancellationToken` passed through.** Last parameter (`CancellationToken ct`) on every async
  method, threaded from the controller down to EF Core and HTTP calls.
- **Also:** `sealed` classes by default; `record` for DTOs and value objects; `DateOnly` for business
  dates; `required` members over constructor null checks; no `dynamic`; one type per file.

---

## 6. Working agreement

1. **Plan first, wait for approval.** For any non-trivial change, present a short plan (files,
   steps, tests, risks) and wait for an explicit go-ahead before editing code. Trivial fixes
   (typos, doc wording) may proceed directly.
2. **Small steps.** One vertical slice or one concern per change. The build and the tests stay
   green at every step.
3. **Tests with every feature.** Unit tests for Domain and Application, integration tests for
   Infrastructure and Api on real SQL Server (Testcontainers + Respawn), architecture tests for
   the rules in section 3. A feature without tests is not done.
4. **Never weaken tests.** Do not delete, skip, loosen or widen a test to make it pass. Fix the
   code, or raise the disagreement with the owner. Changing expected behaviour is called out
   explicitly in the change description.
5. **Record decisions as ADRs** in `docs/adr/NNNN-title.md` (format in ADR 0000). Any decision
   that changes a rule in this file must be linked from here.
6. **Ask before adding dependencies.** Every new NuGet or npm package needs explicit approval,
   and an ADR if it is architectural. Prefer the BCL and ASP.NET Core built-ins.
7. **Keep this file current.** Stale memory is worse than none.
8. **Never commit secrets.** Connection strings, keys and tokens come from environment variables
   or user secrets; `.gitignore` already excludes the usual files.

---

## 7. Documentation map

| Document | Contents |
|---|---|
| `docs/requirements.md` | Domain and vocabulary, actors and permissions, R1 scope, the **BR-nnn business-rule catalogue**, user stories with acceptance criteria, NFRs, R2–R5 outlines |
| `docs/domain-model.md` | Module map, value objects, aggregates with invariants (citing BR-ids), Bill state machine, transaction boundaries and the allocation exception, domain events, ports |
| `docs/database.md` | Conventions, ER diagram, every R1 table with types/keys/indexes/CHECKs, number-series lock, ledger accounts and posting templates, balance identities and reconciliation SQL, idempotency lifecycle, permissions, migrations |
| `docs/api.md` | REST conventions, auth, idempotency, error-code catalogue, endpoints |
| `docs/security.md` | Authentication, authorisation, tenancy, maker-checker, secrets, threat model |
| `docs/testing-strategy.md` | Test pyramid, tooling, database tests, architecture tests, CI gates |
| `docs/runbook.md` | Written so far: local infrastructure and the "Verify on a Docker machine" checklist. Still to come: migrations, backups, incident procedures |
| `docs/adr/` | Architecture Decision Records. ADR 0000 defines the format. |

ADRs so far: 0000 record decisions, 0001 target framework (.NET 8; superseded by 0014),
0002 solution layout and build conventions, 0003 modular monolith and module boundaries, 0004 money
and rounding, 0005 receivables subledger with cached balances, 0006 idempotency for financial POSTs,
0007 optimistic concurrency with rowversion, 0008 gapless document numbering, 0009 multi-tenancy by
TenantId and query filter, 0010 time handling, 0011 error model (Result, exceptions, ProblemDetails),
0012 structured logging, correlation id and PII masking, 0013 API versioning by route prefix,
0014 target framework .NET 10 (and the SqlClient pairing), 0015 persistence conventions (the
context, typed ids, tenant isolation on reads and writes, audit columns, banned APIs, migrations),
0016 integration test infrastructure (one SQL Server per test run, two databases, Respawn, the test
identity), 0017 the CI pipeline, the API container image and LF line endings.
ADRs 0003 and 0005–0010 are *Proposed* until the owner reviews the R1 design documents; flip their
Status to Accepted then (ADR 0003 carries an amendment of 2026-10-17 with the precise module rule).
ADR 0004 is Accepted since its implementation (Prompt 2.1, 2026-10-17). ADRs 0011–0017 are Accepted (0011–0013 approved with the infrastructure step, 0014 and 0015 on the owner's
decisions of 2026-10-03, 0016 and 0017 with the approvals of the Prompt 1.4 and 1.5 plans on 2026-10-17;
ADR 0015 marks
the points it added beyond those decisions, which wait for the owner's review).

---

## 8. Commands

Run from the repository root, on a machine with a .NET 10 SDK (not the office machine, see section 2).

```bash
dotnet tool restore                       # once per clone: the local dotnet-ef tool
dotnet build                              # whole solution, warnings are errors
dotnet test                               # all four test projects; the Database collection needs a running Docker engine (ADR 0016)
dotnet test --filter "FullyQualifiedName!~IntegrationTests.Database."  # without Docker: everything but the database tests (keep the dot)
dotnet format --verify-no-changes         # the CI format gate; plain dotnet format applies the fixes
docker build -f src/BillingRecon.Api/Dockerfile -t billingrecon-api .   # the API image (docs/runbook.md section 6)
docker compose up -d                      # SQL Server 2022 + Seq; needs .env (copy .env.example). Docker machine only.
dotnet run --project src/BillingRecon.Api # http://localhost:5080, Development: creates and migrates the database
dotnet ef migrations add NNNN_<Slice> --project src/BillingRecon.Infrastructure --output-dir Common/Migrations
```

The API refuses to start without `Database:ConnectionString`. Set it once per machine with user-secrets
(the exact command is in `docs/runbook.md`) or pass the `Database__ConnectionString` environment variable.
The other migration commands (check, script, apply by hand, undo) are in `docs/runbook.md` section 3; none of
them needs the API's configuration, and only "apply" needs a database.

Useful URLs while running: `/health/live`, `/health/ready`, `/api/v1/system/info`, the Seq UI at
`http://localhost:5341`, and in Development `/openapi/v1.json` and `/scalar`.
Planned: `api` and `frontend` services in Docker Compose, and `npm --prefix frontend run dev`.

---

## 9. Open items

Kept here so they survive between sessions. Remove an item when it is done.

1. **Run the database tests of Prompt 1.4 on the personal machine** (ADR 0016). The test infrastructure was written
   on the office machine on 2026-10-17 and pre-checked in the .NET 9 copy, where everything outside the `Database`
   collection passes and the collection fails for want of Docker, by design. On the personal machine, with Docker
   Desktop running, from the repository root: `dotnet build`, then `dotnet test`. What that run has to show:
   - the three new packages restore (the NuGet audit covers their graphs; an advisory stops the restore, and the
     fix is to pin the patched version in `Directory.Packages.props`);
   - the container starts from the compose image and the `Database` collection is green: migrations applied and
     recorded, the CHECK constraints and the unique index, readiness 503 then 200, isolation with two tenants,
     the forged update and delete that change nothing, the audit columns, the row version, the typed ids, the
     client identity;
   - the architecture tests, including the module rule and the scan of every project file for banned packages.
   Then record that machine's `dotnet --list-sdks` in section 2. Step 7 of the runbook checklist (`/health/ready`
   answers 200 against the compose SQL Server) is still unrecorded.
   The review fixes of the DbContext step were confirmed green on .NET 10 by the owner the same day (output not
   kept): the log filter stays removed and ADR 0011 holds, the named filters and the tenant-row filter behave.
2. **DbContext step: points that wait for the owner's review** (the additions marked in ADR 0015):
   - the context attaches the save interceptors to itself instead of the dependency-injection set-up;
   - `TenantId` is a concurrency token and never updatable on every tenant-scoped entity;
   - the tenant filter is a named filter (EF Core 10);
   - `EntityEntry.Reload` / `GetDatabaseValues` are banned, and so are the calls that use the machine's time zone;
   - enum CHECK constraints are generated by convention; CHECK constraints and indexes are declared in the model
     instead of `migrationBuilder.Sql` (changes `docs/database.md` section 8);
   - the EF Core tools use the Infrastructure project as startup project;
   - `CreatedByUserId` and `UpdatedByUserId` are null when no user acts (the owner asked for this to be decided
     and recorded);
   - `ExecuteUpdate`/`ExecuteDelete` are banned outright rather than routed through a helper;
   - `Tenants` has no audit columns beyond `CreatedAtUtc` (`Tenant.PostingApprovalThreshold` is `Money?` since
     Prompt 2.1);
   - the tenant row itself is filtered to, and writable by, its own tenant only, while system work sees and may
     write every tenant row (added 2026-10-17 after a review: ADR 0009 promised it and nothing implemented it).
   Open question for the owner: ban the raw-SQL entry points of EF Core as well (`ExecuteSql*`, `SqlQuery*`,
   `FromSql*`, `GetDbConnection`) and sanction them file by file, as `BannedSymbolsTests` already does for RS0030
   suppressions? Today raw SQL is left to review (ADR 0009). Flip the Status line of ADR 0015 once reviewed.
3. **Record the container checklist results** (`docs/runbook.md`, "Verify on a Docker machine", step 12). The
   owner set the containers up on 2026-10-17 (reported, not recorded step by step). Steps 3, 7, 8 and 10 of the
   checklist (both containers healthy; `/health/ready` 200 against SQL Server 2022, which is also the first time
   the migration meets it; readiness following SQL Server; the event in Seq with its correlation id) are not
   recorded yet, and with them the two pending acceptance criteria of the infrastructure step: "`docker compose
   up` starts the DB and Seq" and "logs appear in Seq with a correlation id". When they pass, remove the "first
   run" notes that step 12 names.
4. **Done with Prompt 1.4 (2026-10-17):** the tests the DbContext step promised (tenant filter, write guard and
   audit stamps against real SQL Server, a pending migration means not ready, the `TenantId` predicate in UPDATE
   and DELETE) exist in `tests/BillingRecon.IntegrationTests/Database` (ADR 0016) and await their first run with
   Docker (item 1).
5. **Due with the tables that need them** (ADR 0015, "Not built yet, on purpose"):
   - with the first tenant-scoped table (`Users`): the foreign key from `TenantId` to `Tenants` by convention,
     "no ON DELETE CASCADE anywhere" by convention, and a model check that every index of a tenant-scoped entity
     leads with `TenantId`; also a development tenant and user row, so that rows written in Development satisfy
     their foreign keys;
   - with the first financial and append-only tables: a guard at the EF layer for section 4 rules 10 and 11
     (today a delete of the current tenant's own row, and an update of a creation-only row, pass the tenant
     guard; the database grants that would stop them do not exist yet);
   - with the first aggregate that has child rows (`Bill`): the unit of work marks the root as modified when a
     command changes only children, so the root's row version is checked and advanced (ADR 0007, ADR 0015
     section 4);
   - with the unit of work: one clock reading per use case, and stamping the tenant and audit columns before
     domain events are dispatched (both interceptors expose `Apply` for that);
   - with the logins of `docs/database.md` section 7: the grants, including `SELECT` on
     `__EFMigrationsHistory` for the API's login, without which `/health/ready` can never answer 200;
   - in Phase 5: `LoginUserLookup` as the one sanctioned use of `IgnoreQueryFilters` (add it to
     `BannedSymbolsTests`); the procedure for applying migrations outside Development with the migration login.
6. **Review findings of 2026-10-03 that are still open** (numbering from that review):
   - 1.1 closed 2026-10-17: the module rule and its two exceptions are written in ADR 0003 (amendment) and
     `ModuleDependencyTests`; Audit is generic over `IDomainEvent`, approval handlers live with their documents,
     aggregates take ids rather than each other.
   - 2.3 no default-deny authorisation (comes with the Identity slice); 2.4 TLS, allowed hosts, server header;
     2.5 package lock files, `nuget.config`, transitive pinning (the CI cache keys on `Directory.Packages.props`
     and the project files instead of lock files).
   - 3.1, 3.2 and 3.3 closed 2026-10-17 (`ConventionTests`, `ModuleDependencyTests`, `LayerDependencyTests`:
     controllers never use EF Core or Infrastructure, no minimal APIs, every type in a known module, banned
     packages scanned in every project file, the Domain project file declares no reference); 3.6 soft edges in
     the layer tests; 3.7 xunit 2 is deprecated in favour of xunit.v3.
   - 4.3 most `.editorconfig` style rules were not enforced by the build on the .NET 8 SDK (the .NET 10 SDK
     enforced one more on 2026-10-17, explicit accessibility on interface members, which seven interfaces
     gained; re-check the rest); 4.6 the version endpoint always says `1.0.0`; 4.7 `docs/api.md`, `docs/security.md` and
     `docs/testing-strategy.md` are still stubs.
   - 5.2 placeholder READMEs restate the module map; 5.3 one redundant test theory; 5.4 redundant build properties.
   - Closed by the DbContext step: 1.2 (the time rule is enforced by the banned-API analyzer, and the list
     includes `DateTimeOffset.UtcNow`) and 4.5 (`IClock` is an Application port, ADR 0015).
   - New on 2026-10-03: `PiiMaskerTests.A_very_long_value_is_scanned_without_hitting_the_match_timeout` depends on
     the speed of the machine (a 400,000-character value has to be scanned within the 250 ms match timeout). It
     failed once in 40 runs while four test runs were going at the same time. Left unchanged; the owner decides
     whether a smaller input still makes the point. Seen again on 2026-10-17: under coverlet instrumentation it
     fails outright, which is why CI instruments the Domain and Application unit tests only (ADR 0017).
7. **CI acceptance (Prompt 1.5, ADR 0017).** The workflow, the Dockerfile and Dependabot were written on
   2026-10-17 and have not run: the repository is not on GitHub and has no commit. To do, in this order: create
   the GitHub repository, make the first commit, push `main`, and watch the first run of
   `.github/workflows/ci.yml` (the "deliberately broken test" criterion: a pull request that breaks one
   assertion must go red, then is closed unmerged). The Dockerfile can be proven earlier on the personal machine
   with `docker build` (`docs/runbook.md` section 6). Pre-checked in the .NET 9 copy: the format gate with the
   LF rule, the coverage collection of the two unit test projects, the vulnerable-package report (whose gate
   fires there on the 9.x graph, as it should) and the migration script command. The first commit exists
   (`03c42fb` on `main`); the push is the owner's.
8. **Phase 2 is on branch `phase-2-billing`** (the blueprint's rule: a branch per phase, one commit per prompt).
   Prompt 2.1 (Money and the shared kernel, ADR 0004 built) was written, reviewed and fixed on 2026-10-17 and is
   pre-checked on .NET 9 only; its first run on .NET 10 and with Docker (the Money round trip in the Database
   collection) comes with the next push.
