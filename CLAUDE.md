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

**Company:** **ashline**, the owner's studio (https://ashish-singh-ai-studio.vercel.app/). The product is credited as
"A product of ashline" and "BillingRecon by ashline"; the company's mark is `frontend/src/assets/brand/ashline-logo.svg`,
copied path for path from its site on 2026-11-24 and never redrawn (`docs/build-prompts.md`, Phase 9, has its colours and
how its name is set).

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
| Runtime | **.NET 10 (LTS)** | `Directory.Build.props` holds the single `TargetFramework` (`net10.0`); `global.json` pins the SDK to the **10.0.400** feature band with `latestPatch` (the personal machine's band, read from its build outputs on 2026-10-17). Supported to November 2028. Framework-bound packages (ASP.NET Core, EF Core, `Serilog.AspNetCore`) stay on the same major version. Both machines have a .NET 10 SDK, the office machine since 2026-11-21 (dev machine notes below). See ADR 0014, which supersedes ADR 0001. |
| Web API | **ASP.NET Core Web API with controllers** | No minimal APIs. Controllers derive from `ApiControllerBase`, are `sealed`, end in `Controller` (architecture test). Every route is under `/api/v1` (ADR 0013). Every error is an RFC 9457 ProblemDetails (ADR 0011). Health checks at `/health/live` (process is up) and `/health/ready` (the application database is reachable and has no pending migration), both anonymous. **Transport (ADR 0035):** TLS ends at a reverse proxy; forwarded headers are believed from the loopback addresses and `ReverseProxy:KnownProxies` only; outside Development a request that did not arrive over HTTPS gets 400 `https_required` (no redirect) and an answer over HTTPS carries HSTS for one year; a host `AllowedHosts` does not list gets 400 `host_not_allowed`; the health endpoints answer probes over plain HTTP for any host; CORS from `Cors:Origins` only, without credentials; no `Server` header; `nosniff` on every response, `no-store` and a content security policy on the answers of `/api`. **Diagnostics (ADR 0046):** `Diagnostics:ServerTiming`, off by default and for a load test only, writes how long a request waited for its number series and held it into `Server-Timing`. |
| Authentication | **JWT bearer** (`Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.12) | The API issues its own tokens at `POST /api/v1/auth/login` and checks them on every request: HS256 with `Jwt:SigningKey`, issuer `BillingRecon`, audience `BillingRecon.Api`, 60 minutes by the application's clock, claims `sub`, `tenant_id`, `role`, and `pwd_change` while a password change is due (ADR 0051: a 15-minute token good for `POST /api/v1/auth/password` alone, every other endpoint 403 `password_change_required`, `PasswordChangeGateMiddleware`; a suspended hospital's token 403 `tenant_suspended` within 30 seconds, `SuspendedTenantGateMiddleware`, ADR 0052), and `ses`, the number of the user's sessions (ADR 0057: every request with a token is held against the user's row, uncached, by `SessionGateMiddleware`; a sign-out at `POST /api/v1/auth/logout`, a changed or reset password, a disabling and changed roles raise `Users.SessionVersion`, and a token issued before answers 401 `session_ended`). The tenant and user of a request come from its token only (`RequestPrincipalIdentity`). Deny by default: a fallback policy needs a user on every endpoint but the sign-in, the health checks and, in Development, OpenAPI and Scalar (an architecture test lists them). In Development a request without a token acts as the development user. ADR 0031. **Roles:** every action names exactly one policy of `Policies`, one per capability of the permission matrix (`docs/requirements.md` section 1); a role the policy does not admit gets 403 `forbidden`; a table-driven test walks every endpoint with every role. The checker's role (BR-603) is also checked by `ApprovalRequest` itself. ADR 0032. **Sign-in limits (ADR 0035):** 30 sign-ins a minute per client address (the rate limiter's policy `sign-in`) and an account held 15 minutes after 10 refusals (`SignInThrottleFilter`), both 429 `too_many_sign_ins` with `Retry-After`; fixed in code, in memory per instance. |
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
| **`deploy/load`** (Node, no package) | The load test of `docs/requirements.md` section 5 against a running stack: 50 users, p95 of posting and recording, the number series' wait and hold from `Server-Timing`, zero drift after; run by hand, not in CI (ADR 0046, runbook section 14) |
| **Playwright** (`@playwright/test` 1.65.1) | The browser journey (`frontend/e2e`): clerk, cashier, supervisor, against a running stack; run by hand, not in CI (ADR 0046) |

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
| Screens (ADR 0039) | Lists, records, forms | A list keeps its search, status and page in the address. Since 10.2b (amended 2026-11-25) a search is sent while it is typed, after 300 ms and from 2 characters (`useLiveSearch`), the rows kept until the new ones arrive, a stale request cancelled, the address replaced and not pushed; no screen has a Search or Find button; `RecordPicker` is an autocomplete in the combobox pattern of WAI-ARIA (ADR 0040, amended). Since 10.4d (amended 2026-11-26) every search box is `SearchInput` with its lens, a search is also sent when its box is left, and Tab in a picker takes the first result (a single match only for the payer of a payment). Every change sends `If-Match` from the record's `version`. Who may change is `src/auth/permissions.ts`, a copy of the API's policies that a test holds to `Policies.cs`. Deactivate asks once more, in place. A 409 `concurrency_conflict` says nothing was saved, lists what changed, and offers "Load the latest version" or "Keep my changes". Decimals are typed as text, sent as JSON numbers, refused beyond 15 significant digits; amounts shown with Indian grouping. The API's field messages go at their fields. |
| Bills (ADR 0040) | One editor for a draft; the key of an operation | A new draft is one `POST` with its lines; a correction sends what changed, the header (`PATCH`) then the lines (`PUT`) with chained versions. Create, void and post carry an `Idempotency-Key` from `useIdempotentRequest`: the same key when the same request is sent again after no answer, a fault or 409 `idempotency_in_progress`, a new one otherwise. Live totals are an estimate by exact arithmetic on `BigInt` (`src/bills/lineMath.ts`), held with the server's `LineCalculator` to one file of cases (`src/bills/lineCases.json`); after a save the server's figures are shown. Posting answers posted (200) or sent for approval (202). Every record's history is the `AuditTrail` component. |
| Cashier (ADR 0041) | Allocate oldest first; refunds from their source; the payer statement | Allocating lists the payer's bills that owe something, oldest first, with "Apply oldest first"; the caps (each bill's balance due, the unapplied cash) are checked in exact paise (`src/money.ts`) before sending, and again by the server. A refund is drawn from a payment or a credit note, from its page or the payer statement (`/payers/:id/statement`). Every financial action on a document (reversal requests, a refund's void and submission) goes through `useDocumentAction`: the key of the operation, the retry, the conflict, the outcome. Approving a reversal or a refund is 7.5's. |
| Inbox, credit notes, adjustments (ADR 0042) | Decide in the inbox; draft from the bill | `/approvals` (policy `Decide`) lists what waits from the pending approvals report, which answers `parentDocumentId` (an allocation's payment, an application's credit note) and `makerComment`; **Review** reads the document as it is now and sends Approve (comment optional) or Reject (comment required) with that document's version and a key (none for a prefix change). The maker's own request shows no review. Credit notes and adjustments are drafted from the posted bill (`?billId=`); a credit note credits the bill's lines by quantity at their price and tax, plus free-text lines, capped per line and in total before sending. A credit note's credit is applied with the shared `ApplyToBills` form of the allocation. A document that waits links a checker to the inbox (`ReviewLink`). |
| Reports (ADR 0043) | Eight screens for the six of BR-901; CSV in the browser | `/reports` and one screen per report, filters in the address, the server's totals in the footer. **Export CSV** reads every page of the filter (100 rows a request, at most 10,000), holds the rows to the server's totals (once more if they differ, then refused), and writes UTF-8 with a BOM, plain decimals, quoted fields and the formula guard (`src/reports/csv.ts`). One definition of the columns serves the screen and the CSV (`src/reports/columns.tsx`). |
| Session | **`sessionStorage`** | The token, its expiry and the roles read from it; gone with the tab, at expiry and on a 401 (row S5). Sign out tells the API (`useSignOut`) and, through a `localStorage` signal removed at once, the same user's other tabs (ADR 0057). The roles choose the navigation only; the API is the authority. |
| Charts (7.6) | **Recharts 3.10.1** | SVG, so a series drawn with `var(--series-n)` follows the theme without a re-render. Loaded with the dashboard's route only (ADR 0044). |
| Dashboard (ADR 0044) | One card, one colour token | Six cards from one component, each one `--series-n` token mixed into the surface (stronger in dark), the change coloured by meaning, money compact (₹12.8L) with the exact amount in the tooltip and for screen readers (`src/dashboard/compact.ts`); billing against collections with a table of its figures; the ageing donut; four panels with "View all", each with its own skeleton, empty state and error. Layout reference `docs/ui/dashboard.png`. |
| Tests | **Vitest**, jsdom, React Testing Library | The API faked by a `fetch` given to the client (`src/test/app.tsx`); no mocking package. Vitest reads `src/` only. |
| Browser journey (ADR 0046) | **Playwright** 1.65.1 | `e2e/journey.spec.ts`, its own `tsconfig.e2e.json`, `npm run e2e`; against https://localhost by default (`E2E_BASE_URL`), Chromium or an installed browser (`E2E_BROWSER_CHANNEL`); a console error on any page fails the step, the browser's `/favicon.ico` excepted. |
| Lint and format | **ESLint 9** (type-aware TypeScript, hooks, `jsx-a11y`, no `dangerouslySetInnerHTML`), **Prettier** | ESLint stays on 9 until `jsx-a11y` supports 10. Warnings are errors. One `eslint-disable`, in a test, with its reason. |
| Brand and tokens (ADR 0047) | Inter, self-hosted; our own icons; the sign-in page | Since Prompt 9.1 `tokens.css` holds, for both themes, the brand's blues, the sign-in page's ground, shadows, radii, spacing, type sizes and durations; a token's name holds no CSS colour word (the scan reads it as one). Inter comes from `@fontsource-variable/inter`, bundled and served from our origin, with a metric-matched fallback. Icons are `src/brand/Icon.tsx` (inline SVG, `currentColor`); `BrandMark` is the product's mark, `CompanyCredit` the ashline logo and name. The sign-in page is laid out in the pixels of `docs/ui/sign-in.png` (`--u`), animates only transform and opacity, and nothing with reduced motion. `VITE_SUPPORT_EMAIL` (from `SUPPORT_EMAIL` in `.env`) is the sign-in page's "Contact support". Since 9.2 (ADR 0048): the shell has a sidebar with an icon per section, a rail (kept in `localStorage`) and a drawer under 900 px; the theme is a pill of three (`ThemeToggle`, a radio group named "Theme"); a page change names the tab and focuses the heading; badges carry an icon from `components/tones.ts`; loading is a skeleton (`LoadState`), an empty list an `EmptyState`; `/design`, the specimen of the shared components, exists in Development builds only. Since 9.3 (ADR 0049): money is `formatMoney` and `<Money>` (a sign and a word for a credit or a negative amount); every status of every document is one mapping, `components/documentStatuses.ts`, with its word, tone and icon; a document's totals are `TotalsCard` (an estimate says so; a changed figure is marked); a document's state is a `Banner`; the audit trail is a timeline; a field may carry its unit and a required mark. Since 9.3b: allocating and applying credit show `ApplyMeter`; the statement's four figures are tiles; the inbox is a list of cards. Since 9.4 (ADR 0050): one tooltip (`dashboard/Tooltip.tsx`: `TipBox`, `Tip`; `useChartTip` for Recharts) on `--surface-popover` and `--border-strong`, opened by hover, focus and tap, closed by Escape, blur and a second tap; the donut is drawn here; every chart has its table; the period is a segmented control; no inline style on the dashboard. Since 9.4b: every route (and the shell) loads on demand within budgets that `npm run budgets` holds in CI; a report's table keeps its header and totals in view; print on A4 (`@media print` in `tokens.css`, `base.css` and the modules); `e2e/routes.spec.ts` runs every route with axe. |
| Packages | 31, pinned exactly (Recharts added by 7.6, `@playwright/test` by 8.2, `@fontsource-variable/inter` by 9.1, `@axe-core/playwright` by 9.4b), `package-lock.json` committed | `npm ci` in CI; `npm audit` fails on any advisory; Dependabot a week after a release. A new package needs approval (section 6). |

### Delivery

Docker Compose runs it all (ADR 0045, Prompt 8.1, runbook section 13): `sql` (SQL Server 2022 Developer) and `seq`;
`migrate` (the database and the two logins as the administrator, the idempotent migration script as
`billingrecon_migrator`, `sqlcmd -I`); `api` outside Development as `billingrecon_app`; `frontend`, nginx with the app,
TLS with a self-signed certificate made on its first start, the app's own headers (CSP with the theme script's hash,
`frame-ancestors 'none'`, nosniff, HSTS), `/api` passed to the API as its one known proxy; and `demo-seed`, which with
`DEMO_ENABLED=true` fills the hospital `demo` through the API (`deploy/demo/seed.mjs`) after the API's opt-in bootstrap of
that hospital and its administrator (`DemoTenantSeeder`); and, only with `--profile load`, `load`, the load test of Prompt 8.2
(`deploy/load/load.mjs`, ADR 0046, runbook section 14). Licence acceptances, passwords and the signing key come from
`.env` (copy `.env.example`), which git ignores. The API has a multi-stage image, `src/BillingRecon.Api/Dockerfile` (Debian-based runtime with
time zone data, non-root user, health check; ADR 0017). GitHub Actions, `.github/workflows/ci.yml`, runs on every
push to `main` and every pull request: restore with the NuGet audit, `dotnet format --verify-no-changes`, build
with warnings as errors, every test including the database tests (the runners have Docker), coverage and a
vulnerability report as artifacts, the idempotent migration script, and the image built and smoke-run; and since
Prompt 7.1 a third job, the frontend: `npm ci`, `npm audit`, format, lint, the generated API types against the committed
document, types and build, tests, and since Prompt 8.2 the load test's arithmetic (`node --test`). Dependabot keeps NuGet, npm and the actions current (`.github/dependabot.yml`). Line endings are LF everywhere
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
- Recorded from the first run of `deploy/verify/personal-machine.ps1` (2026-11-24): SDKs 8.0.428, 10.0.400, 10.0.401
  and 10.0.404; Docker 29.8.1; Node 22.12.0 and npm 10.9.0 (22.22 or later is needed; whether it was upgraded is not
  recorded, as no second run of the script was made, 2026-11-26).

Office machine:

- `dotnet --list-sdks`: 5.0.207, 8.0.405, 9.0.102.
- `node --version`: **v24.22.2**, npm 11.19.1 (recorded 2026-11-23; it was v6.10.1 on 2026-09-26). The frontend builds
  and tests here, and the npm registry is reachable.
- Git 2.54.0.windows.1. Default branch `main`.
- **Disk: about 1 GB free on C: (recorded 2026-11-24)**, the only drive. A full load run (Prompt 8.2) filled it; keep
  throwaway databases and copies small and drop them when done, and run the full-size load on the personal machine.
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
docker-compose.yml             The one-command run (ADR 0045): SQL Server, Seq, migrate, api, frontend (nginx) and demo-seed; load behind
                               a profile (ADR 0046). First run
                               with Docker on the owner's machine on 2026-11-24 (runbook section 8, row 8.1).
.env.example                   Template for .env (licence acceptances, passwords, the signing key, the demo, ports). .env itself is git-ignored.
deploy/                        migrate/ (the migration image and its SQL steps), demo/seed.mjs (the demo through the API), and
                               load/ (load.mjs, the load test; stats.mjs, its arithmetic, held by stats.test.mjs; ADR 0046).
                               prod/ (env.example, prepare.sql for TDE, the backup loop, the restore drill, certbot.sh; ADR 0055).
.dockerignore                  What the API image build never sees: build output, tests, docs, .env, the git history.
.github/workflows/ci.yml       GitHub Actions: format, build, tests with Docker, coverage, vulnerability report,
                               migration script, API image built and smoke-run (ADR 0017).
.github/workflows/release.yml  A tag v* builds and pushes the three images and deploys staging; production by hand (ADR 0055).
docker-compose.prod.yml        Production and staging on one Linux server: images by tag, secrets as files of the host,
                               certbot, backups and their off-site copy (ADR 0055, runbook section 19). Not run yet.
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
                                the ledger by business date (Receivables). IRequestTimings with RequestTiming, the
                                durations measured while a request ran (Common), ADR 0046.
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
                                NumberSeriesTimer, the wait and hold of a number series, marked by the allocator and by
                                whoever ends the transaction; the idempotency store reads its key's row at the
                                reservation (Common), ADR 0046. LikePattern, the escaped pattern of a search that
                                matches a part of a text (Common/Persistence; ADR 0018, amended 2026-11-25).
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
                                document says what the wire carries (ADR 0038, built note). Common/Diagnostics
                                (ADR 0046): DiagnosticsOptions, ServerTimingMiddleware and AddDiagnostics.
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
                                   or its class with DriftLeftOnPurpose (ADR 0036, Prompt 6.2). Diagnostics/: the
                                   Server-Timing without a database (ADR 0046).
  BillingRecon.ArchitectureTests/  NetArchTest and file scans: layer rule, module rule with its listed exceptions,
                                   every type in a known module, banned packages in every project file, controller
                                   conventions, no minimal APIs; the guard of the banned-API list
                                   (BannedSymbolsTests); the one anonymous action and the one user of the sign-in
                                   scope, and the sign-in's two rate limits on it alone (AuthenticationConventionTests);
                                   one policy of the catalogue on every action,
                                   and the checker's roles in every decision (PermissionConventionTests); Reporting
                                   writes nothing (ReportingConventionTests); docs/api.md names every endpoint and
                                   every error code (ApiDocumentationTests); docs/traceability.md has a row with a test for every user story, and
                                   every test it names exists (TraceabilityTests, Prompt 8.3).
frontend/                      React + TypeScript + Vite (ADR 0038): index.html with the inline theme script;
                               src/theme (tokens.css, the provider and the chooser), src/auth (the session, the
                               sign-in, the route guard), src/api (openapi.json, the generated schema.d.ts, the client,
                               ProblemDetails as ApiProblem, call.ts: answer, ApiError, ifMatch), src/components
                               (ProblemAlert, the fields, the list controls with listParams, the page header, the status
                               badge, the conflict notice, the inline confirmation, useRecordForm, useStatusActions), useLiveSearch (the search sent while
                               it is typed, 10.2b),
                               src/shell (the navigation from the permission matrix, the shell, the placeholder pages,
                               RequirePolicy), src/patients, src/payers and src/serviceItems (each: api.ts with its queries
                               and changes, the list, the record and the forms; ADR 0039), src/bills (api.ts, the list, the
                               view with useBillActions, the editor with LinesEditor, the pickers, lineMath.ts and
                               lineCases.json; ADR 0040), src/components also RecordPicker and AuditTrail, src/api also
                               useIdempotentRequest, src/payments (api.ts, the list, the record form, the payment's page
                               with its reversal requests, AllocatePage) and src/refunds (api.ts, the list, the form from
                               its source, the refund's page), src/payers also StatementPage, src/components also Badge,
                               ReasonForm and useDocumentAction, money.ts (exact paise; ADR 0041), src/approvals (api.ts
                               with how each of the eight kinds is read and decided, the inbox, ReviewLink), src/creditNotes
                               (api.ts, the list, the page, the editor with CreditLinesEditor, ApplyCreditPage) and
                               src/adjustments (api.ts, the list, the page, the form; ADR 0042), src/bills also
                               ApplyToBills (the allocation and application form) and the payer's open bills, src/reports
                               (api.ts, csv.ts, exportReport.ts, columns.tsx, view.ts, ReportParts, the index and the eight
                               screens; ADR 0043), src/dashboard (api.ts, compact.ts, SummaryCard,
                               Charts, Panels, DashboardPage; ADR 0044), src/auth/permissions.ts (the
                               API's policies), format.ts and decimals.ts, routes.tsx, Providers.tsx; src/test (the app
                               renderer, routedApi, the records, the colour scan, the contrast and theme-script tests).
                               The OpenAPI document is kept equal to the API's by OpenApiSnapshotTests (IntegrationTests).
                               src/assets/brand: the company's mark, ashline-logo.svg. src/brand (Icon, BrandMark,
                               CompanyCredit), src/config.ts (what the build was given), public/favicon.svg, and
                               e2e/signin.spec.ts, the sign-in page in the browser (ADR 0047). src/shell/usePageFocus.ts,
                               src/components/tones.ts, EmptyState, src/theme/ThemeToggle, src/design (the specimen page)
                               and e2e/shell.spec.ts (ADR 0048). src/components also Money, DocumentStatus with
                               documentStatuses.ts, TotalsCard, Banner and Summary.module.css, and e2e/money.spec.ts
                               (ADR 0049). src/components also ApplyMeter, src/approvals/Approvals.module.css, and
                               e2e/cashier.spec.ts with e2e/stubbed.ts, the helper of the stubbed suites (ADR 0049, amended).
                               src/dashboard also Tooltip.tsx with Tooltip.module.css and useChartTip.ts, and
                               e2e/dashboard.spec.ts (ADR 0050). scripts/budgets.test.mjs, e2e/routes.spec.ts and
                               e2e/print.spec.ts (ADR 0050, amended).
                               src/components/LiveSearch.test.tsx and RecordPicker.test.tsx, and e2e/search.spec.ts (10.2b).
                               src/auth/useSignOut.ts and e2e/signout.spec.ts (ADR 0057).
                               src/imports (api.ts, FileField, Findings with findingsCsv.ts, the index, the two imports, a
                               batch's page) and e2e/imports.spec.ts (ADR 0053, amended).
                               src/printing (api.ts, PrintSheet, PrintLink, the three printed pages, tax.ts, words.ts) and
                               e2e/printed.spec.ts (ADR 0054, amended). src/administration/LetterheadImages.tsx (the
                               upload cards), the image helpers of src/printing/api.ts and e2e/letterhead.spec.ts
                               (ADR 0059). src/printing also settings.ts, SheetFrame, the three bodies, PrintPreview and
                               samples.ts, src/administration/PrintSettingsPage.tsx and e2e/printsettings.spec.ts
                               (ADR 0059, amended). src/components/SearchInput.tsx and SearchTab.test.tsx, and
                               e2e/searchtab.spec.ts (ADR 0039 and ADR 0040, amended 2026-11-26).
                               src/administration (api.ts, the users list, a user's page, the user forms with
                               RoleChoices, the settings with the prefix request) and src/auth/ChangePasswordPage.tsx
                               (ADR 0051).
                               e2e/journey.spec.ts with playwright.config.ts and tsconfig.e2e.json: the browser journey
                               (ADR 0046).
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
    An entity marked `IAppendOnly` (`AuditRecord`, the ledger's two, `DocumentPrint` and, since ADR 0059, `TenantImage`) cannot be updated or deleted through
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
| `docs/build-history.md` | What the owner decided for each built prompt and how it was built where that differs, as written on its day; moved out of `docs/build-prompts.md` on 2026-11-26 (Prompt 10.0-close) |
| `docs/security.md` | Authentication and the sign-in limits, authorisation, tenancy, maker-checker, transport (TLS, hosts, CORS, headers), secrets, data protection, the database logins, and the threat model of R1 (section 10: every threat done, accepted with its reason, or named with its prompt). Written in full with Prompt 5.4b |
| `docs/testing-strategy.md` | Test pyramid, tooling, database tests, architecture tests, the frontend's tests (section 5a), the load test and the browser journey (section 5b), CI gates |
| `docs/runbook.md` | Written so far: local infrastructure, the "Verify on a Docker machine" checklist, migrations, tests, CI, the container image, idempotency keys, the deferred verification list, sign-in with the signing key (section 9), the database logins with the migrations outside Development (section 10), the proxy in front of the API with its hosts and origins (section 11), the frontend with its commands, a local sign-in and the OpenAPI snapshot (section 12), the one-command local run with the demo (section 13), the load test and the browser journey (section 14), backup and restore (section 15) and the incident procedures (section 16), with rolling back a migration in section 3. Complete for R1 (Prompt 8.3) |
| `docs/traceability.md` | Each user story, US-01 to US-44, with the tests that prove it; `TraceabilityTests` keeps it complete and every test it names real (Prompt 8.3) |
| `deploy/reference/` | `generate-export.mjs` and `export/`: the synthetic export of the reference hospital, in the layouts of runbook section 18, which `OpeningBalancesApiTests` imports whole (ADR 0058) |
| `docs/reference-hospital.md` | The fictional hospital Phase 10 is built for while there is no client: its size, settings, cut-over date, synthetic export, documents, staff and letterhead, and what a real hospital changes (ADR 0058) |
| `docs/go-live.md` | What stands between the product and a first live hospital outside the code: the open sign-offs, each with who decides it. Begun by Prompt 10.4, completed by Prompt 10.7 |
| `docs/backlog.md` | What R1 leaves open: owner questions, recorded deviations, accepted limitations and follow-ups, each with where it came from and when it is due (Prompt 8.3, from the items of section 9) |
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
Prompt 2.2 plan, 2026-10-17; "Amended 2026-11-25 (Prompt 10.2b)": every search matches a part of the text, the MRN
through a cast of its converted column, with ADR 0039 and ADR 0040 amended the same day for the search sent while it is
typed and the picker as an autocomplete), 0019 bill drafts: replaceable lines, one version for the whole bill, and the
guard against deletes (Accepted for the points of the Prompt 2.4 plan and, with it, the amendment of ADR 0003 for the foreign
keys of `Bills`, 2026-11-20; its unit-of-work point accepted as built on 2026-11-24), 0020 domain events are
dispatched by the unit of work, and the audit trail is their one subscriber (Accepted with the approval of the
Prompt 3.4 plan, 2026-11-20; it also builds the append-only guard and names the migration `0007_Audit`),
0021 typed-id foreign keys by convention, a data step written by hand, and two more files with raw SQL
(Accepted on the owner's decisions of 2026-11-20 for Prompt 3.3).
0022 posting a bill: the order of its locks, the bill that comes to nothing, and what a posted bill answers
(Accepted on the owner's decisions of 2026-11-20 for Prompt 3.5; it amends BR-401),
0023 maker-checker: the approval request, where its decisions live, and what a waiting bill answers (Accepted on
the owner's decisions of 2026-11-20 for Prompt 3.6),
0024 allocating a payment: two aggregates through a Billing contract, the order of work, and the one lock
(written 2026-11-21 from the code of Prompt 4.2; Accepted as built on 2026-11-24, with the release of R1),
0025 credit notes: always a second person, the caps of BR-210 and BR-211 measured again under the ledger's lock,
and applying the credit (Accepted on the owner's approval of the Prompt 4.3 plan, 2026-11-21),
0026 adjustments: no exemption from the second person, the cap asked twice, and the bill reached through
`IBillSettlement` (Accepted on the owner's approval of the Prompt 4.4 plan, 2026-11-21; it resolves open question 1
of the domain model and adds a sentence to BR-501),
0027 reversals of allocations and payments, and a decision that comes late (Accepted on the owner's approval of the
Prompt 4.5 plan, 2026-11-21; its point 9 corrects ADR 0023: a decision that finds no pending approval request
answers 409, not 500, confirmed by the owner on 2026-11-24),
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
in the bill list read per patient and payer (Accepted on the owner's approval of the Prompt 7.3 plan, 2026-11-23),
0041 the cashier screens: allocating oldest first with the caps checked in exact paise, a refund drawn from its source,
the payer statement as a page of its own (Accepted on the owner's approval of the Prompt 7.4 plan, 2026-11-23),
0042 the checker's inbox and the credit note and adjustment screens: the prompt split (reports in 7.5b), the pending
approvals report answering `parentDocumentId` and `makerComment`, decisions made in the inbox and never by the maker, a
credit note's lines from its bill (Accepted on the owner's approval of the Prompt 7.5 plan, 2026-11-23),
0043 the report screens: eight screens for the six reports, the CSV written in the browser from every page and held to
the server's totals (Accepted on the owner's approval of the Prompt 7.5b plan, 2026-11-23),
0044 the dashboard screen: the layout reference from the prototype, one card per colour token, compact money with the
exact amount kept, panels that fail alone, Recharts 3.10.1 loaded with its route (Accepted on the owner's approval of the
Prompt 7.6 plan, 2026-11-23),
0045 the one-command local run: nginx with a local certificate as the API's known proxy, the migrations as in runbook
section 10 by a `migrate` service, a demo hospital bootstrapped by the API and filled through it (Accepted on the owner's
approval of the Prompt 8.1 plan, 2026-11-23),
0046 the load test, the timing of the number series and the browser journey: a Node script with no package, 50 users judged
with a one-second pause and a no-pause stress figure, the 20 ms read as the hold, `Server-Timing` off by default, the
idempotency key's row read before the number, Playwright 1.65.1, neither suite in CI (Accepted on the owner's approval of
the Prompt 8.2 plan and decisions, 2026-11-24),
0047 the brand, the design tokens and the sign-in page: the page laid out in the reference's own pixels, tokens for both
themes with field borders at 3:1, Inter self-hosted through a package, our own icons, the ashline credit, motion by
transform and opacity only, nothing changed in the theme script or nginx's headers (Accepted on the owner's approval of
the Prompt 9.1 plan, 2026-11-24),
0048 the shell, the theme toggle and the shared components: a sidebar with a rail and a drawer, the theme as a pill of three
over a radio group, the heading focused after a navigation, tables that scroll inside their frame, badges with an icon,
skeletons, a specimen page in Development builds (Accepted on the owner's approval of the Prompt 9.2 plan, 2026-11-24),
0049 the money screens: money as one component, one mapping of every status with its icon, the totals card, banners, the
history as a timeline, fields with a unit and a required mark; 9.3 split by the owner into 9.3 and 9.3b, which amends it
(Accepted on the owner's approval of the Prompt 9.3 plan, 2026-11-24; amended by 9.3b the same day: the meter, the
statement's tiles, the inbox as cards),
0050 the dashboard and its tooltips: one tooltip on two new tokens by pointer, keyboard and tap, the donut drawn here,
a table for every chart, no inline style, no layout shift; 9.4 split by the owner into 9.4 and 9.4b, which amends it
(Accepted on the owner's approval of the Prompt 9.4 plan, 2026-11-24),
0051 the users and settings screens and the forced password change: the screens for the TenantAdmin through the existing
API, a change due after creation and after a reset, a short token good for one endpoint while it is due, B24's masked
passwords (Accepted on the owner's approval of the Prompt 10.1 plan, 2026-11-24; it amends ADR 0030 point 3),
0052 onboarding and suspending a hospital by the operator's command: commands of the API's binary on the server, audited
with no user, a one-time password for the first administrator, a suspension that refuses tokens issued before it, the demo
in a `Demo` environment only (Accepted on the owner's approval of the Prompt 10.2 plan, 2026-11-24; it amends ADR 0045),
0057 a sign-out ends the user's sessions on the server: a session number on the user that every token carries, the
user's row read on every request with a token, `POST /api/v1/auth/logout`, and the other tabs told (Accepted on the
owner's approval of the Prompt 10.2c plan, 2026-11-25; it amends ADR 0031 point 8 and ADR 0032 point 8; the numbers 0053
to 0056 stay with Prompts 10.3 to 10.6).
0053 opening balances and the import of a hospital's records: parties first and balances second, opening receivables and
advances as bills and payments of their own series, a seventh ledger account `OpeningBalances`, a batch of three CSV
files posted by a second person's approval, brought in once (Accepted on the owner's approval of the Prompt 10.3 plan,
2026-11-25; it amends ADR 0005, ADR 0008 and ADR 0033),
0054 printed documents: the letterhead as columns of the tenant, the SAC code on a service item and kept on its bill
line, a recorded print as the way a duplicate is known, the tax document and its split derived at print, printing left
to the browser (Accepted on the owner's approval of the Prompt 10.4 plan, 2026-11-25),
0055 production: one Linux server with Docker Compose, secrets as files of the host, `READ_COMMITTED_SNAPSHOT` checked at
startup, SQL Server 2022 Standard with TDE, Let's Encrypt, backups with an off-site copy and a monthly restore drill, and
a release pipeline from a tag (Accepted on the owner's answers and approval of the Prompt 10.5 plan, 2026-11-25),
0058 Phase 10 is built against a reference hospital: `docs/reference-hospital.md` stands in for the client, with a
generated export, both tax documents and hosting left to the owner (Accepted on the owner's request and approval of
2026-11-25; it amends the six inputs of Phase 10).
0059 the logo and the signature of a letterhead: images in the database as insert-only rows named on the tenant by their
SHA-256, PNG, JPEG or WebP told by their bytes and SVG refused, uploaded as the file itself under the settings' version,
removed by a POST action, audited by the hash, shown as `data:` URLs with no change of nginx's policy, and a duplicate
that carries the signature (Accepted on the owner's approval of the Prompt 10.4c plan, 2026-11-26; it amends ADR 0054;
0056 stays with Prompt 10.6).
ADR 0005 is Accepted since its implementation (Prompt 3.3, 2026-11-20); its built note records what the build
decided about the ledger (the port and its template, the BR-405 check, the lock, who opens a payer's balances).
ADR 0006 is Accepted since its implementation (Prompt 3.2, 2026-11-20); its built note records where the build
differs from the first text (the store owns the business transaction, a refusal is stored after a rollback, the
fingerprint includes the query and `If-Match`) and one change the owner accepted on 2026-11-20 (the tenant guard treats
a temporary key value as not set).
ADRs 0003, 0007, 0009 and 0010 were accepted as built on 2026-11-24, with the release of R1 (ADR 0003 carries an amendment of 2026-10-17 with the precise module rule, and one of
2026-11-20, accepted, for the foreign keys of `Bills`). ADR 0008 is Accepted since its implementation (Prompt 3.1,
2026-11-20); its built note records what the build added (who opens the transaction, the sixteen-character limit as
a hard stop, the ban on raw SQL) and one finding the owner accepted the same day (the first number of a new series
can wait for a neighbouring series).
ADR 0004 is Accepted since its implementation (Prompt 2.1, 2026-10-17). ADRs 0011–0017 are Accepted (0011–0013 approved with the infrastructure step, 0014 and 0015 on the owner's
decisions of 2026-10-03, 0016 and 0017 with the approvals of the Prompt 1.4 and 1.5 plans on 2026-10-17;
ADR 0015 marks
the points it added beyond those decisions, accepted as built on 2026-11-24). Since that day every ADR is Accepted but
0001, which ADR 0014 superseded.

---

## 8. Commands

Run from the repository root, on a machine with a .NET 10 SDK (both machines; Docker only on the personal one, section 2).

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
The whole application with the demo: `docker compose up --build`, then https://localhost (runbook section 13). The load
test against it: `$env:SERVER_TIMING = 'true'; docker compose up -d --build`, then
`docker compose --profile load run --rm load` (runbook section 14; at least 2 GB free).

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
npm run budgets            # after a build: the first visit's and each route's compressed size against their budgets
npm run e2e                # Playwright: the browser journey against a running stack (E2E_PASSWORD; runbook section 14)
```

When an endpoint changes, `OpenApiSnapshotTests` fails until the committed document is rewritten
(`BILLINGRECON_WRITE_OPENAPI_SNAPSHOT=1` with that test) and the types are generated again (runbook section 12).

---

## 9. Open items

Kept here so they survive between sessions. Remove an item when it is done.

Since the release of R1 (Prompt 8.3, 2026-11-24) the open work lives in two documents, and this section points at them:

1. **What a machine with Docker still has to run:** `docs/runbook.md` section 8, its lettered steps and one row per
   prompt still owed. The first run of `deploy/verify/personal-machine.ps1` (2026-11-24, at `4c2e75f`, log sent back)
   proved the build, the format gate and every backend test on SQL Server 2022 but three of `PiiMaskerTests`, the
   concurrency classes three times, the containers' health, the headers, the backup and its restore, and 283 of 288
   browser tests through nginx; the rows of Prompts 2.2 to 6.3 and 8.1 are gone. Its red steps were five defects, each
   fixed in its own commit on `phase-10-golive` (`a6cf3c3` to `17af1e5`), and that machine's Node 22.12 (22.22 or later
   is needed). Owed: a second run with Node 24, which must show the journey and the full-size load test (row 8.2), the
   whole routes suite, the component tests and the recovery model; what only the owner's eyes can check; and the first
   green run of the CI workflow on GitHub (row 1.5). **2026-11-26 (Prompt 10.0-close): that second run was not made.**
   The owner tested by hand on the personal machine and sent no output; section 8 has a table of that day with what the
   owner saw and, for every other row, why it is open (the 10,000-bill load, the journey and the browser suites through
   nginx, `PiiMaskerTests` after its fix, the recovery model: no second run; the `Database` tests of 10.1 to 10.4 on
   SQL Server 2022: the first CI run). A row is never closed on a report alone where a machine can show it.
2. **Everything else still open:** `docs/backlog.md`, 34 rows (B1 to B37 but B18, B21 and B24): owner questions, recorded deviations,
   accepted limitations, follow-ups and small fixes, each with its origin and when it is due. Three are marked
   *R1 review*, for the owner: B3 (three credit-note choices), B19 (adjustments by kind), B33 (the stale `0002_Payers`
   in two ADRs). B18 and B24 were closed by Prompt 10.1.
3. **The release:** R1 is tagged `v1.0.0`, an annotated tag on the 8.3 commit of `phase-8-delivery`, local and not
   pushed. Merging to `main` and pushing are the owner's steps; `main` (`03c42fb`) is an ancestor of
   `phase-8-delivery`, so `git merge --ff-only phase-8-delivery` keeps the tag on `main`. The GitHub remote `origin`
   holds `main` and `phase-2-billing` so far. Since 2026-11-26 the commands of the merge are with the owner: every
   phase branch is an ancestor of `phase-10-golive`, so `main` fast-forwards to it; `v1.1.0` did not exist and is to be
   an annotated tag on `65e33ec` (the tip of `phase-9-interface`). Neither tag starts `release.yml`, which is not in
   their commits. The decisions under each built prompt are in `docs/build-history.md` since that day.
4. **History:** the items 1 to 45 of this section (Prompts 1.4 to 8.2: what each built, decided and verified) are in
   CLAUDE.md at commit `4741c7f` (`git show 4741c7f:CLAUDE.md`); the ADRs and `docs/build-prompts.md` cite them by number.
   Every ADR is Accepted since 2026-11-24 (section 7). `docs/traceability.md` proves every user story (Prompt 8.3).
5. **Next: Phase 9, the interface** (`docs/build-prompts.md`, written 2026-11-24 at the owner's request, not started):
   every screen at the level of the owner's sign-in reference, with no change of behaviour, in four prompts: 9.1 the
   brand, the tokens and the sign-in page; 9.2 the shell and the shared components; 9.3 the money screens; 9.4 the
   dashboard, the reports and the finishing pass. "The bar of Phase 9" there holds every prompt. ADR numbers 0047 to
   0050 are theirs (9.3b amends 0049, 9.4b amends 0050). **9.1 is written (2026-11-24, ADR 0047), on branch `phase-9-interface`:** verified in the installed
   Edge against the built app (within 4 px of the reference in both themes, nginx's policy applied in the test with
   nothing refused, the journey of 8.2 passing), 385 component tests. **Owed:** the photograph,
   `frontend/src/assets/brand/signin-hospital.webp`, which the owner does not have yet (the page takes it when the file
   is added, with no change of code); the page through nginx (runbook section 8, row 9.1). **9.2 is written too (ADR 0048):**
   392 component tests and 16 browser tests in Edge against the built app; some components are left for 9.3 (ADR 0048,
   built note); owed: the shell through nginx (row 9.2). **9.3 is written (ADR 0049), split by the owner:** the shared
   components of the money screens with bills, credit notes and adjustments; 422 component tests and 31 browser tests in Edge,
   the 8.2 journey through the new screens; owed: through nginx (row 9.3). **9.3b is written too (ADR 0049, amended):** the meter, the
   statement's tiles, the inbox as cards; 53 browser tests in Edge and the 8.2 journey; owed: through nginx (row 9.3b) and
   backlog B35 (a LocalDB fault to look for on SQL Server 2022). **9.4 is written (ADR 0050), split by the owner:** the
   dashboard with its tooltips; 60 browser tests in Edge and the 8.2 journey; owed: through nginx (row 9.4). **9.4b is written (ADR 0050, amended):** routes on demand
   within budgets (B21 closed), the reports, print, every route with axe; owed: through nginx (row 9.4b). Phase 9 is
   written; `v1.1.0` is proposed, the owner's to tag after the merge.
6. **After Phase 9: Phase 10, go-live** (`docs/build-prompts.md`, written 2026-11-24 from the owner's draft):
   10.0 the verification debt and the merge to `main`; 10.1 the users and settings screens with a forced password change;
   10.2 onboarding a hospital by an operator's command; 10.3 the hospital's records and opening balances; 10.4 printed
   documents; 10.5 production; 10.6 operations; 10.7 readiness. Six inputs from the client came first; since 2026-11-25 there is no client and `docs/reference-hospital.md`, a fictional self-pay hospital with a generated export, gives them, hosting excepted (ADR 0058; the list is at the
   top of Phase 10). ADR numbers 0051 to 0056 are theirs. **10.0 started** on branch
   `phase-10-golive`: `deploy/verify/personal-machine.ps1` (commit `0cc7c1e`) ran once on the personal machine
   (item 1); its defects are fixed, and it waits for the second run, then the merge to `main` (every phase branch is an
   ancestor of the next, so each merge is a fast-forward) and CI. **10.1 is written (ADR 0051, amending ADR 0030 point 3):** the users and settings screens under
   `/administration` and a forced password change after creation and after a reset (`Users.PasswordChangeDue`, migration
   `0021_PasswordChange`; while due, the token carries `pwd_change`, lives 15 minutes and opens only
   `POST /api/v1/auth/password`, every other endpoint answering 403 `password_change_required`); owed: the `Database`
   tests on SQL Server 2022 and the screens through nginx (runbook section 8, row 10.1). **10.2 is written (ADR 0052,
   amending ADR 0045):** the operator onboards a hospital with `tenant create` (a command of the API's own binary,
   `docker compose run --rm api tenant ...`, runbook section 17), its first TenantAdmin with a one-time password printed
   once; `tenant suspend` and `tenant resume`, a suspended hospital refused at sign-in and, within 30 seconds, with a
   token issued before (403 `tenant_suspended`); the demo only in the `Demo` environment (Compose's default,
   `API_ENVIRONMENT`) and Development, the commands refused in `Demo`; owed: runbook section 8, row 10.2. **10.2b is written (2026-11-25, amending ADR 0018, ADR 0039 and
   ADR 0040), at the owner's request:** every search box answers while the user types and no screen has a Search or Find
   button; the record picker is an autocomplete; the MRN of a patient and the e-mail of a user are matched in part; no
   migration, no package; text search on the document lists is left for a later prompt; owed: runbook section 8, row 10.2b.
   **10.2c is written (2026-11-25, ADR 0057), at the owner's request:** a sign-out holds on the server (`Users.SessionVersion`,
   migration `0022_UserSessions`, the claim `ses`, 401 `session_ended`), and so do a changed or reset password, a disabling
   and changed roles; a test that goes on as a user after one of those signs that user in again, and the permission walk
   keeps the sign-out as its last row; owed: runbook section 8, row 10.2c (SQL Server 2022, the load test with the read per
   request, nginx).
   **10.3 is written (2026-11-25, ADR 0053, for the reference hospital of ADR 0058):** patients and guarantors are imported
   by `POST /api/v1/imports/parties` (matched by MRN, never merged, a dry run), and opening balances by an opening batch
   (`/api/v1/opening-batches`: dry run, submit, and the approval of a second person, which posts everything in one
   transaction). An opening receivable is a bill and an opening advance a payment with `Origin = Opening` and
   `LegacyNumber`, numbered from their own series `OBR` and `OBA` (fixed, not settings) in the fiscal year of the cut-over,
   posted against the seventh ledger account `OpeningBalances`; an opening advance is never reversed; reconciliation
   identity 12; migration `0023_OpeningBalances`; the procedure is runbook section 18. A new ledger account must be
   named in `CK_LedgerEntries_Subject`, and a new document type in the allocator's statement: both list by name. Owed: runbook section 8, row 10.3.
   **10.3b is written (2026-11-25, ADR 0053 amended):** the import screens, `frontend/src/imports`, behind a third tab of
   Users and settings (`/administration/import`, its two imports and a batch's page). A file is checked before Import or
   Submit is offered, stays in the page's memory only, and its findings download as CSV. The API's file limits are what one
   request through nginx's 1 MB carries (600,000 characters of patients, 200,000 of guarantors, 250,000 a batch file); the
   browser weighs a request before sending. `e2e/imports.spec.ts`. Owed: runbook section 8, row 10.3b.
   **10.4 is written (2026-11-25, ADR 0054), split by the owner into 10.4 and 10.4b:** the data a printed document needs.
   The letterhead is ten optional columns of `Tenants` (`GET` for anyone signed in and `PUT` for the TenantAdmin at
   `/api/v1/settings/letterhead`, sharing the settings' version; `LetterheadForm` on the Settings screen); the SAC code is
   on `ServiceItem` and kept on `BillLine` from the catalogue; a print is recorded by `POST .../{id}/prints` on bills,
   payments and refunds into `DocumentPrints` (append-only, no event: the row is its own trail), the first the original
   and later ones duplicates; only what took financial effect prints (422 `document_not_printable`). Migration
   `0024_PrintedDocuments`. The tax split (CGST and SGST, never IGST), the kind of tax document and the amount in words
   are derived at print. `docs/go-live.md` holds the open sign-offs. Owed: runbook section 8, row 10.4.
   **10.4b is written (2026-11-25, ADR 0054 amended):** the printed pages, `frontend/src/printing`: `/bills/:id/print`,
   `/payments/:id/print`, `/refunds/:id/print`, each a `PrintSheet` with the letterhead, linked from its document's page.
   Print records the print and then opens the browser's dialog; the sheet says Original, Duplicate or, if never recorded,
   Unrecorded copy. `tax.ts` titles a bill (Tax Invoice, Bill of Supply, Statement of Opening Balance) and halves the
   stored tax; `words.ts` writes an amount in lakh and crore. `e2e/printed.spec.ts`. Owed: runbook section 8, row 10.4b,
   and the accountant's sign-off (`docs/go-live.md`, G1).
   **10.5 is written (2026-11-25, ADR 0055), on the owner's four answers:** production is one Linux server with Docker
   Compose (`docker-compose.prod.yml`, `deploy/prod`; staging the same file), secrets as files of the host that the API
   reads from `/run/secrets` (`SecretFiles`, a file named `Section__Key`), SQL Server 2022 Standard with TDE, Let's
   Encrypt by certbot, a nightly full and 15-minute log backup with an off-site copy by rclone and a monthly restore drill,
   and `.github/workflows/release.yml` (a tag `v*` builds, pushes and deploys staging; production by hand). Outside
   Development the API refuses to start, and is not ready, on a database with `READ_COMMITTED_SNAPSHOT` off
   (`DatabaseSettingsProbe`, a `SqlConnection` of its own reading the server's catalogue, not a raw-SQL file of ADR 0021).
   Runbook section 19. **Nothing of the deployment has run:** no server, domain or registry exists (runbook section 8,
   row 10.5; `docs/go-live.md`, G4 to G6).
   **10.4c-1 is written (2026-11-26, ADR 0059, amending ADR 0054), the first part of stage 2 of the owner's eight stages:**
   the hospital's logo and its signatory's signature. A TenantAdmin uploads each as the file itself (`PUT
   /api/v1/settings/letterhead/logo` and `/signature`, `If-Match` with the settings' version; `POST .../remove` takes it
   off), a PNG, JPEG or WebP told by its own bytes (`LetterheadImageInspector`), at most 256 KB and 128 KB and 2000 pixels
   a side, an SVG refused. The bytes are insert-only rows of `TenantImages`; the tenant names the one it prints by its
   SHA-256 (`LogoSha256`, `SignatureSha256`), so a replacement is a new row, a removal clears the name, and the audit
   carries the hash and never the image. Migration `0025_LetterheadImages`, with `SignatoryDesignation`. The browser reads
   an image with the token and shows it as a `data:` URL, which nginx's policy already admits (no change of the policy);
   the three sheets carry the logo and the signature block in boxes of a fixed size, Print waits for the images, and a
   duplicate is signed like the original. Two tokens, `paper` and `paper-ink`. A new table that is append-only joins the
   lists of `AppendOnlyGuardTests` and `TenantQueryFilterTests`, and its `DENY` goes at the end of its migration. Owed:
   runbook section 8, row 10.4c, and `docs/go-live.md`, G7 (whether a scanned signature is enough).
   **10.4c-2 is written (2026-11-26, ADR 0059 "Amended 2026-11-26"):** print settings for each hospital and kind of
   document (`PrintSettings`, one row a kind made by its first save, migration `0026_PrintSettings`; `GET
   /api/v1/settings/print` for anyone signed in, `PUT .../print/{documentType}` for the TenantAdmin with `If-Match`, the
   version of none `AAAAAAAAAAA=` for a first save). Paper A4, A5 or `Roll80` (never for a bill), margins 5 to 25 mm, the
   logo's place and size or pre-printed stationery, seven switches, a footer line and terms. `PrintLayout.Default` is the
   sheet as it was. For a bill the SAC column and the tax split cannot be switched off (validator, two CHECK constraints,
   and the sheet itself). In the frontend a sheet is `SheetFrame` with a pure body (`BillBody`, `ReceiptBody`,
   `RefundBody`), drawn alike by the printed page and by the preview of the Print tab (`/administration/print`); the
   margins and the paper reach the page through one stylesheet object (`useLayoutRules` in `printing/settings.ts`), so no
   inline style is written and nginx's policy is unchanged; a preview takes the tokens of paper (`[data-paper]`). The
   cashier's name is its own read, `GET /api/v1/{payments|refunds}/{id}/recorder`, through the Identity contract
   `IUserNames`: a payment and a refund answer the same wherever they are read, which nine tests hold. Owed: runbook
   section 8, row 10.4c (SQL Server 2022, nginx, and paper: A5, the roll, real stationery). Part C was decided on 2026-11-26: no
   server-made "Download PDF" now (ADR 0059, backlog B37).
   **10.4d is written (2026-11-26, ADR 0039 and ADR 0040 "Amended 2026-11-26"), stage 3 of the eight:** every search box
   is `components/SearchInput` (a lens from `Icon` "search", `aria-hidden` and no button; the spinner in its place while
   a search is in flight; "Clear search", which is not a stop of the Tab key). `useLiveSearch` sends on blur what the box
   holds, if it is a search and not the one shown, and has `onClear`. `RecordPicker` marks its first result while typing;
   Tab and Shift+Tab take it as the box is left, a late answer too unless the user typed again; with none the box is
   marked "No record chosen" and the focus still moves on; `chooseOnTab="single"` (the payer of a payment alone) takes a
   record only when one matches. The picker's box keeps its 44 px. No package, no change of the API. Owed: runbook section
   8, row 10.4d.
   **10.8, the independent review (stage 4), is in `docs/review-2026-11.md`** (2026-11-26; a separate agent, reading only;
   14 findings, none Critical, two High). Its fixes go one commit each on the owner's approval. **R1 is fixed:** a credit
   note's cap counts the bill's posted adjustments (`IBillAdjustments`, a contract of Adjustments' Application layer that
   Billing asks; `CreditNoteCaps`; ADR 0025 "Amended 2026-11-26"; BR-210). R2 to R11 of its ten wait for the owner.
