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
| Web API | **ASP.NET Core Web API with controllers** | No minimal APIs. Controllers derive from `ApiControllerBase`, are `sealed`, end in `Controller` (architecture test). Every route is under `/api/v1` (ADR 0013). Every error is an RFC 9457 ProblemDetails (ADR 0011). Health checks at `/health/live` (process is up) and `/health/ready` (the application database is reachable and has no pending migration), both anonymous. **Transport (ADR 0035):** TLS ends at a reverse proxy; forwarded headers are believed from the loopback addresses and `ReverseProxy:KnownProxies` only; outside Development a request that did not arrive over HTTPS gets 400 `https_required` (no redirect) and an answer over HTTPS carries HSTS for one year; a host `AllowedHosts` does not list gets 400 `host_not_allowed`; the health endpoints answer probes over plain HTTP for any host; CORS from `Cors:Origins` only, without credentials; no `Server` header; `nosniff` on every response, `no-store` and a content security policy on the answers of `/api`. |
| Authentication | **JWT bearer** (`Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.12) | The API issues its own tokens at `POST /api/v1/auth/login` and checks them on every request: HS256 with `Jwt:SigningKey`, issuer `BillingRecon`, audience `BillingRecon.Api`, 60 minutes by the application's clock, claims `sub`, `tenant_id`, `role`. The tenant and user of a request come from its token only (`RequestPrincipalIdentity`). Deny by default: a fallback policy needs a user on every endpoint but the sign-in, the health checks and, in Development, OpenAPI and Scalar (an architecture test lists them). In Development a request without a token acts as the development user. ADR 0031. **Roles:** every action names exactly one policy of `Policies`, one per capability of the permission matrix (`docs/requirements.md` section 1); a role the policy does not admit gets 403 `forbidden`; a table-driven test walks every endpoint with every role. The checker's role (BR-603) is also checked by `ApprovalRequest` itself. ADR 0032. **Sign-in limits (ADR 0035):** 30 sign-ins a minute per client address (the rate limiter's policy `sign-in`) and an account held 15 minutes after 10 refusals (`SignInThrottleFilter`), both 429 `too_many_sign_ins` with `Retry-After`; fixed in code, in memory per instance. |
| OpenAPI | **Built-in OpenAPI + Scalar UI**, Development only | `AddOpenApi`/`MapOpenApi` (document at `/openapi/v1.json`) and `MapScalarApiReference` (`/scalar`), mapped only when the environment is Development. |
| Data | **EF Core 10 + SQL Server** | One context, `BillingReconDbContext`, whose conventions do the repetitive work (ADR 0015, section 5 below). Migrations live in `Infrastructure/Common/Migrations`, are applied at startup in Development only, and are a deployment step elsewhere. `dotnet-ef` is a local tool (`.config/dotnet-tools.json`, `dotnet tool restore`) at the same version as the EF Core packages; it uses the Infrastructure project and its design-time factory, never the API. SQL Server 2022 in Docker for dev and tests. **Logins (ADR 0034):** outside Development the API connects as `billingrecon_app`, a member of the database role `billingrecon_application` alone, which migration `0020_DatabaseRoles` creates with its grants (`docs/database.md` section 7); the migrations run as `billingrecon_migrator` (`db_ddladmin`, `db_datareader`, `db_datawriter`, `db_securityadmin`); the logins are made per environment (`docs/runbook.md` section 10). The database has `READ_COMMITTED_SNAPSHOT ON`, as EF Core creates one: the application relies on readers not waiting for writers. |
| Banned APIs | **Microsoft.CodeAnalysis.BannedApiAnalyzers** | `src/BannedSymbols.txt` lists what production code must not call (system time and the machine's time zone, `IgnoreQueryFilters`, `EntityEntry.Reload`/`GetDatabaseValues`, `ExecuteUpdate`/`ExecuteDelete`, and since Prompt 3.1 every raw-SQL entry point of EF Core: `ExecuteSql*`, `SqlQuery*`, `FromSql*`, `GetDbConnection`); a call is build error RS0030. Six files may suppress it, each for one named reason: `SystemClock` (the system time); four that send constant, parameterised, tenant-bound SQL statements: `NumberSeriesAllocator` (two: the locked read-and-increment of a series, and since 2026-11-23 the same locked read alone, with which an approved prefix change holds its series, ADR 0033 "Amended 2026-11-23"), `ReceivablesLedger` (one: the locked change of a payer's balances), `ReconciliationQuery` (one, read-only) and, since Prompt 6.3, `LedgerMovementsQuery` (two, read-only: the ledger's balances before a date and its movements by business date, for the dashboard, ADR 0037); and `LoginUserLookup`, the one `IgnoreQueryFilters`, around one query with the tenant in its predicate, because a sign-in has no tenant yet (ADR 0031). A seventh needs an ADR. Tests guard the list and where the rule may be suppressed (ADR 0015, ADR 0021). |
| Validation | **FluentValidation** (12.2.0) | One public validator per command or query that has a shape to check, in the Application layer, run by its handler as the handler's first step; it checks the shape (presence, lengths, formats, ranges), the domain and the handler the business rules. A failure is 400 `validation_failed` with messages per field (ADR 0018). The API registers the validators with its scan of the Application assembly. |
| Logging | **Serilog** | Compact JSON on the console, plus Seq when `Seq:ServerUrl` is set. Every event of a request carries CorrelationId (from `X-Correlation-Id` or generated, BR-703), and TenantId and UserId when the request is authenticated. Phone numbers and e-mail addresses are masked before any sink (ADR 0012). |
| Configuration | **appsettings + environment variables + user-secrets** | One strongly typed options class per section, validated at startup: the API refuses to start when misconfigured. Secrets (`Database:ConnectionString`, `Seq:ApiKey`, `Jwt:SigningKey`) come from user-secrets or environment variables, never from a file in git; the one key in git, Development's signing key, is refused outside Development. Outside Development `AllowedHosts` is required (no `*`); `ReverseProxy:KnownProxies` and `Cors:Origins` are checked at startup (ADR 0035, `docs/runbook.md` section 11). |

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

React + TypeScript + Vite in `frontend/` (ADR 0038, Prompt 7.1). Node 22.22 or later (`engines`; CI uses Node 24).

| Concern | Choice | Notes |
|---|---|---|
| Stack | **React 19, TypeScript 5.9, Vite 8** | TypeScript stays on 5 until typescript-eslint and openapi-typescript accept a later one. Static files, served by the proxy of Prompt 8.1. |
| Routing | **React Router 8**, data mode | `createBrowserRouter`, `RouterProvider` from `react-router/dom`; no framework mode, no server rendering. |
| Data | **TanStack Query** | A 4xx is never retried; the cache is cleared when a session ends. |
| API client | **openapi-fetch** over types from **openapi-typescript** | Generated from `src/api/openapi.json`, the API's own document, which `OpenApiSnapshotTests` (.NET) keeps equal to the API's; `npm run api:types` regenerates `src/api/schema.d.ts`, and CI fails when it differs. Every request carries the token and an `X-Correlation-Id` of its own; a 401 to a request that carried a token ends the session. |
| Forms | **React Hook Form**, no schema library | The API validates; its messages per field go on the fields. |
| Components | Our own, **CSS Modules** on the **design tokens** of `src/theme/tokens.css`; no UI kit | No raw colour outside `tokens.css` (a test scans every file); WCAG AA in both themes (a test computes the contrast). The theme (System, Light, Dark) is `data-theme` on `<html>`, set by the inline script of `index.html` before React mounts; its SHA-256 is pinned by a test for the CSP of 8.1. |
| Errors | **`ProblemAlert`** | The one way a screen shows a refusal: the API's message, field messages not placed on fields, the wait of a 429, the reference (correlation id). |
| Screens (ADR 0039) | Lists, records, forms | A list keeps its search, status and page in the address. Every change sends `If-Match` from the record's `version`. Who may change is `src/auth/permissions.ts`, a copy of the API's policies that a test holds to `Policies.cs`. Deactivate asks once more, in place. A 409 `concurrency_conflict` says nothing was saved, lists what changed, and offers "Load the latest version" or "Keep my changes". Decimals are typed as text, sent as JSON numbers, refused beyond 15 significant digits; amounts shown with Indian grouping. The API's field messages go at their fields. |
| Bills (ADR 0040) | One editor for a draft; the key of an operation | A new draft is one `POST` with its lines; a correction sends what changed, the header (`PATCH`) then the lines (`PUT`) with chained versions. Create, void and post carry an `Idempotency-Key` from `useIdempotentRequest`: the same key when the same request is sent again after no answer, a fault or 409 `idempotency_in_progress`, a new one otherwise. Live totals are an estimate by exact arithmetic on `BigInt` (`src/bills/lineMath.ts`), held with the server's `LineCalculator` to one file of cases (`src/bills/lineCases.json`); after a save the server's figures are shown. Posting answers posted (200) or sent for approval (202). Every record's history is the `AuditTrail` component. |
| Session | **`sessionStorage`** | The token, its expiry and the roles read from it; gone with the tab, at expiry and on a 401 (row S5). The roles choose the navigation only; the API is the authority. |
| Charts (7.6) | **Recharts** | SVG, so a series drawn with `var(--series-n)` follows the theme without a re-render. |
| Tests | **Vitest**, jsdom, React Testing Library | The API faked by a `fetch` given to the client (`src/test/app.tsx`); no mocking package. |
| Lint and format | **ESLint 9** (type-aware TypeScript, hooks, `jsx-a11y`, no `dangerouslySetInnerHTML`), **Prettier** | ESLint stays on 9 until `jsx-a11y` supports 10. Warnings are errors. One `eslint-disable`, in a test, with its reason. |
| Packages | 27, pinned exactly, `package-lock.json` committed | `npm ci` in CI; `npm audit` fails on any advisory; Dependabot a week after a release. A new package needs approval (section 6). |

### Delivery

Docker Compose for local runs: `sql` (SQL Server 2022 Developer) and `seq` exist in `docker-compose.yml`; `api`
and `frontend` join later. Licence acceptance and the SQL Server password come from `.env` (copy `.env.example`),
which git ignores. The API has a multi-stage image, `src/BillingRecon.Api/Dockerfile` (Debian-based runtime with
time zone data, non-root user, health check; ADR 0017). GitHub Actions, `.github/workflows/ci.yml`, runs on every
push to `main` and every pull request: restore with the NuGet audit, `dotnet format --verify-no-changes`, build
with warnings as errors, every test including the database tests (the runners have Docker), coverage and a
vulnerability report as artifacts, the idempotent migration script, and the image built and smoke-run; and since
Prompt 7.1 a third job, the frontend: `npm ci`, `npm audit`, format, lint, the generated API types against the committed
document, types and build, tests. Dependabot keeps NuGet, npm and the actions current (`.github/dependabot.yml`). Line endings are LF everywhere
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
- `node --version`: **v24.22.2**, npm 11.19.1 (recorded 2026-11-23; it was v6.10.1 on 2026-09-26). The frontend builds
  and tests here, and the npm registry is reachable.
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
  LocalDB here runs in mixed mode (SQL logins work), so the fixture's migrator and application logins (ADR 0034) run
  on it too; it keeps its logins between runs, and the fixture gives them a new password each run. It also keeps the
  databases `BillingRecon_Pending` and `BillingRecon_Development`, which `ReadinessOnSqlServerTests` and
  `DevelopmentOnSqlServerTests` need to start out missing, as they do in a new container: drop both before a run
  (`sqlcmd -S "(localdb)\MSSQLLocalDB" -E -C`), or those two classes fail on the second run (found 2026-11-23).
- **Since 2026-11-21 this machine has the .NET 10 SDK (10.0.404), installed by the owner.** `dotnet build`,
  `dotnet format` and `dotnet test` work here, and so does the repository's `dotnet-ef` 10.0.12. What still cannot
  run here is the `Database` collection as written: it needs Docker. **Since 2026-11-21 the migrations `0001` to
  `0015` are in the repository**, so the build, the format gate and every test outside the `Database` collection run
  in the repository itself; the `Database` collection runs in a throwaway copy whose database fixture is pointed at
  LocalDB 2019, with no version substitution. **`dotnet ef` does not run in the repository on this machine:**
  `.config/dotnet-tools.json` carries the Windows mark of a file that came from another computer (the tree came
  from a zip), and the SDK refuses local tools from it. The owner can clear it (the file's Properties, Unblock);
  until then a migration is scaffolded in the copy and its files are copied back, which gives the same files.
  Say "built and tested on .NET 10 with LocalDB 2019, not run on SQL Server 2022". The .NET 9
  procedure below is history, kept for what was verified with it.
- **Until 2026-11-21: no .NET 10 SDK on this machine, and the owner had decided not to add it** (recorded
  2026-10-03): it was installed only on the owner's personal machine. The repository targets .NET 10
  (ADR 0014), so `dotnet build` and `dotnet test` did not work here. Code written here was pre-checked in a
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
                                Settled, BillPosted, and IHasDocumentNumber (Common). Since Prompt 3.6
                                (ADR 0023): the ApprovalRequest aggregate with its enums and events (Common),
                                and the bill's PendingApproval status with SubmitForApproval, PostApproved and
                                ReturnToDraft. Since Prompt 4.1: the Payment aggregate with PaymentMethod,
                                PaymentStatus, PaymentRecorded and PaymentsErrors (Payments); PaymentId and
                                BusinessDateWindow, which bills and payments share (Common). Since Prompt 4.2
                                (ADR 0024): PaymentAllocation, a child of Payment, with Payment.Allocate and
                                PaymentAllocated (Payments); Bill.ApplyCredit with BillCreditApplied (Billing);
                                PaymentAllocationId (Common). Since Prompt 4.3 (ADR 0025): the CreditNote
                                aggregate with CreditNoteLine and CreditNoteAllocation, its statuses and events
                                (Billing); CreditNoteId, CreditNoteLineId and CreditNoteAllocationId (Common).
                                Since Prompt 4.4 (ADR 0026): the Adjustment aggregate with AdjustmentKind,
                                AdjustmentStatus, its events and AdjustmentsErrors (Adjustments); AdjustmentId
                                (Common). Since Prompt 4.5 (ADR 0027): the reversal of an allocation and of a
                                payment on the Payment aggregate, with the states PendingReversal and Reversed
                                and six events (Payments); Bill.ReverseCredit with BillCreditReversed (Billing).
                                Since Prompt 4.5b (ADR 0028): the reversal of a later application of credit on
                                the CreditNote aggregate, ApplyToOwnBillAtPosting and the AppliedAtPosting flag,
                                the states PendingReversal and Reversed of CreditNoteAllocation, and three
                                events (Billing). Since Prompt 4.6 (ADR 0029): the Refund aggregate with
                                RefundSource, RefundSourceKind, RefundStatus and six events, Payment.PayRefund
                                with PaymentRefunded (Payments); CreditNote.PayRefund with CreditNoteRefunded
                                (Billing); RefundId (Common). Since Prompt 5.1 (ADR 0030): the User aggregate with
                                UserRole, Role, UserStatus, RoleDefinition (a row of the global Roles table) and
                                six events (Identity). Since Prompt 5.3b (ADR 0033): Tenant.ChangeSettings and
                                ChangePrefix with two events, the PrefixChange aggregate with PrefixChangeStatus
                                and three events (Identity); NumberSeries an aggregate with ChangePrefix and
                                NumberSeriesPrefixChanged, PrefixChangeId (Common).
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
                                (Billing, Receivables; ADR 0022). IApprovalRequestRepository and the list of
                                approval requests (Common); ApproveBillPostingHandler and
                                RejectBillPostingHandler (Billing; ADR 0023). RecordPaymentHandler with the get
                                and list queries, IPaymentRepository and IPaymentReadService (Payments);
                                IActivePayers, what Payers answers when money is received (Payers);
                                PostingTemplate.PaymentRecorded, and IReceivablesLedger.PostAsync now answers
                                the id of the posting it entered (Receivables). AllocatePaymentHandler, the use
                                case that changes a payment and its bills in one commit, and the query of a
                                payment with its allocations (Payments); IBillSettlement, what Billing answers
                                when a bill is settled (Billing); PostingTemplate.PaymentAllocated and
                                LedgerRefusal (Receivables). ADR 0024. The credit note use cases (draft,
                                replace lines, update header, void, post, approve, reject, apply, get, list)
                                with ICreditNoteRepository and ICreditNoteReadService (Billing);
                                PostingTemplate.CreditNotePosted and CreditApplied (Receivables). ADR 0025.
                                The adjustment use cases (draft, update, void, post, approve, reject, get,
                                list) with IAdjustmentRepository and IAdjustmentReadService (Adjustments);
                                IBillSettlement.FindPostedAsync with PostedBill (Billing);
                                PostingTemplate.AdjustmentPosted (Receivables). ADR 0026. The six reversal use
                                cases (request, approve and reject, for an allocation and for a payment;
                                Payments); IBillSettlement.RestoreAsync (Billing);
                                PostingTemplate.AllocationReversed and PaymentReversed (Receivables);
                                PendingApprovals.FindPendingAsync, how every approve and reject use case finds
                                its request (Common). ADR 0027. The three use cases that reverse an
                                application of credit (request, approve, reject; Billing), the approval reaching
                                the bill through IBillSettlement.RestoreAsync; PostingTemplate.
                                CreditApplicationReversed (Receivables). ADR 0028. The refund use cases (draft,
                                replace, void, post, approve, reject, get, list) with IRefundRepository and
                                IRefundReadService, and the refund check of the payment reversal request
                                (Payments); ICreditNoteSettlement, what Billing answers when a credit note pays a
                                refund (Billing); PostingTemplate.RefundFromPaymentPosted and
                                RefundFromCreditNotePosted (Receivables). ADR 0029. The user use cases (create,
                                update, replace roles, disable, enable, reset password, get, list) with
                                IUserRepository, IUserReadService and IPasswordHasher (Identity). ADR 0030. The
                                settings use cases (get, update) and the prefix change use cases (request,
                                approve, reject, get, list) with ITenantRepository, IPrefixChangeRepository,
                                IPrefixChangeReadService and IPrefixUsage (Identity); INumberSeriesRepository
                                (Common). ADR 0033. The eight report queries with their handlers, validators and
                                DTOs, ReportPage (the list answer with its totals and period), ReportPeriods, the
                                ports IRegisterReports, ICollectionReports, IBalanceReports and IApprovalReports, and
                                Reporting's own words ReportBillStatus, ReportPaymentStatus, ReportPaymentMethod
                                (Reporting). ADR 0036. The reconciliation report (ReconciliationReportQuery and its
                                handler over IReconciliationQuery, DriftRowDto, ReconciliationTotalsDto) (Reporting)
                                and the identity NumberSeriesGap (Receivables), ADR 0036 amended by Prompt 6.2.
                                The dashboard (ADR 0037): the summary, trend and ageing queries with their handlers,
                                validators and DTOs, DashboardPeriods, DashboardFigures, DashboardSeries, the port
                                IDashboardReports, the recent payments over the payment register, and ReportOrder,
                                the register's order (Reporting); ILedgerMovements with LedgerBalances and LedgerDay,
                                the ledger by business date (Receivables).
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
                                typed-id foreign key convention of the context (ADR 0021). The Payments table
                                with its repository and read service, registered by AddPayments (Payments).
                                The PrefixChanges table with its repository and read service, PrefixUsage and
                                TenantRepository (Identity); NumberSeriesRepository (Common/Numbering). ADR 0033.
                                Migrations go to Common/Migrations; `0020_DatabaseRoles` is the application's role
                                and its grants, written by hand (ADR 0034). RegisterReports, CollectionReports,
                                BalanceReports and ApprovalReports, which only read, with ReportMapping and AddReporting
                                (Reporting; the one folder that may name every business module's entities, ADR 0036).
                                DashboardReports, the open bills and the approval requests' instants (Reporting), and
                                LedgerMovementsQuery, the sixth file with raw SQL (Receivables), ADR 0037.
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
                                The payments controller with its request records (Payments). Common/Authentication
                                (ADR 0031): JwtOptions, the token issuer, the claims, RequestPrincipalIdentity (the
                                ITenantContext and ICurrentUser of a request), HttpSignInScope, the Development
                                scheme and AddRequestIdentity; the sign-in controller AuthController next to
                                UsersController (Identity). Common/Authorization (ADR 0032): Policies, the named
                                policies of the permission matrix with the roles each admits, and AddPermissions,
                                which also sets the fallback policy (deny by default). SettingsController and
                                PrefixChangesController with their request records (Identity), ADR 0033.
                                Common/Transport (ADR 0035): AddTransport (forwarded headers from known proxies,
                                allowed hosts, CORS, HSTS, no Server header) with ReverseProxyOptions,
                                AllowedHostsOptions and CorsOriginsOptions, TransportGuardMiddleware (hosts, then
                                HTTPS outside Development) and SecurityHeadersMiddleware; HealthEndpoints (Common);
                                ApiProblems, the ProblemDetails of a refusal made outside an action (Common/Errors);
                                SignInLimits, AccountSignInLimiter and the SignInThrottle filter of the sign-in
                                (Identity). ReportsController, eight GET reports under /api/v1/reports, with their
                                request records (Reporting), ADR 0036. DashboardController, seven GET panels under
                                /api/v1/dashboard, and the policy Dashboard (Reporting), ADR 0037. Program.cs gives
                                the JSON options the OpenAPI document reads the controllers' enum converter, so the
                                document says what the wire carries (ADR 0038, built note).
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
                                   against it acting for a tenant. The migrated database is made as the runbook makes
                                   one: migrated by a login with the migrator's four roles, used by a login with the
                                   application's role alone, so the API host and the production contexts run with the
                                   production grants; a defect seeded on purpose uses AdministratorApiContext (ADR
                                   0034). Support/: FakeClock, FixedIdentity,
                                   ThrowingIdentity, the CreateClientFor helper (every request carries a bearer token
                                   minted for it by the API's own issuer, ADR 0031), TestRoles, the Bogus-based TestData.
                                   Every host is called at https://localhost with AllowedHosts=localhost, as a client
                                   of a deployment is; RemoteAddressStartupFilter gives a request a client address
                                   (header X-Test-Remote-Address); TransportApiFactory has a proxy, named hosts and a
                                   frontend origin; KestrelTransportTests host the API on real Kestrel (ADR 0035).
                                   After every test of the Database collection, DatabaseTest runs the reconciliation of
                                   every tenant and fails on drift, except the identities a test names with ExpectDrift
                                   or its class with DriftLeftOnPurpose (ADR 0036, Prompt 6.2).
  BillingRecon.ArchitectureTests/  NetArchTest and file scans: layer rule, module rule with its listed exceptions,
                                   every type in a known module, banned packages in every project file, controller
                                   conventions, no minimal APIs; the guard of the banned-API list
                                   (BannedSymbolsTests); the one anonymous action and the one user of the sign-in
                                   scope, and the sign-in's two rate limits on it alone (AuthenticationConventionTests);
                                   one policy of the catalogue on every action,
                                   and the checker's roles in every decision (PermissionConventionTests); Reporting
                                   writes nothing (ReportingConventionTests); docs/api.md names every endpoint and
                                   every error code (ApiDocumentationTests).
frontend/                      React + TypeScript + Vite (ADR 0038): index.html with the inline theme script;
                               src/theme (tokens.css, the provider and the chooser), src/auth (the session, the
                               sign-in, the route guard), src/api (openapi.json, the generated schema.d.ts, the client,
                               ProblemDetails as ApiProblem, call.ts: answer, ApiError, ifMatch), src/components
                               (ProblemAlert, the fields, the list controls with listParams, the page header, the status
                               badge, the conflict notice, the inline confirmation, useRecordForm, useStatusActions),
                               src/shell (the navigation from the permission matrix, the shell, the placeholder pages,
                               RequirePolicy), src/patients, src/payers and src/serviceItems (each: api.ts with its queries
                               and changes, the list, the record and the forms; ADR 0039), src/bills (api.ts, the list, the
                               view with useBillActions, the editor with LinesEditor, the pickers, lineMath.ts and
                               lineCases.json; ADR 0040), src/components also RecordPicker and AuditTrail, src/api also
                               useIdempotentRequest, src/auth/permissions.ts (the
                               API's policies), format.ts and decimals.ts, routes.tsx, Providers.tsx; src/test (the app
                               renderer, routedApi, the records, the colour scan, the contrast and theme-script tests).
                               The OpenAPI document is kept equal to the API's by OpenApiSnapshotTests (IntegrationTests).
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
- `Reporting` reads: its Infrastructure may name the entities of every business module, to query them through the
  context, and it writes nothing; its Application and Api layers follow the rule like any module (ADR 0036, ADR 0003
  amendment of 2026-11-23, `ReportingConventionTests`).
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
   As built for its first case, the bill (ADR 0023): the maker's own request to the document answers
   202 and opens an `ApprovalRequest`; `approve` and `reject` are POST actions of the document, with
   their handlers in the document's module; the request is only ever read
   (`GET /api/v1/approval-requests`). The maker can decide nothing: 403 `approval_checker_is_maker`.
   Every later maker-checker document follows this shape. The checker's role is checked from
   Prompt 5.3 on; until then any second user of the tenant can decide.

10. **No hard deletes of financial data** (BR-005, BR-102, BR-105). No `DELETE` statements and no
    `DbSet.Remove` against financial tables; the application login has no DELETE grant on them
    (built with Prompt 5.4: the role `billingrecon_application`, ADR 0034, `docs/database.md` section 7).
    `ExecuteDelete` is banned outright (ADR 0015).
    Lifecycle is by status (Draft, PendingApproval, Posted, PartiallyPaid, Settled, Voided,
    Reversed) and by reversal documents. Drafts are voided, not deleted. Patients, payers and
    service items are deactivated by status, never deleted. **Two exceptions, neither a financial
    record:** a line of a *draft* bill is deleted when the draft's lines are replaced (ADR 0019),
    and the record of an idempotency key is deleted when its request failed with a fault, or when
    it has expired (ADR 0006). Since then two more of the same kind: a line of a draft credit note
    (ADR 0025) and a role of a user when the user's roles are replaced (ADR 0030). The context refuses
    to delete a row of any entity that is not marked `IHardDeletable`, which only `BillLine`,
    `CreditNoteLine`, `UserRole` and `IdempotencyKey` are; a test lists them, and the database grants
    DELETE on their four tables alone (ADR 0034).

11. **Append-only audit** (BR-701 – BR-703). Every state change to a financial or security-relevant
    entity writes an audit record (who, when in UTC, tenant, entity, action, before/after JSON,
    correlation id) in the same transaction. Audit and ledger tables are insert-only for the
    application login: no UPDATE, no DELETE (a `DENY` of the role `billingrecon_application` since
    Prompt 5.4, ADR 0034).
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
  and validated with `ValidateOnStart`. A validation message never repeats a secret value. The one exception is
  `AllowedHostsOptions`, which reads the standard root key `AllowedHosts` (ADR 0035).
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
  it; the line is in the runbook and a test checks it (ADR 0021). The database grants are the one
  migration written wholly by hand, `0020_DatabaseRoles` (ADR 0034): a later table takes the schema's
  `SELECT, INSERT, UPDATE`, and a table of an `IAppendOnly` or `IHardDeletable` entity adds its
  `DENY UPDATE, DELETE` or `GRANT DELETE` line to its own migration; `DatabasePermissionsOnSqlServerTests`
  fails with the line to add.
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
| `docs/api.md` | REST conventions, auth, idempotency, error-code catalogue, endpoints. Written so far: the conventions, the patient and payer endpoints (Prompt 2.2), the service item endpoints (Prompt 2.3), the bill draft endpoints (Prompt 2.4), the audit trail endpoint (Prompt 3.4), posting and approvals (Prompts 3.5 and 3.6), the payment endpoints with allocation (Prompts 4.1 and 4.2), the credit note endpoints (Prompt 4.3), the adjustment endpoints (Prompt 4.4), the reversal endpoints of payments and allocations (Prompt 4.5), the refunds (Prompt 4.6), the users (Prompt 5.1), authentication with the sign-in (Prompt 5.2), the permissions of each role (Prompt 5.3), the tenant settings with the prefix changes (Prompt 5.3b), HTTPS only, hosts, CORS, the headers of an answer and the sign-in limits (Prompt 5.4b), the reports (Prompts 6.1 and 6.2), and the dashboard (Prompt 6.3). Complete since Prompt 6.2: every endpoint, the actions of the audit trail, and the error-code catalogue (section 9), which `ApiDocumentationTests` keeps complete |
| `docs/build-prompts.md` | The build prompts from zero to R1, reconstructed on 2026-11-20: what each finished prompt built, and the wording and order of the prompts still to give. Its phase numbers are the ones this file uses |
| `docs/security.md` | Authentication and the sign-in limits, authorisation, tenancy, maker-checker, transport (TLS, hosts, CORS, headers), secrets, data protection, the database logins, and the threat model of R1 (section 10: every threat done, accepted with its reason, or named with its prompt). Written in full with Prompt 5.4b |
| `docs/testing-strategy.md` | Test pyramid, tooling, database tests, architecture tests, the frontend's tests (section 5a), CI gates |
| `docs/runbook.md` | Written so far: local infrastructure, the "Verify on a Docker machine" checklist, migrations, tests, CI, the container image, idempotency keys, the deferred verification list, sign-in with the signing key (section 9), the database logins with the migrations outside Development (section 10), the proxy in front of the API with its hosts and origins (section 11), and the frontend with its commands, a local sign-in and the OpenAPI snapshot (section 12). Still to come: backups, incident procedures |
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
(Accepted on the owner's decisions of 2026-11-20 for Prompt 3.5; it amends BR-401),
0023 maker-checker: the approval request, where its decisions live, and what a waiting bill answers (Accepted on
the owner's decisions of 2026-11-20 for Prompt 3.6),
0024 allocating a payment: two aggregates through a Billing contract, the order of work, and the one lock
(**Proposed**: written 2026-11-21 from the code of Prompt 4.2; the owner confirms its points, see section 9
item 21),
0025 credit notes: always a second person, the caps of BR-210 and BR-211 measured again under the ledger's lock,
and applying the credit (Accepted on the owner's approval of the Prompt 4.3 plan, 2026-11-21),
0026 adjustments: no exemption from the second person, the cap asked twice, and the bill reached through
`IBillSettlement` (Accepted on the owner's approval of the Prompt 4.4 plan, 2026-11-21; it resolves open question 1
of the domain model and adds a sentence to BR-501),
0027 reversals of allocations and payments, and a decision that comes late (Accepted on the owner's approval of the
Prompt 4.5 plan, 2026-11-21; its point 9 corrects ADR 0023: a decision that finds no pending approval request
answers 409, not 500, and waits for the owner's confirmation),
0028 reversing an application of credit: later applications only, the stored flag of the application made at
posting, and one decision at a time per application (Accepted on the owner's approval of the Prompt 4.5b plan,
2026-11-21; it closes the open question of ADR 0025 point 5),
0029 refunds: one source, the cap and the payer asked twice, and the payment with a posted refund that is never
reversed (Accepted on the owner's approval of the Prompt 4.6 plan, 2026-11-21; it adds a sentence to BR-307),
0030 users and roles, their passwords, and a key for every column that names a user (Accepted on the owner's approval
of the Prompt 5.1 plan, 2026-11-21; it settles ADR 0021 point 1; its point 7, no Idempotency-Key on the user POSTs,
the owner confirmed with the Prompt 5.2 plan),
0031 sign-in and tokens: a JWT the API issues and checks, one answer for every refused sign-in, the sign-in's step
into the user, deny by default and the Development fallback (Accepted on the owner's approval of the Prompt 5.2 plan,
2026-11-21; it refines ADR 0009 and ADR 0015 section 6),
0032 role permissions: one named policy per capability of the matrix on every action, the blank cells, and the
checker's role checked by the decision itself (Accepted on the owner's approval of the Prompt 5.3 plan, 2026-11-21),
0033 tenant settings and prefix changes: the three settings by a TenantAdmin, a prefix change as a maker-checker
document whose approval changes the default and this year's series, a prefix that belongs to one document type for
good (Accepted on the owner's approval of the Prompt 5.3b plan, 2026-11-21; its built note point 5, the first number
of a year taken during an approval, was closed on the owner's decision of 2026-11-23: the approval holds the series,
"Amended 2026-11-23", which also amends ADR 0021 point 3 for the allocator's file),
0034 database roles, grants and the logins of the application: a migration of grants written by hand, schema-wide with
the exceptions of the markers, the migrator's four roles, every database test as the application's login, the role
owned by `dbo`, and `READ_COMMITTED_SNAPSHOT` (Accepted on the owner's approval of the Prompt 5.4 plan, 2026-11-23),
0035 HTTP hardening: TLS at the proxy with forwarded headers from known proxies only, HTTPS only without a redirect and
HSTS, allowed hosts checked by the API, two sign-in limits (per address and per account), CORS from configuration, no
Server header and the headers of an answer, and the accepted risks of the threat model (Accepted on the owner's approval
of the Prompt 5.4b plan, 2026-11-23; it answers ADR 0031 point 12 and amends ADR 0017's smoke run),
0036 Reporting reads every module and writes none: five exceptions to the module rule for its Infrastructure, figures
held against the ledger, the daily collection from the Cash entries of the business date, totals over every page, and
Reporting's own words for the Billing and Payments enums (Accepted on the owner's approval of the Prompt 6.1 plan,
2026-11-23; it amends ADR 0003 and ADR 0018; "Amended 2026-11-23 (Prompt 6.2)": the reconciliation report, gapless
numbering as identity 11, and the reconciliation after every database test, on the owner's approval of the 6.2 plan),
0037 the dashboard: figures from the ledger by business date, no history of drift, and a sixth raw-SQL file
(`LedgerMovementsQuery`), seven panels under `/api/v1/dashboard` for TenantAdmin, Supervisor and Viewer, the payment
register ordered newest first (Accepted on the owner's approval of the Prompt 6.3 proposal, 2026-11-23; it adds US-44 and
BR-902 and amends ADR 0021 and ADR 0036),
0038 the frontend: React, TypeScript 5.9 and Vite; React Router 8, TanStack Query, a client typed from the API's OpenAPI
document kept equal to it by a test; design tokens with no raw colour, WCAG AA in both themes and an inline theme script
pinned by its hash; Recharts for 7.6; the token in `sessionStorage` (Accepted on the owner's approval of the Prompt 7.1
proposal, 2026-11-23; it answers row S5 of the threat model),
0039 screens: lists in the address, `If-Match` from the record's version, a conflict shown and decided by the user, who may
change as a copy of the API's policies held to `Policies.cs`, decimals typed as text and sent as numbers, payers in a
section of their own (Accepted on the owner's approval of the Prompt 7.2 plan, 2026-11-23),
0040 the bill screens: one editor for a draft saving what changed, the key of an operation in memory and reused on a
retry of the same request, live totals by exact arithmetic on BigInt held with the server to one file of cases, names
in the bill list read per patient and payer (Accepted on the owner's approval of the Prompt 7.3 plan, 2026-11-23).
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
Planned: `api` and `frontend` services in Docker Compose (Prompt 8.1).

The frontend, from `frontend/` (Node 22.22 or later; every command and how to sign in locally: `docs/runbook.md`
section 12):

```bash
npm ci                     # once per clone: exactly the locked packages
npm run dev                # http://localhost:5173, /api forwarded to the API at http://localhost:5080
npm test                   # Vitest
npm run lint               # ESLint, warnings are errors
npm run format:check       # the format gate; npm run format writes the fixes
npm run build              # types, then dist/
npm run api:types          # src/api/schema.d.ts from src/api/openapi.json
```

When an endpoint changes, `OpenApiSnapshotTests` fails until the committed document is rewritten
(`BILLINGRECON_WRITE_OPENAPI_SNAPSHOT=1` with that test) and the types are generated again (runbook section 12).

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
   - built 2026-11-23 with Prompt 5.4 (ADR 0034, item 32): the logins of `docs/database.md` section 7 and their
     grants, including `SELECT` on `__EFMigrationsHistory` for the API's login, without which `/health/ready` could
     never answer 200; and the procedure for applying migrations outside Development with the migration login
     (`docs/runbook.md` section 10);
   - built 2026-11-21 with Prompt 5.2 (ADR 0031): `LoginUserLookup` as the one sanctioned use of
     `IgnoreQueryFilters`, in `BannedSymbolsTests`.
6. **Review findings of 2026-10-03 that are still open** (numbering from that review):
   - 1.1 closed 2026-10-17: the module rule and its two exceptions are written in ADR 0003 (amendment) and
     `ModuleDependencyTests`; Audit is generic over `IDomainEvent`, approval handlers live with their documents,
     aggregates take ids rather than each other.
   - 2.3 closed 2026-11-21 by Prompt 5.2: deny by default, a fallback policy that needs a user (ADR 0031);
     2.4 closed 2026-11-23 by Prompt 5.4b: TLS at the proxy with HTTPS only and HSTS, allowed hosts, no server header
     (ADR 0035);
     2.5 package lock files, `nuget.config`, transitive pinning (the CI cache keys on `Directory.Packages.props`
     and the project files instead of lock files): accepted for R1 in the threat model (`docs/security.md` section 10,
     row T9).
   - 3.1, 3.2 and 3.3 closed 2026-10-17 (`ConventionTests`, `ModuleDependencyTests`, `LayerDependencyTests`:
     controllers never use EF Core or Infrastructure, no minimal APIs, every type in a known module, banned
     packages scanned in every project file, the Domain project file declares no reference); 3.6 soft edges in
     the layer tests; 3.7 xunit 2 is deprecated in favour of xunit.v3.
   - 4.3 most `.editorconfig` style rules were not enforced by the build on the .NET 8 SDK (the .NET 10 SDK
     enforced one more on 2026-10-17, explicit accessibility on interface members, which seven interfaces
     gained; re-check the rest); 4.6 the version endpoint always says `1.0.0`; 4.7 `docs/security.md` is no longer a
     stub (Prompts 5.3 to 5.4) and is written in full by Prompt 5.4b (`docs/testing-strategy.md` was written with
     Prompt 1.4, `docs/api.md` with Prompt 2.2).
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
   - **Identity (Phase 5):** done with Prompt 5.3 (item 29): the permission matrix (`docs/requirements.md`
     section 1) is a named policy on every endpoint (ADR 0032); patients and payers are written by TenantAdmin,
     Supervisor and BillingClerk, read by everyone.
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
      documents; changing a prefix is a maker-checker action (3.6, 5.3; done with Prompt 5.3b, item 30); the gapless
      verification query joins the reconciliation report (6.2; done, item 35).
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
    - **Was temporary, removed by Prompt 3.6 (item 19):** the refusal 422 `bill_requires_approval` for a bill at or
      above `Tenant.PostingApprovalThreshold`. Such a bill now waits in `PendingApproval` (ADR 0023).
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
    - **Migrations `0003` to `0007` are not in those commits.** They came, with `0008` to `0015`, as a commit of
      their own on 2026-11-21 ("Migrations 0003 to 0015, scaffolded with EF Core 10"), made on the office machine
      once it had the .NET 10 SDK: B of `docs/runbook.md` section 8 is done. Every "scaffold the migration on the
      personal machine" in items 9 to 24 is done with it. Before that commit `MigrationsTests` and the whole
      database collection failed on a fresh clone, by design.
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
    - **Was temporary (BR-207):** `bill_requires_approval`. Removed by Prompt 3.6 (item 19).
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
      allocations (4.2); the reconciliation endpoint and the gapless check in the report (6.2; done, item 35); role checks
      (Phase 5: bills are posted by TenantAdmin, Supervisor and BillingClerk).
    - Next: Prompt 3.6 (item 19).
19. **Prompt 3.6 (maker-checker approvals, ADR 0023), written 2026-11-20, not built on .NET 10 yet.** The last
    prompt of Phase 3. Verification is deferred with the rest (item 16).
    - Built: `ApprovalRequest` (Common) with its repository, read service and `GET /api/v1/approval-requests`; the
      bill's `PendingApproval` status; `POST /bills/{id}/post` answers 202 and opens a request at or above the
      threshold; `POST /bills/{id}/approve` (which posts) and `POST /bills/{id}/reject` (which returns the bill to
      draft); the `ApprovalRequests` table and the index of waiting bills; and their tests.
    - Decided by the owner with the plan: submit through `post` with 202; approve and reject on the bill; 409
      `bill_pending_approval` and 403 `approval_checker_is_maker`; the posting checks again at approval;
      **the role of the checker is left to Prompt 5.3**.
    - **Differs from the approved plan in one point:** `reject` carries `[Idempotent]` too. The plan said only
      `approve` would; the convention test asks every POST of a financial controller for a key, and it was not
      weakened (ADR 0023, point 8).
    - Decided while building, recorded in ADR 0023: a scope without a user can neither submit nor decide
      (403 `approval_needs_user`); the events of the request are named `ApprovalRequestOpened`, `...Approved` and
      `...Rejected`, not as `docs/domain-model.md` first had them; `CK_ApprovalRequests_RejectionHasComment`.
    - Removed, as items 15 and 18 said: `bill_requires_approval`, its check and its tests. **Two existing tests
      changed their expectation for it** (`PostBillHandlerTests` and `BillPostingApiTests`: the bill at the
      threshold waits instead of being refused).
    - Changed in existing tests: `TenantQueryFilterTests`, `BillsSchemaTests`, `BillsOnSqlServerTests`,
      `MigrationsTests` and `MigrationsOnSqlServerTests` know the new table, status, index and migration;
      `TestDoubles.WithId` gives an in-memory aggregate the id the persistence layer would.
    - **A third configuration for `Bills`:** `BillApprovalConfiguration` holds the index of waiting bills so that it
      lands in `0010_Approvals`. Merge it into `BillConfiguration` with `BillPostingConfiguration` once the
      migrations are in the repository.
    - Pre-checked in the .NET 9 copy: build, format gate, Domain 481 and Application 252 tests green,
      Architecture 108 green and Integration 910 green on LocalDB 2019, with `0003` to `0010` scaffolded there
      separately by EF Core 9. Failing there, as before and by design: the same eleven tests. The approval and
      posting tests (36) ran eight times in a row without a failure.
    - **To do on the personal machine:** `docs/runbook.md` section 8, which now ends with `0010_Approvals`.
    - Not proven anywhere yet: two aggregates in one commit on EF Core 10; the races between two checkers on SQL
      Server 2022; the 202 answer and the optional request body on ASP.NET Core 10.
    - Follow-ups for later slices: the checker holds Supervisor or TenantAdmin (5.3); every maker-checker document
      of Phase 4 follows the same shape; pending approvals in the reports (6.1); the checker's inbox screen (7.5).
    - Next: Phase 4, Prompt 4.1 (record a payment). `phase-2-billing` now holds Phase 2 and all of Phase 3; the
      owner decides when it is merged.
20. **Prompt 4.1 (record a payment), written 2026-11-20, not built on .NET 10 yet.** The first prompt of Phase 4,
    on `phase-2-billing` like the rest. Verification is deferred with the rest (item 16). No new ADR: the decisions
    are in the "Amended 2026-11-20 (Prompt 4.1)" note of ADR 0005, and the order of the steps is that of ADR 0022.
    - Built: the `Payment` aggregate; `POST /api/v1/payments` (records and posts, 201, `[Idempotent]`),
      `GET /api/v1/payments/{id}` and `GET /api/v1/payments`; the `Payments` table; the posting template
      Dr Cash / Cr UnappliedCash(payment); three identities of the reconciliation (`PaymentUnapplied`,
      `PaymentWithoutPosting`, `PostingWithoutPayment`); and their tests. The Payments README placeholders are gone.
    - Decided by the owner with the plan: **`IReceivablesLedger.PostAsync` answers the id of the posting**, and the
      payment stores it (`PostingId`, NOT NULL, foreign key to `LedgerPostings`); the table holds **only what
      recording needs** (the columns and states of a reversal arrive with Prompt 4.5, which also widens
      `CK_Payments_Status`); an amount is at most **9,999,999,999.99**.
    - **No new module-dependency exception.** Payments uses two Application contracts, as the module rule allows:
      `IActivePayers` (new, in Payers: the payer exists and is active, BR-309) and `IReceivablesLedger`. To keep
      the Payments domain from referring to the Billing domain, **`BusinessDateWindow` moved from
      `Domain/Billing` to `Domain/Common`** (BR-205 is one rule for every dated document); nothing else about it
      changed.
    - Decided while building: an unknown payer answers 404 `payer_not_found` and an inactive one 422
      `payer_inactive`, as for a bill; a date that BR-205 refuses answers 422 `payment_date_in_future` or
      `payment_date_too_old`; a payment without a date is dated today in India; the audit action is `Recorded`;
      the list is ordered by payment date, newest first, then by number. Who may record a payment is a role
      question and is left to Phase 5 with the others.
    - Lock order of a recording (the order of ADR 0022): every check first with nothing locked; then the number
      series; then the `UPDLOCK` read of the payer's row of `PayerBalances` inside the ledger; then the save. The
      payment row is an insert and locks nothing anyone waits for.
    - Changed in existing code and tests, none weakened: the callers and test doubles of `PostAsync` return the
      new type; `IdempotencyConventionTests` lists `PaymentsController` as financial; `TenantQueryFilterTests`,
      `TypedIdForeignKeyTests` (two new keys), `MigrationsTests` and `MigrationsOnSqlServerTests` know the new table
      and migration. **`ReceivablesOnSqlServerTests` now leaves the identity `PostingWithoutPayment` out of its
      drift helper**: those tests write payment postings through the port alone, without payment rows, which the
      reconciliation now reports, correctly. Every other identity is asserted there as before, and
      `PaymentsApiTests` asserts the unfiltered reconciliation, this identity included.
    - Pre-checked in the .NET 9 copy: strict build, format gate, Domain 501 and Application 274 tests green,
      Architecture 108 green and Integration 928 green on LocalDB 2019, with `0003` to `0011` scaffolded there
      separately by EF Core 9 (`0011_Payments` held the `Payments` table alone, with its three foreign keys and
      four indexes). Failing there, as before and by design: the same eleven tests. The payment, receivables and
      migration tests (72) ran three times in a row without a failure.
    - **To do on the personal machine:** `docs/runbook.md` section 8, which now ends with `0011_Payments`.
    - Not proven anywhere yet: twenty payments recorded at once for one payer, and a recording held open while its
      key is sent again, on SQL Server 2022; the failure injection at each step there; the 201 answer of an
      idempotent POST with its Location on ASP.NET Core 10.
    - Follow-ups for later slices: allocation and `PaymentAllocations` (4.2); reversal, its columns and states
      (4.5); refunds (4.6); the role that may record (5.3); the daily collection report over
      `IX_Payments_TenantId_PaymentDate` (6.1).
    - Next: Prompt 4.2 (allocate a payment to bills), which is item 21.
21. **Prompt 4.2 (allocate a payment to bills, ADR 0024), code and tests written 2026-11-20, pre-checked and
    documented 2026-11-21, not built on .NET 10 yet.** On `phase-2-billing` like the rest. Verification is deferred
    with the rest (item 16). The code and tests are in commit `fbfebbd` ("20 nov 2026"), made before the pre-check
    and the documents; the documents and ADR 0024 are the commit after it.
    - Built: `PaymentAllocation`, a child of `Payment`; `POST /api/v1/payments/{id}/allocations` (`[Idempotent]`,
      `If-Match` with the version of the payment, 200 with the payment and its allocations) and
      `GET /api/v1/payments/{id}/allocations`; the `PaymentAllocations` table; `Bill.ApplyCredit`, the one change a
      posted bill accepts (BR-208: `PartiallyPaid`, `Settled`); the posting template
      Dr UnappliedCash(payment) / Cr Receivable(bill), one posting per allocation; four identities of the
      reconciliation (`PaymentAllocations`, `AllocationWithoutPosting`, `PostingWithoutAllocation`,
      `AllocationPayerMismatch`); and their tests.
    - **No new module-dependency exception.** Payments reaches a bill only through `IBillSettlement`, a public
      Application contract of Billing; the bill joins the unit of work of the request and is saved by the handler's
      commit under its own row version.
    - Order and lock (ADR 0024): shape; the payment and the version the client read; the sum against the unapplied
      amount; each bill in the order of the bill ids; then the ledger, whose first posting takes the `UPDLOCK` on the
      payer's row of `PayerBalances`, the one lock the use case waits for; then the save. No number is taken.
    - **Changed in the ledger:** the locked statement that moves `PayerBalances` now answers when a balance would go
      below zero and changes nothing; `PostAsync` returns `ledger_balance_negative` instead of the CHECK constraint
      failing the batch (BR-405). The allocation handler answers that as 409 `concurrency_conflict`, like a stale
      row version: both mean that a bill read by the request was changed by one that committed first.
    - New error codes: 422 `allocation_exceeds_unapplied`, `allocation_exceeds_balance_due`,
      `allocation_payer_mismatch`, `bill_not_posted`. One request names at most 50 bills, each once. A payment of a
      payer who was deactivated since can still be allocated.
    - **Migrations renumbered:** the table is `0012_PaymentAllocations`, kept out of `0011_Payments` by
      `PaymentConfiguration` (which takes the allocations out of the model) and `PaymentWithAllocationsConfiguration`
      (which puts them back; moved out of the project while `0011` is scaffolded). Credit notes, adjustments,
      refunds and users are now `0013` to `0016` (`docs/build-prompts.md`, `docs/database.md` section 8).
    - Changed in existing tests, none weakened: `PaymentsApiTests` expects a lowered `UnappliedAmount` to be reported
      by two identities instead of one (the ledger, and now the amount less the allocations);
      `RecordPaymentTests` names the overload of the repository's `Add`; `TenantQueryFilterTests`,
      `TypedIdForeignKeyTests` (three new keys), `ErrorCodesTests`, `MigrationsTests` and
      `MigrationsOnSqlServerTests` know the new entity, table and migration.
    - Pre-checked in the .NET 9 copy on 2026-11-21: strict build, format gate, Domain 518 and Application 298 tests
      green, Architecture 108 green and Integration 944 green on LocalDB 2019, with `0003` to `0012` scaffolded there
      separately by EF Core 9 (`0011` held `Payments` alone, `0012` held `PaymentAllocations` alone). Failing there,
      as before and by design: the same eleven tests. The allocation, payment, receivables, bill-posting and
      migration tests (108) ran three times in a row without a failure.
    - **To do on the personal machine:** `docs/runbook.md` section 8, which now ends with
      `0012_PaymentAllocations`.
    - **For the owner:** confirm the points of ADR 0024 (it is Proposed: it was written a day after the code, from
      the code), then flip its Status.
    - Not proven anywhere yet: two cashiers on the last rupee and ten payments allocated at once across overlapping
      bills, on SQL Server 2022; the refusal of the ledger under its lock there; the split of `0011` and `0012` on
      EF Core 10.
    - Follow-ups for later slices: credit notes and adjustments lower a bill through the same `IBillSettlement`
      (4.3, 4.4); reversal of an allocation, its columns and states (4.5); the role that may allocate (5.3).
    - Next: Prompt 4.3 (credit notes), which is item 22.
22. **Prompt 4.3 (credit notes, ADR 0025), written 2026-11-21, not built on .NET 10 yet.** On `phase-2-billing`
    like the rest. Verification is deferred with the rest (item 16). Built in the session that finished 4.2, at the
    owner's word ("continue with 4.3"), after 4.2 was committed and with the plan approved first.
    - Built: the `CreditNote` aggregate with `CreditNoteLine` and `CreditNoteAllocation` (Billing); ten endpoints
      under `/api/v1/credit-notes` (create, get, list, `PATCH` header, `PUT` lines, `void`, `post`, `approve`,
      `reject`, `applications`); the three tables; the posting templates Dr Revenue / Cr CreditAvailable(credit
      note) and Dr CreditAvailable(credit note) / Cr Receivable(bill); eight identities of the reconciliation; and
      their tests.
    - Decided by the owner with the plan (ADR 0025): the caps of BR-210 and BR-211 are checked before any lock
      (422) and **again after the approval's first ledger posting has locked the payer's balances** (409
      `concurrency_conflict`), with no new column on `Bills`; a credit note of zero is refused at `post`; the payer
      need not be active; no reversal columns on `CreditNoteAllocations`; a line may name a bill line and takes its
      defaults from it; applying credit to another bill needs no second person.
    - The maker's `post` always answers 202 (every credit note needs a second person, BR-212); the checker's
      `approve` is the posting, in the six steps of ADR 0022, and applies `min(Total, Bill.BalanceDue)` to the
      credit note's own bill; the rest stays in `AvailableAmount`.
    - **No new module-dependency exception.** Everything is in Billing; the foreign keys to payers, patients and
      ledger postings come from the typed-id convention (ADR 0021); the ledger is reached through
      `IReceivablesLedger`.
    - **Decided while building, to be seen by the owner:** the JSON of a bill line gained `id`, which a credit note
      line names as `billLineId` (the plan said "references a bill line" and not how); a credit note line has no
      service item of its own (`docs/database.md` first had it "as BillLines"); the validators of credit notes use
      the limits of bill lines.
    - **Two existing tests changed their expectation**, both because a line of a draft credit note is deletable
      like a line of a draft bill: `HardDeleteGuardTests` (the deletable entities are now a bill line, a credit
      note line and an idempotency key; the test is renamed) and `RelationalConventionTests` (two foreign keys with
      `ClientCascade` instead of one). Neither checks less than before.
    - Changed in other existing tests, none weakened: `IdempotencyConventionTests` lists `CreditNotesController`
      as financial; `TenantQueryFilterTests`, `TypedIdForeignKeyTests` (eight new keys), `MigrationsTests` and
      `MigrationsOnSqlServerTests` know the new entities, tables and migration.
    - Pre-checked in a .NET 9 copy made from scratch: strict build, format gate, Domain 540 and Application 306
      tests green, Architecture 108 green and Integration 958 green on LocalDB 2019, with `0003` to `0013`
      scaffolded there separately by EF Core 9 (`0013` held the three tables alone). Failing there, as before and
      by design: the same eleven tests. The credit note, allocation, approval, ledger and migration tests (73) ran
      eight times in a row without a failure. With the second reading of the caps switched off in the copy, the
      test of two credit notes approved at once failed six times out of six.
    - **To do on the personal machine:** `docs/runbook.md` section 8, which now ends with `0013_CreditNotes`.
    - **Open, for the owner:** no remaining prompt reverses an application of credit; Prompt 4.5 names allocations
      of payments and payments only. Add it to 4.5, or leave it out of R1 (ADR 0025, point 5).
    - Not proven anywhere yet: three aggregates in one commit and the split query on EF Core 10; the race of two
      approvals on SQL Server 2022.
    - Follow-ups for later slices: adjustments lower a bill the same way (4.4); refunds from a credit note's
      available amount (4.6); the roles that may draft, approve and apply (5.3); available credit by payer (6.1).
    - Next: Prompt 4.4 (adjustments), which is item 23.
23. **Prompt 4.4 (adjustments, ADR 0026), written 2026-11-21, and the first prompt built and tested on .NET 10.**
    On `phase-2-billing` like the rest. The plan was shown first and approved "as recommended".
    - Built: the `Adjustment` aggregate (Adjustments module, the first code in its four folders; their README
      placeholders are gone); eight endpoints under `/api/v1/adjustments` (create, get, list, `PUT`, `void`, `post`,
      `approve`, `reject`); the `Adjustments` table; the posting template Dr Adjustments / Cr Receivable(bill);
      three identities of the reconciliation; and their tests.
    - Decided by the owner with the plan (ADR 0026): **no exemption from maker-checker for a small rounding**
      (open question 1 of `docs/domain-model.md`, now resolved); the cap against the balance due is asked when the
      maker submits and again at approval; the adjustment date is not before the bill date (**BR-501 gained that
      sentence**); the payer need not be active; a posted adjustment is not reversed.
    - **No new module-dependency exception, and one listed exception left unused.** Adjustments reaches the bill
      through `IBillSettlement`, which gained `FindPostedAsync` (the payer, the date, the number and the balance of
      a posted bill, with no Billing type in its answer). The exception `Application.Adjustments` to
      `Domain.Billing` in `ModuleDependencyTests` and ADR 0003 is not used; whether to take it out of the list is
      the owner's call. **Differs from the plan in one point:** the plan said Billing's read service; its DTO
      carries a Billing enum, which Adjustments may not name.
    - Changed in existing tests, none weakened: `IdempotencyConventionTests` lists `AdjustmentsController`;
      `TenantQueryFilterTests`, `TypedIdForeignKeyTests` (three new keys), `MigrationsTests` and
      `MigrationsOnSqlServerTests` know the new entity, table and migration; `BillSettlementTests` gained two tests.
    - **Verified on .NET 10 (SDK 10.0.404), in a throwaway copy without any version substitution:** the
      repository's `dotnet-ef` 10.0.12 scaffolded `0003` to `0014` one by one, each holding exactly its own tables,
      which proves on EF Core 10 the device of ADR 0022 point 7 and ADR 0024 point 7; strict build and format gate
      clean; Domain 560, Application 313, Architecture 113, Integration 977, **all green**. The database tests ran
      against LocalDB 2019 through a patched fixture, in the copy only.
    - **An intermittent failure in an older test, not explained yet.** In six runs of the concurrency classes
      (221 tests) `BillApprovalApiTests.BR804_two_checkers_at_once_give_one_decision_and_one_conflict_whether_both_approve_or_one_rejects`
      (Prompt 3.6) failed once and then passed 45 times in a row; its message was not captured. Reading the test:
      when the rejection wins and the approval reads the bill after that commit, the bill is a draft and the right
      answer is 422 `bill_not_pending_approval`, but the test accepts only 409. That would be an assumption of the
      test about timing, not a defect of the code; it is a guess until a failure is caught. Not changed: it is a
      test of another prompt. **That guess was wrong, and the failure is explained and fixed with Prompt 4.5
      (item 24, ADR 0027 point 9):** the losing request answered 500, a defect of the code.
    - **Still to do:** the run on SQL Server 2022 in Docker. (The migrations `0003` to `0014` were committed on
      their own on 2026-11-21, with `0015`.)
    - Follow-ups for later slices: the roles that may draft and approve (5.3); adjustments by kind in the reports
      (6.1).
    - Next: Prompt 4.5 (reversals), which is item 24.
24. **Prompt 4.5 (reversals, ADR 0027), written 2026-11-21, built and tested on .NET 10.** On `phase-2-billing`
    like the rest. The plan was shown first and approved "as recommended".
    - Built: the reversal of an allocation (US-32, BR-305) and of a payment (US-33, BR-306) as maker-checker
      actions on the `Payment` aggregate; six endpoints in `PaymentReversalsController` (`reverse`,
      `approve-reversal`, `reject-reversal`, on a payment and on one of its allocations); `Bill.ReverseCredit` and
      `IBillSettlement.RestoreAsync`; the mirror posting templates; the reversal columns of `Payments` and
      `PaymentAllocations`; three identities of the reconciliation; and their tests.
    - Decided by the owner with the plan (ADR 0027): reversing an application of credit is its own prompt,
      **4.5b**, added to `docs/build-prompts.md`; the BR-306 check "refused when a refund exists" is built with
      refunds (4.6); a payment and one of its allocations never wait for a reversal at the same time; a reversal
      takes no document number; the maker's reason is kept by the approval request and copied to the row at
      approval.
    - **A defect found and fixed, in code of earlier prompts as well (ADR 0027, point 9).** Every approve and reject
      use case read its document, then looked for the pending approval request, and threw when there was none.
      Another decision committed between the two reads makes exactly that happen, and the client got **500**. All
      ten handlers (bills since 3.6, credit notes, adjustments, and the four new ones) now answer **409
      `concurrency_conflict`** through `PendingApprovals.FindPendingAsync`. Nothing was ever written wrongly: only
      the answer was wrong. The new test `BillApprovalApiTests.BR804_a_decision_that_read_the_bill_before_another_decision_committed...`
      forces the order; it answered 500 before the change. This was the intermittent failure of item 23.
    - **Changed expectations in existing tests**, each called out: `BillApprovalHandlerTests` expected an
      exception for a waiting bill without a request and now expects the conflict (renamed); `MigrationsTests`
      expects the status constraints of `Payments` and `PaymentAllocations` to list the three states from `0011`
      and `0012` on, because they are generated from the enums.
    - Changed in other existing tests, none weakened: `IdempotencyConventionTests` lists
      `PaymentReversalsController`; `TypedIdForeignKeyTests` (two new keys), `MigrationsTests` and
      `MigrationsOnSqlServerTests` know the columns and the migration; `BillSettlementTests` gained tests.
    - **Migrations renumbered:** the columns are `0015_PaymentReversals`, kept out of `0011` and `0012` by a third
      configuration class, `PaymentWithReversalsConfiguration`. Refunds and users are now `0016` and `0017`, one
      more each if 4.5b adds a migration first.
    - **Verified on .NET 10 (SDK 10.0.404), in a throwaway copy made from scratch:** `0003` to `0015` scaffolded one
      by one with EF Core 10.0.12 (`0015`: eight columns, two CHECK constraints, two foreign keys, no table); strict
      build and format gate clean; Domain 572, Application 322, Architecture 113, Integration 989, all green on
      LocalDB 2019; the concurrency classes (231 tests) twelve times in a row without a failure.
    - **Still to do:** SQL Server 2022 in Docker; the refund check of BR-306 with 4.6. (The migrations are in the
      repository since 2026-11-21.)
    - **For the owner:** confirm point 9 of ADR 0027, which reverses a decision of Prompt 3.6 (a missing request
      was a fault there).
    - Next: Prompt 4.5b (reverse an application of credit), which is item 25, then 4.6 (refunds).
25. **Prompt 4.5b (reverse an application of credit, ADR 0028), written 2026-11-21, built and tested on .NET 10.**
    On `phase-2-billing` like the rest. The plan was shown first and approved "as recommended".
    - First, on its own: the migrations `0003` to `0015`, scaffolded earlier that day in a copy, committed as
      `740fb31`, so that this prompt's migration is added the ordinary way.
    - Built: the reversal of a later application of a credit note's credit as a maker-checker action on the
      `CreditNote` aggregate; three endpoints in `CreditApplicationReversalsController` (`reverse`,
      `approve-reversal`, `reject-reversal` on `/credit-notes/{id}/applications/{applicationId}`); the approval
      gives the bill its amount back through `IBillSettlement.RestoreAsync` and enters Dr Receivable(bill) /
      Cr CreditAvailable(credit note); `PostingTemplate.CreditApplicationReversed`; one new identity of the
      reconciliation (`ReversedApplicationNoPosting`) and the extension of two others; migration
      **`0016_CreditApplicationReversals`**; and their tests (Domain 7, Application 8, Integration 12 new).
    - Decided by the owner with the plan (ADR 0028): only a later application can be reversed, the one made at
      posting answers 422 `credit_application_made_at_posting`; the row says which one it is (`AppliedAtPosting`,
      kept `Active` by a CHECK constraint); one decision at a time for each application (409), and the credit note
      can still apply its credit meanwhile; no document number, and the maker's reason is kept by the approval
      request and copied to the row at approval.
    - **Changed in existing code and tests, none weakened:** `ApproveCreditNoteHandler` calls the new
      `ApplyToOwnBillAtPosting`; the JSON of an application gained `appliedAtPosting`, `reversedAtUtc` and
      `reversalReason`; `IdempotencyConventionTests` lists the new controller; `TypedIdForeignKeyTests` (one new key),
      `MigrationsTests` (two new tests) and `MigrationsOnSqlServerTests` (16 migrations) know the migration.
    - **Accepted without a data step (ADR 0028, point 9):** `AppliedAtPosting` is added with the default 0, so an
      application made at posting before `0016` would count as a later one. No database outside the tests holds a
      posted credit note yet. If one ever does before `0016` runs, a hand-written data step is owed, and that needs
      the owner's approval.
    - **Migrations renumbered:** refunds and users are now `0017_Refunds` and `0018_Users`.
    - **Verified on .NET 10 (SDK 10.0.404):** in the repository, the strict build and
      `dotnet format --verify-no-changes` are clean and Domain 579, Application 330 and Architecture 113 tests pass;
      in a throwaway copy whose database fixture points at LocalDB 2019, the integration project passes in full
      (1001 tests), and the 18 `BR804` concurrency tests ran five more times without a failure. `0016` was scaffolded
      with the repository's `dotnet-ef` in such a copy and copied back, because `.config/dotnet-tools.json` in the
      repository carries the "came from another computer" mark and `dotnet ef` refuses to run there; the owner can
      clear it (file Properties, Unblock).
    - **Still to do:** SQL Server 2022 in Docker.
    - Next: Prompt 4.6 (refunds), which is item 26.
26. **Prompt 4.6 (refunds, ADR 0029), written 2026-11-21, built and tested on .NET 10.** On `phase-2-billing` like
    the rest. The plan was shown first and approved "as recommended".
    - Built: the `Refund` aggregate (Payments) from one source, a payment's unapplied cash or a posted credit note's
      available credit, as a maker-checker document numbered `REF/...` at the approval; eight endpoints in
      `RefundsController` (`POST`, `GET`, list, `PUT`, `void`, `post`, `approve`, `reject`); `Payment.PayRefund`;
      `CreditNote.PayRefund`, reached through the new Billing contract `ICreditNoteSettlement` (no new module
      exception); the posting Dr UnappliedCash(payment) or CreditAvailable(credit note) / Cr Cash; three identities
      of the reconciliation and refunds in two others; migration **`0017_Refunds`**; and their tests (Domain 17,
      Application 11, Integration 16 new), with the end-to-end scenario test of the prompt
      (`EndToEndScenarioTests`: bill, part payment, credit note, adjustment, allocation reversal, refund; zero drift
      after every step and every series gapless at the end).
    - Decided by the owner with the plan (ADR 0029): BR-306 is asked when a payment's reversal is requested, through
      the refund repository (422 `payment_refunded`), with no cached refunded amount on `Payments`; the cap and the
      source's state at the post and at the approval, the payer's activity at the draft and at the approval; the
      refund date not before the source's date (**BR-307 gains that sentence**); a payment that waits for its reversal
      or was reversed refunds nothing (409), a waiting refund does not stop a reversal request; the payment and the
      credit note record `Refunded`. The refund's own trail says `Updated` for a corrected draft, as an adjustment's
      does, where the plan said `Replaced`.
    - **Changed expectations in existing tests, each called out, none weakened:** two `MigrationsTests` checks that
      "nothing after `0014` / `0016` creates a table" now look at their own migration only; `ReceivablesOnSqlServerTests`
      leaves out `PostingWithoutRefund`, as it has left out `PostingWithoutPayment` since 4.1, because it writes
      postings through the port without their documents. Lists updated: `MigrationsOnSqlServerTests` (17),
      `TenantQueryFilterTests`, `TypedIdForeignKeyTests` (four keys), `IdempotencyConventionTests`.
    - **Migrations renumbered:** users are now `0018_Users`.
    - **Verified on .NET 10 (SDK 10.0.404):** in the repository, the strict build and
      `dotnet format --verify-no-changes` are clean and Domain 596, Application 341 and Architecture 113 tests pass;
      in a throwaway copy on LocalDB 2019 the integration project passes in full (1017 tests), and the 20 `BR804`
      concurrency tests ran five more times without a failure. `0017` was scaffolded in such a copy (the "came from
      another computer" mark still blocks `dotnet ef` in the repository) and copied back; no pending model change.
    - **Still to do:** SQL Server 2022 in Docker. The merge of the branch to `main` is the owner's step.
    - Next: Prompt 5.1 (users and roles), the first of Phase 5, which is item 27.
27. **Prompt 5.1 (users and roles, ADR 0030), written 2026-11-21, built and tested on .NET 10.** On `phase-2-billing`
    like the rest. The plan was shown first and approved "as recommended".
    - Built: the `User` aggregate (Identity) with `UserRole` children and the global `Roles` table; PBKDF2 hashing
      behind `IPasswordHasher` (`Pbkdf2PasswordHasher`); eight endpoints in `UsersController` (`POST`, `GET`, list,
      `PATCH`, `PUT .../roles`, `disable`, `enable`, `password`); the development user, seeded with the development
      tenant; the audit snapshot leaves out a column annotated `BillingRecon:NotAudited` (the password hash); migration
      **`0018_Users`**; and their tests (Domain 8, Application 9, Integration 22 new).
    - Decided by the owner with the plan (ADR 0030): every column that names a user is a foreign key to `Users` (39 in
      all, 34 of them added to earlier tables); the development database is dropped before `0018` (runbook section 1;
      no data step); PBKDF2-HMAC-SHA-256 with 600,000 iterations, passwords of 12 to 128 characters, an admin reset
      with no forced change; at least one role, the last active TenantAdmin protected, no self-disable; no role check
      before 5.2 and 5.3; the development user has a password nothing matches.
    - **Confirmed by the owner with the Prompt 5.2 plan (ADR 0030 point 7):** the user POSTs take no Idempotency-Key.
      Users are reference data like patients and payers, and the idempotency store keeps a SHA-256 fingerprint of the
      request body, which for a creation or a reset holds the password: a fast hash of the password must not be stored.
    - **Test support changed:** every database test identity is now a real user. `NewApiTenantAsync`,
      `AddApiTenantRowsAsync`, `AddProbeTenantRowsAsync` and the new `DatabaseApiFactory.CreateClientFor` write the
      identity's user row (`TestUsers`); Respawn leaves `Roles` alone. Two tests add the row of a second user they act as.
    - **Changed expectations in existing tests, each called out, none weakened:** `TypedIdForeignKeyTests` expected no
      key on the user columns ("until Prompt 5.1") and now expects the key to `Users`; the lists of keys of seven
      SQL Server tests, the lists of `TenantQueryFilterTests`, `HardDeleteGuardTests`, `RelationalConventionTests` and
      `IdempotencyConventionTests`, and `MigrationsOnSqlServerTests` (18) name the new tables, keys and controller.
    - **Verified on .NET 10 (SDK 10.0.404):** in the repository, the strict build and
      `dotnet format --verify-no-changes` are clean, and Domain 604, Application 350, Architecture 113 and the 595
      integration tests without a database pass; in a throwaway copy on LocalDB 2019 the integration project passes in
      full (1039 tests), and `UsersApiTests` with the 20 `BR804` tests ran three more times without a failure. `0018`
      was scaffolded in such a copy and copied back; no pending model change.
    - **Still to do:** SQL Server 2022 in Docker; drop any development database with data before `0018`.
    - Next: Prompt 5.2 (sign-in and JWT).
28. **Prompt 5.2 (sign-in and JWT, ADR 0031), written 2026-11-21, built and tested on .NET 10.** On `phase-2-billing`
    like the rest. ADR 0031 was written first; the plan was shown and approved "as recommended". No migration.
    - Built: `POST /api/v1/auth/login` (`AuthController`, `SignInHandler`) answering a 60-minute HS256 JWT with `sub`,
      `tenant_id` and `role`; `JwtTokenIssuer` behind `ITokenIssuer`; the bearer scheme with the lifetime checked by
      the application's clock (`TokenLifetime`); `RequestPrincipalIdentity` as `ITenantContext` and `ICurrentUser` in
      every environment; `HttpSignInScope` behind `ISignInScope`; `LoginUserLookup`, the one `IgnoreQueryFilters`;
      deny by default with a fallback policy, the anonymous endpoints being the sign-in, the health checks and, in
      Development, OpenAPI and Scalar; the Development scheme for a request without a token; `Jwt:SigningKey`
      validated at startup (a Development key in `appsettings.Development.json`, refused elsewhere); the error kind
      `Unauthorized`; `User.RecordSignIn` with the event `UserSignedIn` (audited as `SignedIn`); a password
      verification without a hash for the refusals that have none. `UnavailableIdentity` is gone.
    - Decided by the owner with the plan (ADR 0031): the JwtBearer package 10.0.12; the key design; one 401
      `invalid_credentials` for every refusal, the reason in the log only; the sign-in recorded and audited as the user
      through `ISignInScope`; a token valid for its 60 minutes after its user is disabled; the Development fallback;
      real tokens in every test; no Idempotency-Key on the sign-in, and ADR 0030 point 7 confirmed.
    - **Found while building (ADR 0031, built note):** the time provider of IdentityModel's validation parameters is
      internal, hence `TokenLifetime`; an anonymous endpoint must authenticate nobody, or a sign-in in Development, or
      one that carries another tenant's token, finds only that tenant (fixed and tested, also in Development on SQL
      Server); the validator measured the code before trimming it, so a 20-character code typed with a space was a 400
      (fixed and tested); the sign-in's records print no password, address or token.
    - **Test support changed:** `TestPrincipalStartupFilter` and `TestRequestIdentity` are gone; `CreateClientFor`
      puts a token minted by the API's issuer on every request (a client without roles is a TenantAdmin, `TestRoles`);
      every test host outside Development has a random key of its own; `ThrowingIdentity` stands where the persistence
      tests used `UnavailableIdentity`. New: `AuthenticationTests` (the token on the wire, and a request without a token
      to every endpoint), `AuthApiTests` (US-02), `TenantIsolationApiTests` (US-04 for the users and every financial
      document), `AuthenticationConventionTests`, the sign-in's unit tests.
    - **Changed expectations in existing tests, each called out, none weakened:** an anonymous request now answers 401
      `unauthorized` where it answered 500 from the throwing test identity (`ClientIdentityTests`) or reached the probe
      (`LoggingTests`, which still finds no tenant or user on its events); the tests that expect 404 for a path that
      does not exist or is not mapped in Production (`ErrorContractTests`, `OpenApiEndpointsTests`, the old health path,
      the unversioned path) send a token, since without one a request gets 401 first; `RequestIdentityTests` now
      expects the long name-identifier claim not to be read; `IdentityRegistrationTests` is rewritten for the new
      identity; `IdempotencyConventionTests` names its second list the non-financial controllers, with the sign-in;
      `BannedSymbolsTests` adds `LoginUserLookup`; the error tests add the kind `Unauthorized`.
    - **Verified on .NET 10 (SDK 10.0.404):** in the repository, the Release build and
      `dotnet format --verify-no-changes` are clean, and Domain 606, Application 368, Architecture 117 and the 629
      integration tests without a database pass; in a throwaway copy on LocalDB 2019 the integration project passes in
      full (1087 tests), and the 20 `BR804` tests with the sign-in and isolation tests ran three more times without a
      failure.
    - **Still to do:** SQL Server 2022 in Docker; the container's smoke test in CI with its throwaway key (the workflow
      changed, it has not run); the user commands and requests of 5.1 that hold a password print it in their generated
      `ToString` (nothing logs them; a small follow-up). The rate limit of sign-ins (ADR 0031 point 12) is done with
      Prompt 5.4b (item 33).
    - Next: Prompt 5.3 (role permissions and tenant settings).
29. **Prompt 5.3, first half: role permissions (ADR 0032), written 2026-11-21, built and tested on .NET 10.** On
    `phase-2-billing` like the rest. The plan was approved "as recommended", which split the prompt: this is 5.3, the
    tenant settings are 5.3b (its decisions are recorded in `docs/build-prompts.md` already). No migration.
    - Built: `Policies`, sixteen named policies, one per capability of the matrix, and `AddPermissions` with the
      fallback policy; one `[Authorize(Policy = ...)]` on each of the 78 actions but the sign-in; `ICurrentUser.Roles`,
      read from the token; `ApprovalRequest.Approve` and `Reject` take the checker's roles and refuse one without
      Supervisor or TenantAdmin (403 `approval_checker_role`), passed by all fourteen decisions; the audit trail of a
      user for a TenantAdmin only; `docs/security.md` written.
    - Decided by the owner with the plan (ADR 0032): a blank cell is no access, except that a Cashier reads credit
      notes; applying credit is the credit notes' row; the inbox is the checkers'; every role reads the audit trail but
      a user's; the checker's role is checked by the policy and again by the decision.
    - **Tests:** `PermissionMatrixApiTests` walks every endpoint with each of the five roles against its own copy of the
      matrix and checks that it names every endpoint; `PermissionConventionTests` (one policy per action, no policy on a
      controller, no unused policy, the checker's roles in every decision); `AuthorizationTests` without a database; the
      approval request's and two handlers' tests of the checker's role.
    - **Changed expectations in existing tests, each called out, none weakened:** clients that set up and act name both
      roles (the cashiers of the payment, allocation, reversal and refund tests also hold BillingClerk; the clerk of the
      refund and end-to-end tests also holds Cashier); Viewer clients that registered what they read are BillingClerks;
      the five maker-cannot-decide tests act as the maker holding Supervisor and also assert the role's 403; the
      last-administrator test acts with a token from before the demotion; the identity probe reports
      `ICurrentUser.Roles`; the domain and application tests of decisions pass a supervisor's roles.
    - **Verified on .NET 10 (SDK 10.0.404):** in the repository, the Release build and
      `dotnet format --verify-no-changes` are clean, and Domain 612, Application 373, Architecture 121 and the 643
      integration tests without a database pass; in a throwaway copy on LocalDB 2019 the integration project passes in
      full (1104 tests), and the 20 `BR804` tests with the matrix and isolation tests ran three more times without a
      failure.
    - **Still to do:** SQL Server 2022 in Docker; Prompt 5.3b (done: item 30).
    - Next: Prompt 5.3b (tenant settings and prefix changes).
30. **Prompt 5.3b, second half: tenant settings and prefix changes (ADR 0033), written 2026-11-21, built and tested on
    .NET 10.** On `phase-2-billing`. The plan was approved "as recommended". Migration `0019_PrefixChanges`, scaffolded
    in the throwaway copy with the repository's `dotnet-ef` and copied back.
    - Built: `GET` and `PATCH /api/v1/settings` (TenantAdmin, If-Match; the threshold, the back-date window, the
      default due days; a threshold left out is none) with `Tenant.ChangeSettings`; the `PrefixChange` document
      (`POST /api/v1/settings/prefix-changes` by a TenantAdmin, `GET` one and the list, `approve` and `reject` by a
      Supervisor or TenantAdmin who is not the maker), whose approval sets the tenant's default and the prefix of the
      type's series of the current fiscal year (`NumberSeries.ChangePrefix`, through the model and its row version);
      `IPrefixUsage`, which keeps a prefix to one document type for good; the approval request's document type renamed
      `NumberSeries` → `PrefixChange`; the trails of the tenant, its prefix changes and its number series for a
      TenantAdmin only; two checks on `Tenants` (`CK_Tenants_PrefixesDistinct`, `CK_Tenants_PostingApprovalThreshold`).
    - Decided by the owner with the plan (ADR 0033 points 3, 5, 6, 9): the checkers read the changes; the document
      type is renamed; a prefix belongs to one type for good; the three trails are the TenantAdmin's.
    - **Decided in the build, not in the plan (ADR 0033 built note):** the prefix-change posts take no
      Idempotency-Key (the controller is declared non-financial in `IdempotencyConventionTests`; the filtered unique
      index and If-Match refuse repeats); the request answers 201; the change's own methods are `RecordApproval` and
      `RecordRejection`, so the convention test that counts the decisions of approval requests (now sixteen) sees
      each once.
    - **Found, closed 2026-11-23 by item 31 (ADR 0033 built note point 5):** the approval's 409 covers a series that exists. When
      the first number of a type in a fiscal year is taken in the milliseconds between the approval's read of the
      series and its commit, the allocator creates the series with the old prefix and the approval answers 200; that
      year's series keeps the old prefix, numbers stay unique and gapless. `docs/database.md` section 4.4 finds it;
      one or two further changes repair it. Closing it needs a locked read of the series in the approval, which is
      new raw SQL (the alternative ADR 0033 rejected): not built.
    - **Tests:** Domain (`TenantTests`, `PrefixChangeTests`), Application (`SettingsHandlerTests`, 19), Database
      (`SettingsApiTests`: US-03 end to end, the maker-checker path that numbers the next bill with the new prefix,
      the maker refused, a rejection, one type for good, another tenant's change; seven new rows of
      `PermissionMatrixApiTests`; the tenants' new checks in `MigrationsOnSqlServerTests`), and the script checks of
      `0019` in `MigrationsTests`. Changed expectations, each called out: `MigrationsOnSqlServerTests` lists nineteen
      migrations and ten checks on `Tenants`; `PermissionConventionTests` counts sixteen decisions;
      `TenantQueryFilterTests` and `TypedIdForeignKeyTests` list the new table and its two user keys; the matrix row
      "users" covers the settings and the prefix requests too.
    - **Verified on .NET 10 (SDK 10.0.404):** in the repository, the Release build and `dotnet format
      --verify-no-changes` are clean, and Domain 625, Application 392, Architecture 121 and the 658 integration tests
      without a database pass; in a throwaway copy on LocalDB 2019 the integration project passes in full (1114
      tests), and the settings, matrix, bill approval, migration and number series tests ran five more times without a
      failure.
    - **Still to do:** SQL Server 2022 in Docker. (The owner's decision on ADR 0033 built note point 5: made and built,
      item 31.)
31. **The prefix approval holds the series (ADR 0033, "Amended 2026-11-23"), written 2026-11-23, built and tested on
    .NET 10.** On `phase-2-billing`, its own commit between 5.3b and 5.4. The owner chose option B of three put in the
    plan ("approve as recommended"). No migration.
    - Built: `INumberSeries.HoldAsync`, the allocator's locked read alone (`HoldSeries`, a second constant statement in
      `NumberSeriesAllocator.cs`, under its own suppression of RS0030; no new sanctioned file); `ApprovePrefixChangeHandler`
      holds this year's series of the type after every check and before it reads the tenant and the series, so a
      number in flight finishes first and the approval changes the series it created, and a first number asked for
      meanwhile waits and starts from the new default. A number in the same instant no longer makes the approval answer
      409.
    - **Changed expectation, called out:** `BannedSymbolsTests` pinned one statement in the allocator's file and now pins
      two, both constants with the tenant in the WHERE clause and the same lock. The five test decorators of
      `INumberSeries` (`BillApprovalApiTests`, `BillPostingApiTests`, `PaymentsApiTests`) pass the hold through.
    - **Tests:** Application (the order of the hold, nothing held when refused; 2 new), Database (`NumberSeriesOnSqlServerTests`:
      a hold of a missing series and of an existing one, isolation and arguments, 4 new; `SettingsApiTests`: the
      approval stopped by the test in the gap of point 5, and ten rounds of a first bill and an approval at once, 2
      new). With the hold removed in a throwaway copy, both API tests fail, the second because a series kept the old
      prefix: the race is shown by a test, not only by reading the code.
    - **Verified on .NET 10 (SDK 10.0.404):** in the repository, the Release build and `dotnet format
      --verify-no-changes` are clean, and Domain 625, Application 394, Architecture 121 and the 658 integration tests
      without a database pass; in a throwaway copy on LocalDB 2019 the integration project passes in full (1120 tests),
      and the settings, number series, bill approval and bill posting tests (81) ran five more times without a failure.
    - **Still to do:** SQL Server 2022 in Docker.
    - Next: Prompt 5.4 (database logins and grants), with the plan approved on 2026-11-23: split into 5.4 (logins and
      grants, migration `0020_DatabaseRoles`, ADR 0034) and 5.4b (TLS and HSTS, hosts, server header, sign-in rate
      limit, CORS, `docs/security.md` in full). Done: item 32.
32. **Prompt 5.4, first half: database logins and grants (ADR 0034), written 2026-11-23, built and tested on .NET 10.**
    On `phase-2-billing`, after the fix of item 31. The plan was approved "as recommended" with the fix; it split the
    prompt: this is 5.4, the HTTP hardening and `docs/security.md` in full are 5.4b. Migration `0020_DatabaseRoles`,
    scaffolded empty in the throwaway copy with the repository's `dotnet-ef`, copied back and written by hand.
    - Built: the role `billingrecon_application` (owned by `dbo`) with schema-wide `SELECT, INSERT, UPDATE`,
      `DENY UPDATE, DELETE` on the ledger and the audit trail, `DELETE` on the four `IHardDeletable` tables only, and
      `Roles` and `__EFMigrationsHistory` read only; `docs/runbook.md` section 10 (the database, the two logins, the
      migrations as the migrator, the check); `docs/database.md` section 7 rewritten (it listed DELETE on two tables of
      the four); `docs/security.md` section 7.
    - Decided by the owner with the plan (ADR 0034 points 1 to 4): the grants in a migration; schema-wide with the
      markers' exceptions; the migrator's four roles; the database tests as the application's login.
    - **Settled or found in the build (ADR 0034 points 5 to 7):** the role is created `AUTHORIZATION [dbo]` (otherwise
      the migrator's user owned it); every run migrates as a login with the migrator's four roles (the plan kept the
      administrator; this proves the runbook); and **the application relies on `READ_COMMITTED_SNAPSHOT ON`**, which EF
      Core sets when it creates a database and a plain `CREATE DATABASE` does not: on a database made by hand, a read
      waited for the open transaction of an idempotent request until it timed out. The runbook's database and the
      fixture's set it, and a test checks it.
    - **Test support changed:** `SqlServerFixture` creates the application database, the two logins (random passwords
      per run) and their users; `ApiConnectionString` is the application's login, `ApiAdminConnectionString` and
      `AdminConnectionStringFor` the administrator's (Respawn, the probe database, the databases a test creates);
      `DatabaseTest.AdministratorApiContext` for a defect seeded on purpose, used by the eight helpers that change a
      ledger row, a balance or a document behind the application's back (their expectations are unchanged).
    - **Tests:** `DatabasePermissionsOnSqlServerTests` (the prompt's test: the application login gets 229 deleting a
      bill or changing or deleting an audit record or a ledger row; the API connects as that login, which is no
      administrator and holds the role alone; the role and grants belong to `dbo`; `READ_COMMITTED_SNAPSHOT`; every
      table's permissions against its markers, failing with the line to add); `MigrationsTests` (the twentieth
      migration and its script); `MigrationsOnSqlServerTests` lists twenty migrations. With the `DENY` on
      `AuditRecords` and the `GRANT DELETE` on `UserRoles` removed in a copy, four tests fail, among them the users API
      test that replaces roles.
    - **Verified on .NET 10 (SDK 10.0.404):** in the repository, the Release build and `dotnet format
      --verify-no-changes` are clean, and Domain 625, Application 394, Architecture 121 and the 660 integration tests
      without a database pass; in a throwaway copy on LocalDB 2019 the integration project passes in full (1127 tests),
      and the permission, idempotency, settings, users, readiness and migration tests (70) ran three more times without
      a failure.
    - **Still to do:** SQL Server 2022 in Docker (runbook section 8, row 5.4); the logins of runbook section 10 in each
      environment.
    - Next: Prompt 5.4b (HTTP hardening and `docs/security.md` in full), with its own plan. Done: item 33.
33. **Prompt 5.4b, second half: HTTP hardening and the security document (ADR 0035), written 2026-11-23, built and
    tested on .NET 10.** On `phase-2-billing`, after 5.4. The plan was approved "as recommended". No migration, no
    package (the rate limiter, CORS, forwarded headers and HSTS are in the shared framework).
    - Built: TLS ends at a reverse proxy; `UseForwardedHeaders` first, believing the loopback addresses and
      `ReverseProxy:KnownProxies` only; `TransportGuardMiddleware`, which refuses a host `AllowedHosts` does not list
      (400 `host_not_allowed`) and, outside Development, a request that did not arrive over HTTPS (400 `https_required`,
      no redirect), the two health endpoints excepted; HSTS for one year outside Development; CORS from
      `Cors:Origins`; no `Server` header; `SecurityHeadersMiddleware` (`nosniff`; `no-store` and a content security
      policy on `/api`); two sign-in limits answering 429 `too_many_sign_ins` with `Retry-After` (30 a minute per
      client address, the rate limiter's policy `sign-in`; an account held 15 minutes after 10 refusals,
      `SignInThrottleFilter`); the startup checks of every setting; `docs/security.md` written in full with its threat
      model; `docs/runbook.md` section 11.
    - Decided by the owner with the plan (ADR 0035 points 1 to 7), including the accepted risks of the threat model
      and a sentence each in prompts 7.1 (where the browser keeps the token) and 8.1 (TLS at the demo's nginx, the
      frontend's own headers) of `docs/build-prompts.md`.
    - **Found in the build (ADR 0035 built note):** the framework's host filter, which reads the same key, is set to
      let every host through; the test server has no client address, so a test-only startup filter gives one;
      `WebApplicationFactory.UseKestrel` (new in ASP.NET Core 10) hosts the API on real Kestrel for the `Server` header;
      HSTS is not on an answer of 500 (the exception handler clears the response).
    - **Changed in existing tests, none weakened:** every integration test host is called at `https://localhost` and
      names `localhost` in `AllowedHosts` (`ApiFactory`), as a client of a deployment is; the architecture test of the
      anonymous action gained a sibling for the rate limits. No existing expectation changed.
    - **Tests:** `TransportSecurityTests`, `CorsTests`, `SecurityHeadersTests`, `SignInRateLimitTests`,
      `TransportStartupValidationTests`, `KestrelTransportTests` (without a database, 78 new), four per-account tests in
      `AuthApiTests` (database), one architecture test. With the `Server` header switched back on, HSTS removed, the
      guard removed and the per-account filter removed in a throwaway copy, 20 of the tests run against it fail, each
      change caught by at least one.
    - **Verified on .NET 10 (SDK 10.0.404):** in the repository, the Release build and `dotnet format
      --verify-no-changes` are clean, and Domain 625, Application 394, Architecture 122 and the 738 integration tests
      without a database pass; in a throwaway copy on LocalDB 2019 the integration project passes in full (1209 tests;
      the readiness test of a pending migration first failed on a database an earlier run had left, see section 2),
      and the new tests with the sign-in tests ran three more times without a failure. The smoke run's checks passed
      against the published API on Kestrel with the smoke run's settings (alive, no `Server` header, 400
      `https_required`).
    - **Still to do:** SQL Server 2022 in Docker and the CI smoke run of the image (runbook section 8, row 5.4b); in each
      environment, `AllowedHosts`, the proxy's address and TLS at the proxy (runbook section 11).
    - Next: Prompt 6.1 (registers and balances). Phase 5 is written to its end; its merge to `main` is the owner's step.
      Done: item 34.
