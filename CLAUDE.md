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
| Banned APIs | **Microsoft.CodeAnalysis.BannedApiAnalyzers** | `src/BannedSymbols.txt` lists what production code must not call (system time and the machine's time zone, `IgnoreQueryFilters`, `EntityEntry.Reload`/`GetDatabaseValues`, `ExecuteUpdate`/`ExecuteDelete`, and since Prompt 3.1 every raw-SQL entry point of EF Core: `ExecuteSql*`, `SqlQuery*`, `FromSql*`, `GetDbConnection`); a call is build error RS0030. Four files may suppress it, each for one named reason: `SystemClock` (the system time), and three that each send one constant, parameterised, tenant-bound SQL statement: `NumberSeriesAllocator`, `ReceivablesLedger` (the locked change of a payer's balances) and `ReconciliationQuery` (read-only). A fifth needs an ADR. Tests guard the list and where the rule may be suppressed (ADR 0015, ADR 0021). |
| Validation | **FluentValidation** (12.2.0) | One public validator per command or query that has a shape to check, in the Application layer, run by its handler as the handler's first step; it checks the shape (presence, lengths, formats, ranges), the domain and the handler the business rules. A failure is 400 `validation_failed` with messages per field (ADR 0018). The API registers the validators with its scan of the Application assembly. |
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
  against a real database), not a substitute for SQL Server 2022. Since 2026-10-17 the copy's
  `SqlServerFixture` can also be pointed at LocalDB (a patch to the copy only, never to the repository), so the
  whole `Database` collection runs here as a pre-check; the run with Docker on the personal machine stays the
  proof. LocalDB 2022 is not installed: its installer needs a licence acceptance, which is the owner's to give.
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
                                IHasRowVersion, TenantId, UserId, PatientId, PayerId, ServiceItemId, CommonErrors,
                                the internal Field text helper), the value objects Money, UnitPrice, Quantity and
                                Percent (Common, ADR 0004), the Tenant aggregate with IdentityErrors (Identity),
                                the Patient and Payer aggregates with Mrn, their events and PayersErrors (Payers),
                                and, in Billing, the ServiceItem aggregate with ServiceItemCode, and the Bill
                                aggregate with BillLine as drafts (LineCalculator, BusinessDateWindow), with their
                                events and BillingErrors. BillId, BillLineId and the IHardDeletable marker are in
                                Common (ADR 0019), and so are FiscalYear, DocumentType, DocumentNumber and the
                                read-only NumberSeries entity (ADR 0008), and IdempotencyKey, the record of a key
                                and its stored answer (ADR 0006). Since Prompt 3.4 (ADR 0020): AuditRecord
                                (Audit), the markers IAppendOnly, IHasDomainEvents and IHasReason (Common), and
                                an event for every correction (PatientUpdated, PayerUpdated, ServiceItemUpdated,
                                BillHeaderUpdated, BillLinesReplaced). Since Prompt 3.3 (ADR 0005 built):
                                LedgerPosting with LedgerEntry, both append-only, PayerBalance and
                                ReceivablesErrors (Receivables); LedgerPostingId (Common). Since Prompt 3.5
                                (ADR 0022): Bill.Post and CheckCanPost, the statuses Posted, PartiallyPaid and
                                Settled, BillPosted, and IHasDocumentNumber (Common).
  BillingRecon.Application/     Use cases (command/query handlers as plain classes), FluentValidation validators,
                                port interfaces (IClock, IUnitOfWork, repositories, INumberSeries,
                                IIdempotencyStore, ITenantContext, ICurrentUser), DTOs and explicit mappers.
                                Today: IClock, ITenantContext, ICurrentUser, the use-case contracts
                                ICommandHandler and IQueryHandler, IUnitOfWork, paging, the validator helper and
                                the shared text rules (Common); the patient and payer use cases, validators, DTOs,
                                repositories and read services (Payers), and IBillableParties, what Payers answers
                                when a bill is about to be raised; the same for the service catalogue and for bill
                                drafts (Billing); ITenantSettingsReader (Identity); INumberSeries, the gapless
                                document numbers (Common, ADR 0008). IIdempotencyStore with its outcome and
                                response records (Common, ADR 0006). ADR 0018, ADR 0019. IDomainEventSubscriber
                                with DomainEventOccurrence, and ICorrelationContext (Common); IAuditWriter, the
                                generic AuditSubscriber, AuditAction, and the query of an audit trail with its
                                read service and DTO (Audit). ADR 0020. IReceivablesLedger with PostingTemplate,
                                IPayerAccounts, IReconciliationQuery with DriftRow (Receivables, ADR 0005).
                                PostBillHandler, the first posting use case, and PostingTemplate.BillPosted
                                (Billing, Receivables; ADR 0022).
  BillingRecon.Infrastructure/  EF Core DbContext, entity configurations, migrations, port implementations,
                                ledger posting, number series, idempotency store, audit writer, JWT wiring. Today:
                                typed options (Common/Settings), the Serilog pipeline and PII mask (Common/Logging),
                                the context with its conventions, interceptors, startup step, design-time
                                factory, the value-object converters, the TenantId-first index check and the unit
                                of work (Common/Persistence), SystemClock (Common/Time), the database health
                                check (Common/Health), the identities, the Tenants table and the development
                                tenant seeder (Identity), the Patients and Payers tables with their repositories
                                and read services, registered by AddPayers (Payers), the ServiceItems, Bills and
                                BillLines tables with their repositories and read services, registered by
                                AddBilling (Billing), the tenant settings reader (Identity), the guard against
                                deletes and the unit of work that makes a root part of the save when only its
                                children changed (Common/Persistence, ADR 0019), the NumberSeries table and the
                                allocator that takes a number in the transaction the unit of work commits
                                (Common/Numbering, ADR 0008; the one file with raw SQL), the IdempotencyKeys table
                                and the store that holds the transaction of a request while its key is reserved
                                (Common/Idempotency, ADR 0006), AddInfrastructure. The unit of work dispatches
                                domain events before it saves, with one clock reading per commit
                                (BeginCommit/EndCommit of the context), EntitySnapshot builds the before and after
                                images, AppendOnlyGuardInterceptor refuses an update or delete of an IAppendOnly
                                row (Common/Persistence, ADR 0020); the AuditRecords table, AuditWriter,
                                AuditReadService, registered by AddAudit (Audit). The LedgerPostings,
                                LedgerEntries and PayerBalances tables, ReceivablesLedger, PayerAccounts and
                                ReconciliationQuery, registered by AddReceivables (Receivables, ADR 0005); the
                                typed-id foreign key convention of the context (ADR 0021).
                                Migrations go to Common/Migrations.
  BillingRecon.Api/             Controllers, middleware and filters (correlation id, idempotency, tenant resolution),
                                the error contract (Common/Errors: exception handler, Error to ProblemDetails mapper),
                                authentication/authorisation, OpenAPI + Scalar, Program.cs (composition root),
                                and the Dockerfile of the API image (ADR 0017). Today also: the ETag and If-Match
                                helpers of ApiControllerBase and AddApplication, which registers every handler and
                                validator by scanning (Common); the patient and payer controllers with their
                                request records (Payers); the service item and bill controllers with their request
                                records (Billing). Common/Idempotency: the [Idempotent] attribute, its filter,
                                the header names and the request fingerprint (ADR 0006). The read-only audit
                                trail controller (Audit) and HttpCorrelationContext (Common/Correlation), ADR 0020.
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
   As built (ADR 0005, built note): a posting use case calls `IReceivablesLedger.PostAsync` with a
   `PostingTemplate` built by the named factory of its event, after its validation and after it has
   taken its number. Lock order: the number series, then the balances of the payer, both before the save.
   Every posting use case follows the six steps of ADR 0022 in order: read and compare the version;
   every check that can refuse; the number; the document takes it; the ledger; the save. The row of
   the document is written last, under its row version. A document whose total is zero has no ledger
   posting (BR-401 as amended).

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
   As built (ADR 0006, built note of 2026-11-20): mark the action `[Idempotent]`; the hash covers the
   method, the path with its query, `If-Match` and the body; a success is stored and committed with
   the work it reports, a refusal (4xx) is stored after that work has been rolled back, a fault
   frees the key. A test lists which controllers are financial and fails for a POST without the
   attribute.

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
    service items are deactivated by status, never deleted. **Two exceptions, neither a financial
    record:** a line of a *draft* bill is deleted when the draft's lines are replaced (ADR 0019),
    and the record of an idempotency key is deleted when its request failed with a fault, or when
    it has expired (ADR 0006). The context refuses to delete a row of any entity that is not marked
    `IHardDeletable`, which only `BillLine` and `IdempotencyKey` are; a test lists them.