34. **Prompt 6.1, registers and balances (ADR 0036), written 2026-11-23, built and tested on .NET 10.** On
    `phase-2-billing` like the rest (the owner kept the branch with the plan). The plan was approved "as recommended". No
    migration, no package.
    - Built: the Reporting module in three layers (the Domain has none): eight GET reports under `/api/v1/reports`
      (bill register, payment register, daily collection, outstanding by payer, a payer's balance, unapplied cash,
      available credit, pending approvals), each paged report answering its totals over every page; the policy
      `Reports` for all five roles; the README placeholders of `Application/`, `Infrastructure/` and `Api/Reporting` are
      gone (`Domain/Reporting` keeps its own, having no code).
    - Decided by the owner with the plan (ADR 0036 points 1 to 7): five allowed exceptions of the module rule for
      `Infrastructure.Reporting` (ADR 0003, amendment of 2026-11-23) and a test that Reporting never writes; where each
      figure comes from; the daily collection from the Cash entries of the date; the endpoints; totals over every page;
      the policy; the branch.
    - **Found in the build (ADR 0036 built note and point 7):** Reporting's Application and Api layers may not name
      `BillStatus`, `PaymentStatus` or `PaymentMethod`, so the module has its own three words, mapped explicitly and kept
      in step by `ReportVocabularyTests`; the database cannot sum a `Money` behind its converter, so totals are added in
      memory over the matching rows' amounts; the payer of a balance is found through the Payers module's own query.
    - **Tests:** `ReportingHandlerTests` (Application, 14), `ReportsApiTests` (database, 8: every report after a month
      of one hospital, each figure against ledger sums in SQL, the IST date boundary, filters and paging, another
      tenant, an empty tenant, the reconciliation), `ReportVocabularyTests` (3), `ReportingConventionTests`
      (architecture, 3), eight rows of `PermissionMatrixApiTests` and the five exceptions of `ModuleDependencyTests`.
      With the refunds left out of the collection, zero balances listed, drafts let into the register and an `.Add(`
      in a Reporting file, in a throwaway copy, five tests fail, each change caught. No existing expectation changed.
    - **Verified on .NET 10 (SDK 10.0.404):** in the repository, the Release build and `dotnet format
      --verify-no-changes` are clean, and Domain 625, Application 408, Architecture 125 and the 741 integration tests
      without a database pass; in a throwaway copy on LocalDB 2019 the integration project passes in full (1220 tests),
      and the report and permission tests ran twice more without a failure.
    - **Still to do:** SQL Server 2022 in Docker (runbook section 8, row 6.1).
    - Next: Prompt 6.2 (reconciliation and audit trail). Done: item 35.
35. **Prompt 6.2, reconciliation and audit trail (ADR 0036, "Amended 2026-11-23 (Prompt 6.2)"), written 2026-11-23,
    built and tested on .NET 10.** On `phase-2-billing`. The plan was approved "as recommended". No migration, no package.
    - Built: `GET /api/v1/reports/reconciliation` (US-41), paged, with the count of each broken identity and the time of
      the check, an error in the log when it finds drift; identity 11, `NumberSeriesGap`, in the reconciliation's
      statement (no new raw-SQL file); the reconciliation of every tenant after every test of the `Database` collection
      (`DatabaseTest`), with `ExpectDrift` and `DriftLeftOnPurpose` for the tests that leave a defect on purpose;
      `docs/api.md` completed (every endpoint, the actions of the audit trail, the error-code catalogue of section 9)
      and kept complete by `ApiDocumentationTests`.
    - **The automatic check, first run:** it failed exactly the 41 tests that seed defects to see them found or take
      numbers without documents (the idempotency probe, the number series on its own, one race of ADR 0033); each now
      names the identities it leaves, and none leaves drift by mistake. No other expectation changed; the opt-outs are
      the only edits of existing tests, besides one row of `PermissionMatrixApiTests`.
    - **Found, for the owner (ADR 0036 amendment):** the audit image of a creation holds its decimals as written (`1`,
      `200`), later images at the column's scale (`1.000`, `200.0000`): equal values, different text. Left as it is;
      `EntitySnapshot` could read the created row back, at a read per creation.
    - **Tests:** `ReconciliationApiTests` (3: no drift after real postings; a changed balance and a skipped number
      reported with both figures and logged as an error; another tenant's drift never shown), `BillAuditTrailApiTests`
      (US-43: a Viewer reads ten state changes of one bill, each with its user and correlation id, "before" equal by
      value to the previous "after"), the report's handler in `ReportingHandlerTests`, `ApiDocumentationTests` (2).
    - **Verified on .NET 10 (SDK 10.0.404):** in the repository, the Release build and `dotnet format
      --verify-no-changes` are clean, and Domain 625, Application 409, Architecture 127 and the 741 integration tests
      without a database pass; in a throwaway copy on LocalDB 2019 the `Database` collection passes in full (483 tests),
      every one now ending with the reconciliation, and the new and opted-out classes ran twice more without a failure.
    - **Still to do:** SQL Server 2022 in Docker (runbook section 8, row 6.2).
    - Next: Prompt 6.3 (dashboard figures), which begins with a proposal of its user story for the owner. Done: item 36.
36. **Prompt 6.3, dashboard figures (ADR 0037), written 2026-11-23, built and tested on .NET 10.** On `phase-2-billing`.
    The proposal (requirements and design) was approved "as recommended". No migration, no package.
    - **New R1 scope** in `docs/requirements.md`: US-44, BR-902 and the matrix's row "Dashboard" (TenantAdmin, Supervisor,
      Viewer).
    - Built: seven GET panels under `/api/v1/dashboard` (`summary`, `trend`, `ageing`, `top-payers`, `recent-payments`,
      `pending-approvals`, `drift`) under the new policy `Dashboard`; a period of IST business dates (the last 30 days by
      default, at most 366, ending today at the latest) against the previous period of the same length; the figures of
      past days summed from the ledger by business date through `ILedgerMovements` (Receivables) and
      `LedgerMovementsQuery`, **the sixth file sanctioned for raw SQL** (two constant, read-only statements, pinned in
      `BannedSymbolsTests`); the four lists call the handlers of the 6.1 and 6.2 reports; the payment register takes
      `order=NewestFirst`.
    - Decided by the owner with the proposal (ADR 0037 points 1 to 8): **no trend for the drift card** (its count of now,
      no snapshot table); the ledger sums in SQL; collected gross of reversals and refunds; one endpoint per panel; the
      policy; percentages rounded to two places, amounts never rounded.
    - **Found in the build (ADR 0037 built note):** Entity Framework cannot sum `Money` behind its converter (a probe
      failed), hence the SQL; `ReportingConventionTests` refuses `.Add(` in Reporting, so the series are filled by index
      and an iterator (the test was not loosened).
    - **Tests:** `DashboardHandlerTests` (Application, 14), `DashboardApiTests` (database, 7: a month of one hospital with
      the clock walked through it, every card against ledger sums in SQL, the ageing total against the Receivable
      account, a bill due today in `0-30`, payments at 23:59 and 00:01 in India, the trend's daily series, another
      tenant, an empty tenant, refused periods), seven rows of `PermissionMatrixApiTests`, the register newest first in
      `ReportsApiTests` with an unknown order refused, and the pins of the new file in `BannedSymbolsTests` (a new test
      and a second case of a theory). Changed in existing tests, none weakened: `BannedSymbolsTests`
      pins a sixth file and its "only reads" theory covers two files; `ReportingConventionTests` checks that every
      controller of `Api.Reporting` has GET actions only, the dashboard's included. With four defects put in, one at a
      time, in a throwaway copy (a request not pending on its own day, the last day of the ledger sums lost, a previous
      period one day short, bills not yet due left out of the ageing), each is caught.
    - **Verified on .NET 10 (SDK 10.0.404):** in the repository, the Release build and `dotnet format
      --verify-no-changes` are clean, and Domain 625, Application 423, Architecture 129 and the 741 integration tests
      without a database pass; in a throwaway copy on LocalDB 2019 the `Database` collection passes in full (490 tests,
      each ending with the reconciliation), and the dashboard, report, matrix and reconciliation tests ran twice more
      without a failure. No query plan of the two new statements has been looked at.
    - **Still to do:** SQL Server 2022 in Docker (runbook section 8, row 6.3). The backend of R1 is written to its end;
      the merge of `phase-2-billing` to `main` is the owner's step.
    - Next: Prompt 7.1 (scaffold and sign-in). Done: item 37.