11. **Append-only audit** (BR-701 – BR-703). Every state change to a financial or security-relevant
    entity writes an audit record (who, when in UTC, tenant, entity, action, before/after JSON,
    correlation id) in the same transaction. Audit and ledger tables are insert-only for the
    application login: no UPDATE, no DELETE.
    As built (ADR 0020): an aggregate is audited by raising a domain event, and **every state change
    raises one**, raised only when something really changed. The unit of work dispatches the events
    before it saves, and the generic `AuditSubscriber`, the only subscriber, writes one `AuditRecord`
    per event into the same save. The before and after images are the stored row with its children.
    An entity marked `IAppendOnly` (only `AuditRecord` so far) cannot be updated or deleted through
    the context. A new use case that changes state needs its event and a test that counts one record.

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
  Every index of a tenant-scoped table leads with `TenantId` (the model fails otherwise), and no
  index is added per foreign key by default: a configuration declares each index it needs. A
  module's value object stored through a converter is a `readonly record struct` (a class would be
  taken for an entity), with the converter in the module's entity configuration (ADR 0015, built
  note of 2026-10-17). A column that holds the typed id of another entity gets its foreign key from
  the context, `FK_<Table>_<Column>`, whichever module the other entity is in: a configuration does
  not declare it and never names another module's entity for it (ADR 0021). A migration may carry
  one hand-written, idempotent data statement at the end of `Up()` when the model cannot express
  it; the line is in the runbook and a test checks it (ADR 0021).
- **Use cases** (ADR 0018). One public sealed handler per command or query, behind
  `ICommandHandler<TCommand, TResult>` or `IQueryHandler<TQuery, TResult>`, returning `Result<T>`. A
  command handler validates (`validator.GetErrorAsync` first), loads through a repository, compares
  the version the client read, calls the aggregate and commits once through `IUnitOfWork`, which
  returns `concurrency_conflict` or `duplicate_key` for a lost race; the handler replaces
  `duplicate_key` with the error of the rule it checked. A query handler reads through a read
  service. Handlers and validators are registered by the API's scan (`AddApplication`); each
  business module registers its ports with its own `Add<Module>` in its Infrastructure folder,
  called from `Program.cs`.