37. **Prompt 7.1, the frontend's scaffold and sign-in (ADR 0038), written 2026-11-23, built and tested on the office
    machine.** On `phase-2-billing` (the owner's answer with the proposal). The proposal was approved "as recommended"
    with its four answers: the token in `sessionStorage`, ESLint 9 with the accessibility rules, this branch, the OpenAPI
    snapshot refreshed by a .NET test. 27 npm packages approved and pinned; no NuGet package, no migration.
    - Built: `frontend/` (section 2 and 3): the sign-in (US-02) with the hospital code remembered; the session from the
      token, ended at its expiry, on sign-out and on a 401; the shell with the hospital, the user, the theme chooser and
      the navigation of the permission matrix, one placeholder page per section naming the prompt that builds it, a
      no-access page and a not-found page; `ProblemAlert`; the typed client; the design tokens, the theme provider and
      the inline theme script; the CI job and Dependabot's npm entry; `OpenApiSnapshotTests` (.NET, 2 tests).
    - **Changed in the API, called out:** `Program.cs` gives `ConfigureHttpJsonOptions` the controllers' enum converter,
      because the built-in OpenAPI generator reads those options: without it the document gave all 27 enums as numbers.
      The wire did not change; the whole `Database` collection and every other test pass with it.
    - **Found, for the owner (ADR 0038 built note):** the document gives amounts and counts as `number | string`, which is
      true: the API's JSON defaults read a number written as a string (it always writes numbers). Making the API strict
      is a change of the API, left as it is. No prompt builds the users and settings screens; the TenantAdmin's section
      "Users and settings" says so.
    - **Tests:** the frontend's 161 (`docs/testing-strategy.md` section 5a): the sign-in and its refusals, the shell per
      role and its expiry, the navigation against a copy of the matrix for each role, the session and the return
      address, the client and ProblemDetails, the theme chooser, the inline script run as the browser does with its
      SHA-256 pinned, no raw colour in any file, WCAG AA contrast of both themes. With a raw colour, approvals shown to a
      clerk, a changed theme script and a faint muted text put in one at a time, each is caught; with the enum converter
      taken out in a throwaway copy, both snapshot tests fail.
    - **Verified:** in `frontend/` with Node 24.22.2, `npm audit` (no advisory), the format check, lint, types, 161 tests
      and the build; in the repository, the Release build and `dotnet format --verify-no-changes` are clean, and Domain
      625, Application 423, Architecture 129 and the 743 integration tests without a database pass; in a throwaway copy on
      LocalDB 2019 the `Database` collection passes in full (490). **In the browser**, against the API in Development on a
      throwaway LocalDB database: a Supervisor and a Cashier signed in and saw their own sections, the Cashier was refused
      the dashboard by its address, a wrong password showed the API's message with its reference, the three theme
      settings applied without a reload and a hard reload kept Dark. Not seen in the browser: the live switch under System
      (the pane's emulation fires no `change` event; the component test covers it).
    - **Still to do:** the CI job's first run; `npm ci` and `npm test` on the personal machine, whose Node version is not
      recorded (runbook section 8, row 7.1). The CSP of 8.1 lists the inline script's hash (`docs/security.md` section 6).
    - Next: Prompt 7.2 (patients, payers and catalogue screens). Done: item 38.
38. **Prompt 7.2, patients, payers and catalogue screens (ADR 0039), written 2026-11-23, built and tested on the office
    machine.** On `phase-2-billing`. The plan was approved "as recommended" with its two answers: payers in a section of
    their own, and a conflict that lists what changed and offers both choices. No package, no migration, no change of the
    API.
    - Built: for patients, payers and service items, a list (search, status filter, pages of 20, all in the address), a
      record page (details, status, Edit, Deactivate with a confirmation, Reactivate; a patient's payer, or the way to
      create it; a payer's patient), and the create and correct forms. Read by every role, changed by TenantAdmin,
      Supervisor and BillingClerk; the write routes answer "No access" to the others. The shared pieces of every later
      screen: fields, list controls, page header, status badge, conflict notice, inline confirmation, `useRecordForm`,
      `useStatusActions`, `answer` and `ifMatch`, the policies' copy, Indian formats, decimal checks.
    - **Fixed, from 7.1, called out:** `OpenApiSnapshotTests` writes and compares the API's document with every `\r\n`
      inside its descriptions as `\n`. The generator joins the lines of an XML comment with the machine's line break, so
      the snapshot written on Windows would have failed on the Linux runners. 242 descriptions changed in the committed
      copy; the generated types did not.
    - **Changed expectations, called out:** the navigation's matrix table and the shell tests gain `/payers` for all five
      roles, and "Patients and payers" became "Patients" (ADR 0039 point 6).
    - **Tests:** 220 frontend tests (59 new; `docs/testing-strategy.md` section 5a), against `routedApi`. With five defects
      put in one at a time (`If-Match` unquoted, a conflict listing nothing, every role allowed to change, the status
      filter not sent, the digit limit dropped), each is caught.
    - **Verified:** in `frontend/`, `npm audit` (none), the format check, lint, types, 220 tests and the build, and the
      generated types equal to the committed document; in the repository, the Release build and `dotnet format
      --verify-no-changes` are clean, and Domain 625, Application 423, Architecture 129 and the 743 integration tests
      without a database pass (the `Database` collection was not run again: no production code of the API changed). **In
      the browser**, against the API in Development on a throwaway LocalDB database: paging through 23 patients, the API's
      refusals at the field and in the error component, a patient registered and their payer created, a real conflict
      (the record changed through the API behind the open form) kept and saved over, a service item priced 1250000.1234
      stored exactly and deactivated after the confirmation, and a Viewer with no action and no access to the forms.
    - **Still to do:** the CI job's first run (runbook section 8, row 7.2).
    - Next: Prompt 7.3 (bill screens). Done: item 39.
39. **Prompt 7.3, the bill screens (ADR 0040), written 2026-11-23, built and tested on the office machine.** On
    `phase-2-billing`. The plan was approved "as recommended" with its four answers: one editor for a draft, the key of an
    operation in memory, live totals on `BigInt` with no package, names in the list read per patient and payer. No
    package, no migration, no change of the API.
    - Built: the bill list (status and bill-date filters, a patient's or payer's bills from their pages, names per row);
      the bill's view (status, number and fiscal year, parties, lines, the server's totals and balance, the void reason, a
      banner while it waits for approval, the audit trail); post (confirmed; 200 posted with its number, 202 sent for
      approval) and void (with a reason) for TenantAdmin, Supervisor and BillingClerk; the draft editor (patient and payer
      pickers, the patient's own payer chosen for them, lines from the catalogue or in free text, per-line checks and the
      API's per-line messages at their fields, live totals as an estimate). Shared from here on: `RecordPicker`,
      `AuditTrail`, `useIdempotentRequest`, `answerWithStatus`, and `useRecordForm`'s mapping of the API's field names.
    - **One file of cases for two arithmetics:** `frontend/src/bills/lineCases.json` (18 lines) holds both the browser's
      `lineMath.ts` and the server's `LineCalculator` (`LineCalculatorSharedCasesTests`, Domain, new).
    - **Changed in existing tests, called out:** three tests that used `/bills` as a page without requests (sign-out,
      the notice after it, the sign-in's return) use `/reports` instead, since the bill list now reads the API; their
      expectations are the same.
    - **Tests:** 262 frontend tests (42 new), Domain 644 (19 new). With five defects put in one at a time (a new key every
      time, "still running" not retryable, midpoints rounded down, the lines always sent, a 202 taken as posted), each is
      caught.
    - **Verified:** in `frontend/`, `npm audit` (none), the format check, lint, types, 262 tests and the build, and the
      generated types equal to the committed document; in the repository, the Release build and `dotnet format
      --verify-no-changes` are clean, and Domain 644, Application 423, Architecture 129 and the 743 integration tests
      without a database pass (the `Database` collection was not run again: no production code of the API changed). **In
      the browser**, against the API in Development on a throwaway LocalDB database with an approval threshold of 5,000:
      a draft whose estimate (2,328.21) the server stored to the paisa, corrected by a `PATCH` and a `PUT` (2,511.07 on
      both sides), posted as BIL/26-27/000001 with its trail; a bill of 9,000 sent for approval (202); a draft voided with
      its reason; a Viewer with names in the list, no action, and no access to a new bill.
    - **Still to do:** the CI job's first run (runbook section 8, row 7.3).
    - Next: Prompt 7.4 (cashier screens).