- **The HTTP contract of a resource** (ADR 0018, `docs/api.md`). Request records in the Api with
  explicit mapping to commands, refusing unknown JSON members; the Application DTOs as responses.
  Enums by name, never numbers. `PATCH` sends every editable field, an optional one left out is
  cleared. State changes are `POST` actions; no `DELETE`. Responses that return one record carry
  its version as the ETag; changes of an existing record need `If-Match` (428 without, 400 when
  malformed, 409 when stale). Lists take `page` and `pageSize` (at most 100) and answer
  `{ items, page, pageSize, totalCount }`. Another tenant's record is 404, never 403.
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
| `docs/api.md` | REST conventions, auth, idempotency, error-code catalogue, endpoints. Written so far: the conventions, the patient and payer endpoints (Prompt 2.2), the service item endpoints (Prompt 2.3), the bill draft endpoints (Prompt 2.4) and the audit trail endpoint (Prompt 3.4) |
| `docs/build-prompts.md` | The build prompts from zero to R1, reconstructed on 2026-11-20: what each finished prompt built, and the wording and order of the prompts still to give. Its phase numbers are the ones this file uses |
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
identity), 0017 the CI pipeline, the API container image and LF line endings, 0018 use cases,
validation, the unit of work and the HTTP contract of a resource (Accepted with the approval of the
Prompt 2.2 plan, 2026-10-17), 0019 bill drafts: replaceable lines, one version for the whole bill, and the
guard against deletes (Accepted for the points of the Prompt 2.4 plan and, with it, the amendment of ADR 0003 for the foreign
keys of `Bills`, 2026-11-20; its unit-of-work point awaits the owner's review), 0020 domain events are
dispatched by the unit of work, and the audit trail is their one subscriber (Accepted with the approval of the
Prompt 3.4 plan, 2026-11-20; it also builds the append-only guard and names the migration `0007_Audit`),
0021 typed-id foreign keys by convention, a data step written by hand, and two more files with raw SQL
(Accepted on the owner's decisions of 2026-11-20 for Prompt 3.3).
0022 posting a bill: the order of its locks, the bill that comes to nothing, and what a posted bill answers
(Accepted on the owner's decisions of 2026-11-20 for Prompt 3.5; it amends BR-401).
ADR 0005 is Accepted since its implementation (Prompt 3.3, 2026-11-20); its built note records what the build
decided about the ledger (the port and its template, the BR-405 check, the lock, who opens a payer's balances).
ADR 0006 is Accepted since its implementation (Prompt 3.2, 2026-11-20); its built note records where the build
differs from the first text (the store owns the business transaction, a refusal is stored after a rollback, the
fingerprint includes the query and `If-Match`) and one change the owner accepted on 2026-11-20 (the tenant guard treats
a temporary key value as not set).
ADRs 0003, 0007, 0009 and 0010 are *Proposed* until the owner reviews the R1 design documents; flip their
Status to Accepted then (ADR 0003 carries an amendment of 2026-10-17 with the precise module rule, and one of
2026-11-20, accepted, for the foreign keys of `Bills`). ADR 0008 is Accepted since its implementation (Prompt 3.1,
2026-11-20); its built note records what the build added (who opens the transaction, the sixteen-character limit as
a hard stop, the ban on raw SQL) and one finding the owner accepted the same day (the first number of a new series
can wait for a neighbouring series).
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
   Answered 2026-11-20 with the owner's approval of the Prompt 3.1 plan: the raw-SQL entry points of EF Core
   (`ExecuteSql*`, `SqlQuery*`, `FromSql*`, `GetDbConnection`) are banned (RS0030), and the one file that may use
   one is `NumberSeriesAllocator.cs`, listed in `BannedSymbolsTests` (ADR 0008, built note). Flip the Status line of
   ADR 0015 once the remaining points are reviewed.
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
   - built 2026-10-17 with the first tenant-scoped tables (`Patients`, `Payers`; ADR 0015, built note): the
     foreign key from `TenantId` to `Tenants`, no ON DELETE CASCADE, and the TenantId-first index check, all by
     convention, and the development tenant row (`DevelopmentTenantSeeder`). Still due: the development user row,
     with the `Users` table, and with it the foreign keys of `CreatedByUserId` and `UpdatedByUserId` to `Users`;
   - built 2026-11-20 with `Bills` (Prompt 2.4, ADR 0019): the guard at the EF layer for rule 10,
     `HardDeleteGuardInterceptor`, which refuses to delete a row of any entity but `BillLine`. Built 2026-11-20
     with `AuditRecords` (Prompt 3.4, ADR 0020): the guard for rule 11, `AppendOnlyGuardInterceptor`, which refuses
     an update or a delete of a row marked `IAppendOnly`; the ledger entities get the marker in Prompt 3.3. The
     database grants that would stop either do not exist yet;
   - built 2026-11-20 with `Bill` (ADR 0019): the unit of work marks the root as modified when a command changes
     only children, so the root's row version is checked and advanced (ADR 0007);
   - built 2026-11-20 with the dispatch of domain events (Prompt 3.4, ADR 0020): the tenant and audit columns
     are stamped before the events are dispatched, and a commit reads the clock once
     (`BillingReconDbContext.BeginCommit` and `EndCommit`);
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
     gained; re-check the rest); 4.6 the version endpoint always says `1.0.0`; 4.7 `docs/security.md` is still a
     stub (`docs/testing-strategy.md` was written with Prompt 1.4, `docs/api.md` with Prompt 2.2).
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
9. **Prompt 2.2 (patients and payers, ADR 0018), written 2026-10-17, not built on .NET 10 yet.** Pre-checked in the
   .NET 9 copy: build, format gate, every test outside the database collection (apart from the eleven that differ
   on .NET 9), and the whole database collection, pointed at LocalDB 2019 in the copy only (89 tests). To do on
   the personal machine, in this order: `dotnet tool restore`, scaffold the migration
   (`dotnet ef migrations add 0002_Payers --project src/BillingRecon.Infrastructure --output-dir Common/Migrations`),
   copy its three files (`*_0002_Payers.cs`, `*_0002_Payers.Designer.cs`, the updated
   `BillingReconDbContextModelSnapshot.cs`) back into the repository, then `dotnet build` and `dotnet test` with
   Docker running. Until the migration exists, `MigrationsTests` and every test of the database collection fail.
   Follow-ups that belong to later slices:
   - **Receivables (Phase 3):** the zero `PayerBalances` row is created by the payer use case through a Receivables
     port (docs/domain-model.md section 8), plus one for every payer that exists by then.
   - **Audit (Phase 3):** done with Prompt 3.4 (item 14): the domain events are dispatched, and patients and
     payers have their audit records (BR-701).
   - **Identity (Phase 5):** the role checks of the permission matrix (`docs/requirements.md` section 1): patients
     and payers are written by TenantAdmin, Supervisor and BillingClerk, read by everyone.
   - **Open question for the owner:** composite foreign keys that include the tenant, so that the database refuses
     a payer naming another tenant's patient (ADR 0015, built note of 2026-10-17). Today the use case prevents it.
   - **Recorded deviations:** a 409 does not carry the current ETag (ADR 0007, built note); creating a guarantor is
     not idempotent (ADR 0018, `docs/api.md` section 7).
   - **2.2-close, started 2026-11-20 on the office machine, not finished.** The migration exists as
     `20261018182819_0002_SyncCurrentModel` (EF Core 10.0.12), not `0002_Payers`; it was compared with
     `docs/database.md` section 3.2, the entity configurations and the snapshot (no difference), it keeps its name,
     and the two tests that named it expect the real id. Found and put right the same day: the EF Core Design package
     had been moved from the Infrastructure project to the Api project (back in Infrastructure, as section 2 says),
     and a pin of `System.Security.Cryptography.Pkcs` 9.0.21 had appeared without a note (kept: a NuGet audit fix per
     the owner; the advisory id is not recorded). **Still to do:** the owner's run on the personal machine
     (`dotnet --list-sdks`, `dotnet tool restore`, `dotnet build`, `dotnet format --verify-no-changes`, `dotnet test`
     with Docker), then items 1 and 8 and the SDK list of section 2, the README status, the stale `0002_Payers` in
     ADR 0015 and ADR 0018, and the commit "Prompt 2.2: patients and payers (ADR 0018)". The scaffolding steps at the
     top of this item are done and no longer apply.
10. **Prompt 2.3 (service catalogue), written 2026-11-20 on top of the uncommitted 2.2, not built on .NET 10 yet.**
    The owner chose to go on before 2.2 was verified, so the two prompts are in one working tree: commit 2.2 first,
    then 2.3, each on its own.
    - Built: the `ServiceItem` aggregate (Billing) with `ServiceItemCode`, the six use cases, the `ServiceItems`
      table, `/api/v1/service-items`, and their tests. Decided with the owner: `Status` instead of the `IsActive` bit
      of the first design, the code fixed at creation, the `Field` helper moved from Payers to `Domain.Common` and
      the two text rules from `PayersRules` to `Application.Common.TextRules`.
    - Pre-checked in the .NET 9 copy: build, format gate, every unit test, and every integration test including the
      whole database collection on LocalDB 2019 (127 tests) with a migration scaffolded there by EF Core 9. Failing
      there, as before and by design: 5 `BannedSymbolsTests`, 5 logging and error-contract tests, 1 unnamed-filter
      test.
    - **To do on the personal machine:** scaffold the migration under exactly this name,
      `dotnet ef migrations add 0003_ServiceItems --project src/BillingRecon.Infrastructure --output-dir Common/Migrations`,
      check that it creates only the `ServiceItems` table, copy its three files back, then `dotnet build`,
      `dotnet format --verify-no-changes` and `dotnet test` with Docker. Until the migration exists, `MigrationsTests`
      and every test of the database collection fail.
    - Not proven anywhere yet: the search of the list endpoint casts the code's value object to its column for
      `LIKE` (`ServiceItemReadService`); it translates on EF Core 9 and must be seen to translate on EF Core 10.
    - Follow-ups for later slices: role checks (Phase 5: written by TenantAdmin, Supervisor and BillingClerk, read by
      everyone); audit records of the catalogue (Phase 3).
11. **Prompt 2.4 (bill drafts, ADR 0019), written 2026-11-20 on top of the uncommitted 2.2 and 2.3, not built on
    .NET 10 yet.** Three prompts are now in one working tree; commit them one by one, in order.
    - Built: `Bill` with `BillLine` in the states Draft and Voided, `LineCalculator` (BR-202), the six use cases,
      `Bills` and `BillLines`, `/api/v1/bills`, the guard against deletes, the unit of work that versions a bill
      with its lines, `IBillableParties` (Payers) and `ITenantSettingsReader` (Identity), and their tests.
    - Accepted by the owner on 2026-11-20: the amendment of ADR 0003 that lets `BillConfiguration` name `Patient`
      and `Payer` for the two foreign keys of `Bills` (a third listed exception in `ModuleDependencyTests`). The
      alternative written up there, a typed-id foreign key convention, is to be weighed again when the next table
      refers to another module's (payments, credit notes, adjustments, refunds).
    - **Awaits the owner's review** (decided while building, not in the approved plan): how the unit of work finds
      the roots of changed children (ADR 0019, point 5).
    - Changed in existing tests, each for a stated reason: `RelationalConventionTests` (one foreign key is
      `ClientCascade`, still `NO ACTION` in the database), `TenantQueryFilterTests` and `MigrationsOnSqlServerTests`
      (new tables and migrations), `ErrorCodesTests` (a sample `DateOnly`), and the probe `Widget`, which carries
      `IHardDeletable` so that the tenant-guard tests on a DELETE still reach the database.
    - Pre-checked in the .NET 9 copy: build, format gate, every unit test, and every integration test including
      the whole database collection on LocalDB 2019 (180 tests), with `0003_ServiceItems` and `0004_Bills`
      scaffolded there separately by EF Core 9. Failing there, as before and by design: the same eleven tests.
    - **To do on the personal machine:** scaffold the two migrations one after the other, as `docs/runbook.md`
      section 3 describes (the two Bill configurations are moved out for `0003_ServiceItems` and back for
      `0004_Bills`), check what each creates, copy the files back, then `dotnet build`,
      `dotnet format --verify-no-changes` and `dotnet test` with Docker.
    - Not proven anywhere yet: everything above on EF Core 10 and SQL Server 2022; in particular the test of ten
      simultaneous changes of the lines of one bill, which passes on LocalDB.
    - Recorded deviation: the grant of `DELETE` on `BillLines` cannot tell a draft's line from a posted bill's,
      which the posting slice has to settle. (The other one, `POST /api/v1/bills` without an `Idempotency-Key`,
      was closed by Prompt 3.2: both bill POSTs carry `[Idempotent]`.)
    - Follow-ups for later slices: the number, the fiscal year, the posted stamps, the four later statuses and
      their constraints (Prompt 3.5); `Idempotency-Key` on `POST /bills` (3.2); audit records of bills (3.4); role
      checks (Phase 5: bills are written by TenantAdmin, Supervisor and BillingClerk, read by everyone).
12. **Prompt 3.1 (fiscal year and gapless number series, ADR 0008), written 2026-11-20, not built on .NET 10 yet.**
    The first prompt of Phase 3, written while Phase 2 (2.2, 2.3, 2.4) is still uncommitted: four prompts are in
    one working tree. Phase 3 belongs on a branch `phase-3-posting` cut from `main` after Phase 2 is merged; sort
    the commits accordingly when the run on the personal machine is green.
    - Built: `FiscalYear`, `DocumentType`, `DocumentNumber`, the `NumberSeries` table, `INumberSeries` and
      `NumberSeriesAllocator`, the transaction that the allocator opens and the unit of work commits, the ban on
      raw SQL with its one sanctioned file, and their tests. No document takes a number yet.
    - Accepted by the owner on 2026-11-20 (found while testing, ADR 0008 built note): the first number of a new
      series can wait for a neighbouring series whose posting is in flight, another tenant's included. Once per
      series, for milliseconds, no deadlock. The alternative, insert-and-retry without the range lock, is written
      up there and was not chosen.
    - Changed in existing code and tests: `UnitOfWork.CommitAsync` commits or rolls back an open transaction (use
      cases that take no number are unaffected); `TenantQueryFilterTests`, `MigrationsTests` and
      `MigrationsOnSqlServerTests` know the new table and migration; `BannedSymbolsTests` has the second
      sanctioned file and pins the raw-SQL entries.
    - Pre-checked in the .NET 9 copy: build, format gate, every unit test, and every integration test including
      the whole database collection on LocalDB 2019 (212 tests), with `0003`, `0004` and `0005` scaffolded there
      separately by EF Core 9. The number series tests, with fifty parallel takers, ran eighteen times in a row
      without a failure. The raw-SQL entries of the ban list were compared with the EF Core 10.0.12 package in
      the NuGet cache: the ids are the same as on EF Core 9. Failing there, as before and by design: the same
      eleven tests.
    - **To do on the personal machine:** scaffold the three migrations one after the other, as `docs/runbook.md`
      section 3 describes, check what each creates, copy the files back, then `dotnet build`,
      `dotnet format --verify-no-changes` and `dotnet test` with Docker; then run the number series tests several
      times: `dotnet test --filter "FullyQualifiedName~NumberSeriesOnSqlServerTests"`.
    - Follow-ups for later slices: bill posting takes the first number (3.5); the typed wrappers come with their
      documents; changing a prefix is a maker-checker action (3.6, 5.3); the gapless verification query joins
      the reconciliation report (6.2).
13. **Prompt 3.2 (idempotency for financial POSTs, ADR 0006), written 2026-11-20, not built on .NET 10 yet.** The
    fifth prompt in one uncommitted working tree (2.2, 2.3, 2.4, 3.1, 3.2). The owner was advised twice to run the
    earlier prompts on the personal machine first and chose to go on; the first thing to do now is that run.
    - Built: `IdempotencyKey`, `IIdempotencyStore`, `IdempotencyStore`, the `IdempotencyKeys` table, `[Idempotent]`
      with `IdempotencyFilter`, the header and fingerprint rules, the three error codes, and their tests. Applied
      to `POST /api/v1/bills` and `POST /api/v1/bills/{id}/void`; proven on a test-only probe endpoint
      (`IdempotencyProbeController`) that takes a document number and saves a record.
    - **Accepted by the owner on 2026-11-20** (found while building, ADR 0006 built note): `TenantWriteGuardInterceptor`
      now treats a *temporary* tenant value on an added row as not set. `IdempotencyKeys` is the first table whose
      tenant is part of its primary key, and Entity Framework tracks such a row under a placeholder until the guard
      sets the tenant; the guard had refused it as another tenant's. A value a caller assigned is never temporary
      and is refused as before (two new tests in `TenantWriteGuardTests`).
    - Changed in existing code and tests: `UnitOfWork` takes an optional `RequestTransaction` and leaves the commit
      to the idempotency store while it holds the transaction; the test client's `PostJsonAsync` and
      `SendWithIfMatchAsync` send a new `Idempotency-Key` with every POST, as a well-behaved client does, so the
      existing bill tests needed no other change (one raw POST in `BillsApiTests` got the header by hand);
      `HardDeleteGuardTests` lists two deletable entities; `TenantQueryFilterTests`, `MigrationsTests` and
      `MigrationsOnSqlServerTests` know the new table and migration.
    - Pre-checked in the .NET 9 copy: build, format gate, every unit test, and every integration test including
      the whole database collection on LocalDB 2019, with `0003` to `0006` scaffolded there separately by EF
      Core 9. The idempotency and number series tests ran ten times in a row without a failure. Failing there, as
      before and by design: the same eleven tests.
    - **To do on the personal machine:** scaffold the four migrations one after the other, as `docs/runbook.md`
      section 3 describes, check what each creates, copy the files back, then `dotnet build`,
      `dotnet format --verify-no-changes` and `dotnet test` with Docker; then several runs of
      `dotnet test --filter "FullyQualifiedName~IdempotencyApiTests|FullyQualifiedName~NumberSeriesOnSqlServerTests"`.
    - Not proven anywhere yet: the response buffering of the filter on ASP.NET Core 10 (the tests run it through
      the test server on ASP.NET Core 9 here); everything on EF Core 10 and SQL Server 2022.
    - Not built, on purpose: the sweep of expired keys (the manual procedure is in `docs/runbook.md` section 7,
      with the one for a key stuck `InProgress`).
    - Follow-ups for later slices: every new financial POST gets `[Idempotent]` and its controller a line in
      `IdempotencyConventionTests` (3.5 onward); the scheduled sweep (Phase 8, or with the first background job);
      the grants of `docs/database.md` section 7 (5.4).
14. **Prompt 3.4 (domain event dispatch and the audit trail, ADR 0020), written 2026-11-20, not built on .NET 10
    yet.** The sixth prompt in one uncommitted working tree (2.2, 2.3, 2.4, 3.1, 3.2, 3.4). **Prompt 3.3, the
    receivables ledger, was skipped and is still to be given**; the owner chose to go on and to check later.
    - Built: the dispatch in `UnitOfWork`, `BeginCommit`/`EndCommit` with one clock reading per commit,
      `EntitySnapshot`, `IDomainEventSubscriber` and `DomainEventOccurrence`, `ICorrelationContext` with the API's
      `HttpCorrelationContext`, the Audit module (`AuditRecord`, `IAuditWriter`, `AuditSubscriber`, the
      `AuditRecords` table), `IAppendOnly` with `AppendOnlyGuardInterceptor`,
      `GET /api/v1/audit-records`, five new events for corrections, and their tests.
    - Approved by the owner with the plan on 2026-11-20, beyond the prompt's wording: the migration is
      `0007_Audit` and the receivables one becomes `0008_Receivables`; a correction raises an event only when a
      value changed, so a PATCH that changes nothing writes no audit record and leaves the version alone; the
      append-only guard, which Prompt 3.3 was to bring, is built here.
    - Changed in existing code and tests: `UnitOfWork` takes the subscribers as an optional third argument and
      does its preparation inside its `try`; the context creates its interceptors once and holds them;
      `PatientTests` expected no event after an update and now expects `PatientUpdated` (a changed expectation,
      called out in ADR 0020); `TenantQueryFilterTests`, `MigrationsTests` and `MigrationsOnSqlServerTests` know
      the new table and migration.
    - Pre-checked in the .NET 9 copy: build, format gate, Domain 428 and Application 222 tests green, Architecture
      104 green and Integration 826 green on LocalDB 2019 with `0003` to `0007` scaffolded there separately by EF
      Core 9. Failing there, as before and by design: the same eleven tests. The audit, unit-of-work, idempotency
      and bill API tests (152) ran three times in a row without a failure.
    - **To do on the personal machine:** scaffold the five migrations one after the other, as `docs/runbook.md`
      section 3 describes, check what each creates, copy the files back, then `dotnet build`,
      `dotnet format --verify-no-changes` and `dotnet test` with Docker.
    - Not proven anywhere yet: everything on EF Core 10 and SQL Server 2022; in particular the images of
      `EntitySnapshot`, which read Entity Framework's original values and type mappings.
    - **Open point for the owner:** the audit images copy patient data (names, phone numbers, e-mail addresses)
      into a table that is never changed or deleted. Same database, same tenant filter, never a log; a later
      erasure or retention rule has to take it into account (ADR 0020, Consequences).
    - Not built, on purpose: the grants that refuse UPDATE and DELETE on `AuditRecords` (5.4); a role check on
      the audit endpoint (Phase 5: readable by everyone per US-43); `DocumentNumber` in a record (3.5).
    - Follow-ups for later slices: every new aggregate raises an event for every state change and gets a test
      that counts one record per use case; the ledger entities carry `IAppendOnly` (3.3); approvals, number
      series prefix changes, users and tenant settings are audited when they are built (BR-701).
15. **Order of work decided by the owner on 2026-11-20, and decisions for Prompts 3.3 and 3.5.**
    - **Order** (changed by the owner later the same day, see item 16): 2.2 to 3.4 are committed unverified, one
      commit per prompt; then 3.3 alone; 3.5 after it. One prompt at a time, never two together, and none before
      the one before it is committed.
    - **The decisions themselves are in `docs/build-prompts.md`**, under prompts 3.3 and 3.5 ("Owner's decisions"):
      the renumbered migrations (`0008_Receivables` to `0015_Users`), how the `PayerBalances` row is created, no
      new module-dependency exception without approval, the data step of `0008` and its ADR, the zero-total bill,
      the lock order and its ADR, the three idempotency and concurrency cases with their error codes, and the
      extra tests.
    - **Settled by the owner on 2026-11-20, before 3.3 was coded:** the port opens the `PayerBalances` row; the
      foreign keys to `Payers` come from the typed-id convention, with no new exception; the ledger and the
      reconciliation are sanctioned raw-SQL files; no `LastPostingId`; `PostedByUserId` nullable without a key
      (ADR 0021, ADR 0005 built note; item 17).
    - **Settled by the owner on 2026-11-20, before 3.5 was coded:** BR-401 and identity 2 are amended for a bill
      that comes to nothing; a second posting gets 409 `concurrency_conflict` for a stale version and 409
      `bill_already_posted` for the current one (ADR 0022; item 18).
    - **Temporary, to be removed by Prompt 3.6:** a bill at or above `Tenant.PostingApprovalThreshold` is refused
      with 422 `bill_requires_approval` (BR-207). Maker-checker replaces the refusal with the `PendingApproval`
      state; then `BillingErrors.BillRequiresApproval`, its check in `PostBillHandler`, its tests and its lines in
      `docs/api.md` section 8.4 go.
    - **A first draft of 3.3 and 3.5** was written on 2026-11-20 before these decisions and taken out of the
      repository again the same day; the tree was checked to be back at the state of item 14 (same build, format
      and test results in the .NET 9 copy). The draft is parked in `C:\aptran2026\BillingRecon\parked-3.3-3.5-draft`,
      outside the repository, as reference only: it does not match the decisions above.
16. **Verification on the personal machine is deferred (owner's decision, 2026-11-20).** The owner will run it
    later and building goes on meanwhile. Until then, read every "to do on the personal machine" of items 9 to 14
    as still owed, and say "unverified on .NET 10" of everything after Prompt 2.1.
    - **The one running list of what is owed is `docs/runbook.md` section 8**: the commands in order, every
      migration scaffold with the table it must create, the concurrency tests to repeat, and what to send back.
      A prompt that adds a migration or a check adds its line there; nothing is removed until the owner has run it.
    - **Commits:** Prompts 2.2, 2.3, 2.4, 3.1, 3.2 and 3.4 are committed on `phase-2-billing`, one commit per
      prompt, each message ending "UNVERIFIED on .NET 10, migrations not scaffolded". A file is in the commit of
      the prompt that introduced it, in its final state, so a file that later prompts changed carries their
      changes already: **only the last of the six commits is a state that builds**. Shared documents (this file,
      the README, the documents of `docs/` that are not ADRs) are in the last one. Recorded here so that nobody
      bisects those commits or checks one out expecting it to compile.
    - **Migrations `0003` to `0007` are not in those commits.** They come as a commit of their own after the run
      of section 8. Until then `MigrationsTests` and the whole database collection fail on a fresh clone, by
      design.
    - **Branch:** Phase 3 work (3.1, 3.2, 3.4 and what follows) is on `phase-2-billing` too, because the owner
      asked for the current branch; the branch-per-phase rule of item 8 resumes when the owner says so.
    - **If the run of section 8 is red:** the fix is a commit on top, with the output that showed it.
17. **Prompt 3.3 (receivables ledger; ADR 0005 built, ADR 0021), written 2026-11-20 after 3.4, not built on
    .NET 10 yet.** Verification is deferred with the rest (item 16).
    - Built: `LedgerPosting` and `LedgerEntry` (append-only, balanced, the six accounts and subject rules of
      BR-403), `PayerBalance`, the port `IReceivablesLedger` with `PostingTemplate`, `IPayerAccounts` (called by
      the use case that creates a payer), `IReconciliationQuery`, the three tables, the typed-id foreign key
      convention in `BillingReconDbContext`, and their tests.
    - Decided by the owner before coding: see item 15 and ADR 0021.
    - Changed in existing code and tests: `CreatePayerHandler` takes `IPayerAccounts` and opens the balances;
      `Program.cs` calls `AddReceivables`; `BannedSymbolsTests` lists three raw-SQL files and checks each;
      `TenantQueryFilterTests`, `AppendOnlyGuardTests`, `MigrationsTests` and `MigrationsOnSqlServerTests` know
      the new tables and migration; `PayerHandlerTests` expects the balances to be opened once on success and
      never on a refusal.
    - **Repaired in the same commit:** `docs/database.md` had lost its title, and two passages of Prompt 3.4 (the
      built note of section 3.8 and two rows of the migration table) sat at the top of the file, put there by a
      scripted edit that went wrong and was committed unnoticed in `7fc0cda`. Both are where they belong now.
    - Pre-checked in the .NET 9 copy: build, format gate, Domain 449 and Application 222 tests green,
      Architecture 108 green and Integration 867 green on LocalDB 2019, with `0003` to `0008` scaffolded there
      separately by EF Core 9 and the data step added to `0008` by script. Failing there, as before and by
      design: the same eleven tests. The ledger, foreign-key, payer and migration tests (87) ran five times in a
      row without a failure. Migrations `0003` to `0007` hold the same foreign keys with the convention as
      without it.
    - **To do on the personal machine:** `docs/runbook.md` section 8, which now ends with `0008_Receivables` and
      the one line to paste into it by hand.
    - Not proven anywhere yet: the convention, the locked balance change, the computed column and the
      reconciliation statement on EF Core 10 and SQL Server 2022.
    - Recorded for the owner: the exception that lets `BillConfiguration` name `Patient` and `Payer` is no longer
      needed (the convention would add the same two keys under the same names) and can be removed whenever wanted;
      it was left alone because nobody asked for a change to Prompt 2.4. When `Users` enters the model
      (Prompt 5.1), every user column, the audit record's and the idempotency key's included, gets a foreign key
      by the convention unless that prompt decides otherwise.
    - Not built, on purpose: an endpoint for the reconciliation (6.2); reads of a payer's balances (6.1); the
      bill identities of the reconciliation and `PostingTemplate.BillPosted` (3.5); the error code
      `ledger_balance_negative` is not in the catalogue of `docs/api.md` yet, because no endpoint can answer it.
    - Next: Prompt 3.5 (item 18).
18. **Prompt 3.5 (post a bill, ADR 0022), written 2026-11-20, not built on .NET 10 yet.** Verification is deferred
    with the rest (item 16).
    - Built: `Bill.Post` and `CheckCanPost`, the statuses `Posted`, `PartiallyPaid` and `Settled`, `BillPosted`,
      `PostBillHandler`, `POST /api/v1/bills/{id}/post` (`[Idempotent]`, `If-Match`),
      `PostingTemplate.BillPosted`, the bill identities of the reconciliation, the document number in the audit
      record (`IHasDocumentNumber`), `BillPostingConfiguration`, and their tests.
    - Decided by the owner: see item 15 and ADR 0022. BR-401 is amended in `docs/requirements.md`.
    - **Temporary (BR-207):** `bill_requires_approval`, see item 15. Prompt 3.6 removes it.
    - Changed in existing code and tests, each for a stated reason: a posted bill answers `bill_already_posted`
      (409) where every non-draft answered `bill_not_draft` (422), which stays for a voided draft;
      `ReplaceBillLinesHandler` asks the bill (`CheckIsDraft`) instead of comparing the status itself;
      `TenantDocumentSettings` carries the threshold; `IBillableParties` has `EnsureCanOweAsync`; the bill DTOs
      have `number`, `fiscalYear` and `postedAtUtc`, so **`BillsApiTests.A_draft_has_no_number_in_any_form` asserts
      null members where it asserted absent ones** (a changed expectation, called out in ADR 0022);
      `BillsSchemaTests` and `BillsOnSqlServerTests` list the new columns, indexes and constraints;
      `DatabaseApiFactory` takes a `configure` action so that a test can make one port fail.
    - **Two configurations for `Bills`:** `BillConfiguration` ignores the five properties of posting and
      `BillPostingConfiguration` names them again, so that `0004_Bills` and `0009_BillPosting` are two migrations
      although neither is scaffolded yet. Merge the two classes once both migrations are in the repository.
    - Pre-checked in the .NET 9 copy: build, format gate, Domain 461 and Application 238 tests green,
      Architecture 108 green and Integration 892 green on LocalDB 2019, with `0003` to `0009` scaffolded there
      separately by EF Core 9. Failing there, as before and by design: the same eleven tests. The posting and
      ledger tests (41) ran eight times in a row without a failure.
    - **To do on the personal machine:** `docs/runbook.md` section 8, which now ends with `0009_BillPosting`;
      mind that `BillPostingConfiguration.cs` comes back last.
    - Not proven anywhere yet: the split of the two configurations on EF Core 10; every concurrency test on SQL
      Server 2022; the failure injection through the real pipeline on ASP.NET Core 10.
    - Follow-ups for later slices: maker-checker and `PendingApproval` (3.6); `PartiallyPaid` is reached by
      allocations (4.2); the reconciliation endpoint and the gapless check in the report (6.2); role checks
      (Phase 5: bills are posted by TenantAdmin, Supervisor and BillingClerk).
    - Next: Prompt 3.6.
