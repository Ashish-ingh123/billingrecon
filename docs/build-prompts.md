# BillingRecon build prompts, from zero to a working R1

Written 2026-11-20 after inspecting this repository. The original blueprint file
(`billing_recon_blueprint.md`) is no longer on the office machine, so the prompts below are
**reconstructed** from what the repository records (CLAUDE.md, `docs/requirements.md`,
`docs/domain-model.md`, `docs/database.md`, ADRs 0000 to 0018, the commit messages). Prompts 0.1 to
2.2 describe what was already built. Prompts from 2.2-close onward are new wording for the work that
is left; their numbering is mine, not the blueprint's.

## Where the project stands

| Step | State |
|---|---|
| Phase 0 (0.1 to 0.4), Phase 1 (1.1 to 1.5) | Done, commit `03c42fb` on `main` |
| 2.1 Money and the shared kernel | Done, commit `ae97209` on `phase-2-billing` |
| 2.2 Patients and payers | Code, tests, docs and ADR 0018 written; migration scaffolded with EF Core 10.0.12 on 2026-10-18 under the name `0002_SyncCurrentModel`; **not committed**; no test result recorded |
| 2.3, 2.4, 3.1, 3.2 and 3.4 (service catalogue, bill drafts, number series, idempotency, event dispatch and audit) | Written on 2026-11-20 on top of the uncommitted 2.2, each pre-checked on .NET 9 with LocalDB only; **not built on .NET 10, not committed**; migrations `0003` to `0007` in the repository since 2026-11-21, scaffolded with EF Core 10 (CLAUDE.md section 9, item 16) |
| 3.3 Receivables ledger | Written on 2026-11-20 after 3.4, with the owner's decisions (ADR 0005 built note, ADR 0021); pre-checked on .NET 9 with LocalDB only; **not built on .NET 10**; migration `0008_Receivables` in the repository since 2026-11-21, completed by hand as ADR 0021 says |
| 3.5 Post a bill | Written on 2026-11-20 with the owner's decisions (ADR 0022); pre-checked on .NET 9 with LocalDB only; **not built on .NET 10**; migration `0009_BillPosting` in the repository since 2026-11-21 |
| 3.6 Maker-checker approvals | Written on 2026-11-20 with the owner's decisions (ADR 0023); pre-checked on .NET 9 with LocalDB only; **not built on .NET 10**; migration `0010_Approvals` in the repository since 2026-11-21. The role of the checker is left to 5.3 |
| 4.1 Record a payment | Written on 2026-11-20 with the owner's decisions (CLAUDE.md section 9, item 20); pre-checked on .NET 9 with LocalDB only; **not built on .NET 10**; migration `0011_Payments` in the repository since 2026-11-21 |
| 4.2 Allocate a payment to bills | Code and tests written on 2026-11-20, pre-checked and documented on 2026-11-21 (ADR 0024, Proposed; CLAUDE.md section 9, item 21); pre-checked on .NET 9 with LocalDB only; **not built on .NET 10**; migration `0012_PaymentAllocations` in the repository since 2026-11-21 |
| 4.3 Credit notes | Written on 2026-11-21 with the owner's decisions (ADR 0025; CLAUDE.md section 9, item 22); pre-checked on .NET 9 with LocalDB only; **not built on .NET 10**; migration `0013_CreditNotes` in the repository since 2026-11-21 |
| 4.4 Adjustments | Written on 2026-11-21 with the owner's decisions (ADR 0026; CLAUDE.md section 9, item 23). **Built and tested on .NET 10** (SDK 10.0.404, installed on the office machine that day) in a throwaway copy, with the database tests on LocalDB 2019; not run on SQL Server 2022; migration `0014_Adjustments` in the repository since 2026-11-21 |
| 4.5 Reversals | Written on 2026-11-21 with the owner's decisions (ADR 0027; CLAUDE.md section 9, item 24). Built and tested on .NET 10 in a throwaway copy, with the database tests on LocalDB 2019; not run on SQL Server 2022; migration `0015_PaymentReversals` in the repository since 2026-11-21. It also corrects what a late decision answers in every approve and reject use case since 3.6 (409, not 500) |
| 4.5b Reverse an application of credit | Written on 2026-11-21 with the owner's decisions (ADR 0028; CLAUDE.md section 9, item 25). Built and tested on .NET 10: build, format gate and the tests without a database in the repository, the database tests in a throwaway copy on LocalDB 2019; not run on SQL Server 2022; migration `0016_CreditApplicationReversals` in the repository, added the ordinary way on top of `0003` to `0015`, which were committed on their own first |
| 4.6 Refunds | Written on 2026-11-21 with the owner's decisions (ADR 0029; CLAUDE.md section 9, item 26). Built and tested on .NET 10 like 4.5b: build, format gate and the tests without a database in the repository, the database tests in a throwaway copy on LocalDB 2019; not run on SQL Server 2022; migration `0017_Refunds` in the repository. With it the end-to-end scenario test of the prompt |
| 5.1 Users and roles | Written on 2026-11-21 with the owner's decisions (ADR 0030; CLAUDE.md section 9, item 27). Built and tested on .NET 10 like 4.6: build, format gate and the tests without a database in the repository, the database tests in a throwaway copy on LocalDB 2019; not run on SQL Server 2022; migration `0018_Users` in the repository. A development database with data must be dropped before `0018` |
| 5.2 Sign-in and JWT | Written on 2026-11-21 with the owner's decisions (ADR 0031; CLAUDE.md section 9, item 28). Built and tested on .NET 10 like 5.1; no migration |
| 5.3 Role permissions | Written on 2026-11-21 with the owner's decisions (ADR 0032; CLAUDE.md section 9, item 29), the first half of the prompt. Built and tested on .NET 10 like 5.1; no migration |
| 5.3b Tenant settings and prefix changes | Written on 2026-11-21 with the owner's decisions (ADR 0033; CLAUDE.md section 9, item 30). Built and tested on .NET 10 like 5.1; migration `0019_PrefixChanges` in the repository. Committed (`31243e3`, `7755271`) |
| 5.3b fix: the prefix approval holds the series | Written on 2026-11-23 with the owner's decision on ADR 0033 built note point 5 (option B; ADR 0033 "Amended 2026-11-23"; CLAUDE.md section 9, item 31). Built and tested on .NET 10 like 5.1; no migration. Committed (`ce64e6c`) |
| 5.4 Database logins and grants | Written on 2026-11-23 with the owner's decisions (ADR 0034; CLAUDE.md section 9, item 32), the first half of the prompt. Built and tested on .NET 10 like 5.1, every database test now as the application's login; migration `0020_DatabaseRoles` in the repository, written by hand. Found: the application relies on `READ_COMMITTED_SNAPSHOT ON` |
| 5.4b HTTP hardening and the security document | Written on 2026-11-23 with the owner's decisions (ADR 0035; CLAUDE.md section 9, item 33), the second half of 5.4. Built and tested on .NET 10 like 5.1; no migration, no package. TLS at a proxy, HTTPS only with HSTS, allowed hosts, two sign-in limits, CORS, the headers of an answer; `docs/security.md` written in full with its threat model. The CI smoke run changed and has not run |
| 6.1 Registers and balances | Written on 2026-11-23 with the owner's decisions (ADR 0036; CLAUDE.md section 9, item 34). Built and tested on .NET 10 like 5.1; no migration, no package. Eight read-only report endpoints, every figure held against ledger sums in the tests; Reporting reads every module through five listed exceptions to the module rule and writes nothing |
| 6.2 Reconciliation and audit trail | Written on 2026-11-23 with the owner's decisions (ADR 0036 amendment; CLAUDE.md section 9, item 35). Built and tested on .NET 10 like 5.1; no migration, no package. The reconciliation report with gapless numbering as identity 11, the reconciliation after every database test, US-43 for a bill, and `docs/api.md` complete with every endpoint and an error-code catalogue that a test keeps complete |
| 6.3 Dashboard figures | Written on 2026-11-23 with the owner's decisions (ADR 0037; US-44 and BR-902 added to `docs/requirements.md`; CLAUDE.md section 9, item 36). Built and tested on .NET 10 like 5.1; no migration, no package. Seven GET panels under `/api/v1/dashboard` for TenantAdmin, Supervisor and Viewer, every figure derived from the ledger by business date through a sixth sanctioned raw-SQL file; no trend for the drift card. The backend of R1 is written to its end |
| 7.1 Scaffold and sign-in | Written on 2026-11-23 with the owner's decisions (ADR 0038; CLAUDE.md section 9, item 37). Built and tested on the office machine with Node 24.22.2: the frontend's format, lint, types, 161 tests and build, and the .NET gates with the new `OpenApiSnapshotTests`; signed in through the real API in the browser. 27 npm packages approved; no NuGet package, no migration. The CI job has not run |
| 7.2 Patients, payers and catalogue screens | Written on 2026-11-23 with the owner's decisions (ADR 0039; CLAUDE.md section 9, item 38). Built and tested like 7.1: 220 frontend tests, the .NET gates; in the browser against the real API, a real conflict included. No package, no migration, no change of the API; `OpenApiSnapshotTests` now writes line breaks as LF |
| 7.3 Bill screens | Written on 2026-11-23 with the owner's decisions (ADR 0040; CLAUDE.md section 9, item 39). Built and tested like 7.1: 262 frontend tests, the .NET gates with a Domain test that holds the server to the browser's line cases; in the browser against the real API, posting and approval included. No package, no migration, no change of the API |
| 7.4 Cashier screens | Written on 2026-11-23 with the owner's decisions (ADR 0041; CLAUDE.md section 9, item 40). Built and tested like 7.1: 296 frontend tests, the .NET gates; in the browser against the real API, recording, allocation, a reversal request, the statement and a refund submitted. No package, no migration, no change of the API |
| 7.5 Approvals, credit notes and adjustments | Written on 2026-11-23 with the owner's decisions (ADR 0042; CLAUDE.md section 9, item 41). The prompt was split with its plan: the reports are 7.5b. Built and tested like 7.1: 330 frontend tests, the .NET gates and the whole `Database` collection on LocalDB (the pending approvals report answers two more fields); in the browser against the real API, the Phase 4 scenario by three users. No package, no migration |
| 7.5b Reports | Written on 2026-11-23 with the owner's decisions (ADR 0043; CLAUDE.md section 9, item 42). Built and tested like 7.1: 345 frontend tests; in the browser against the real API, the registers, the collection, the balances and the reconciliation, and a CSV read in the page. No package, no migration, no change of the API |
| 7.6 Dashboard | Written on 2026-11-23 with the owner's decisions (ADR 0044; CLAUDE.md section 9, item 43): the layout reference from the owner's prototype, Recharts 3.10.1. 357 frontend tests; in the browser, a Supervisor's figures equal to the 6.1 reports in both themes. No migration, no change of the API |
| 8.1 One-command local run | Written on 2026-11-23 with the owner's decisions (ADR 0045; CLAUDE.md section 9, item 44), on `phase-8-delivery`. The SQL steps and the demo seed run here on LocalDB. On 2026-11-24 the owner ran `docker compose up --build` with the demo on the personal machine (signed in, the dashboard shown) and `dotnet test` with Docker; two fixes to the images came back from that run as their own commit (ADR 0045, "Run with Docker"). Still owed: the page's headers and the http redirect (runbook section 8, row 8.1) |
| 8.2 Performance and browser tests | Written on 2026-11-24 with the owner's decisions (ADR 0046; CLAUDE.md section 9, item 45), on `phase-8-delivery`. A load test in Node with no package (`deploy/load`), the number series' wait and hold in `Server-Timing` (off by default), the idempotency key's row read before the number, and a Playwright journey (`@playwright/test` 1.65.1). On the office machine, on LocalDB at 2,000 bills: every target met with a one-second pause, no drift; the full 10,000 and the journey through nginx are owed by the personal machine |
| 8.3 Release R1 | Written on 2026-11-24 with the owner's decisions, in the session of 8.2: the runbook finished (rolling back a migration, backup and restore with `deploy/backup`, run on LocalDB, and the incident procedures); `docs/traceability.md` with every user story and its tests, held by `TraceabilityTests`; every item of CLAUDE.md section 9 closed or re-filed to `docs/backlog.md` or runbook section 8; the README for R1; every ADR Accepted; the tag `v1.0.0`, annotated and local |
| 9.1 Brand, design tokens and the sign-in page | Written on 2026-11-24 with the owner's decisions (ADR 0047), on `phase-9-interface`. The tokens of both themes, Inter self-hosted (`@fontsource-variable/inter` 5.3.0), our own icons, the BillingRecon mark, the ashline credit and the sign-in page laid out in the reference's own pixels, with its motion; the shared buttons and fields restyled. In the installed Edge: within 4 px of `docs/ui/sign-in.png` in both themes, nothing refused by nginx's policy (applied in the test), the journey of 8.2 passing through it. **Built without the photograph**, which the owner does not have yet; not seen through nginx |
| 9.2 The shell and the shared components | Written on 2026-11-24 with the owner's decisions (ADR 0048), on `phase-9-interface`: the sidebar with icons, its rail and its drawer, the top bar, the ashline credit, a three-way theme pill in place of the drop-down, the tab's title and the heading's focus after a navigation, the shared components restyled from the tokens, skeletons and empty states, a specimen page in Development builds. 392 component tests and 16 browser tests in the installed Edge, the 8.2 journey through the new shell. Some components were left for 9.3 (ADR 0048, built note); not seen through nginx |
| 9.3 The money screens: shared components, bills, credit notes, adjustments | Written on 2026-11-24 (ADR 0049), on `phase-9-interface`, after the owner split 9.3 in two: money as one component, one mapping of every status with its icon, the totals card, banners, the history as a timeline, units and a required mark in fields, pending buttons, the refused field focused; bills, credit notes and adjustments, the no-access and not-found pages. 422 component tests and 31 browser tests in the installed Edge, the 8.2 journey through the new screens. Not seen through nginx |
| 9.3b The cashier's screens and the inbox | Written on 2026-11-24 (ADR 0049, amended), on `phase-9-interface`: the meter of what is left to apply, figures cards and banners on the payment's and refund's pages, the statement's four tiles drawn without a shift, the inbox as cards, units and required marks on the cashier's forms. 422 component tests and 53 browser tests in the installed Edge, the 8.2 journey. Not seen through nginx |
| 9.4 (the dashboard, reports and finishing pass) | Written as a prompt on 2026-11-24, not started |

**Verification is deferred (owner, 2026-11-20): the personal-machine run comes later, and `docs/runbook.md` section 8 is the one running list of every command it owes, in order. Meanwhile every prompt from 2.2 on is committed unverified, one commit per prompt. Phase 3 is written to its end (3.1 to 3.6), and 4.1 (record a payment), 4.2 (allocate a payment to bills), 4.3 (credit notes) and 4.4 (adjustments) are written. Since 2026-11-21 the office machine has the .NET 10 SDK: the whole tip of the branch built and passed there on .NET 10 with LocalDB 2019, and the migrations `0003` to `0015` are in the repository; only the run on SQL Server 2022 in Docker is still owed. 4.5 (reversals), 4.5b (reverse an application of credit) and 4.6 (refunds) are written too, and `0016` and `0017` are in the repository. Phase 4 is written to its end; its branch merge to `main` is the owner's step. 5.1 (users and roles) is written too, and `0018` is in the repository. 5.2 (sign-in and JWT) is written too; it adds no migration. 5.3 was split with its plan: its first half, the role permissions, is written too, with no migration. 5.3b (tenant settings and prefix changes) is written too, and `0019` is in the repository. ADR 0033 built note point 5 was decided on 2026-11-23 (the approval holds the series) and is built as its own commit. 5.4 was split with its plan: its first half, the database logins and grants, is written too, and `0020` is in the repository. 5.4b (HTTP hardening and `docs/security.md` in full) is written too, with no migration; Phase 5 is written to its end, and its merge to `main` is the owner's step. 6.1 (registers and balances), 6.2 (reconciliation and audit trail) and 6.3 (dashboard figures) are written too, with no migration, on `phase-2-billing` like the rest: the backend of R1 is written to its end, and its merge to `main` is the owner's step. 7.1 (the frontend's scaffold and sign-in) is written too, on the same branch, with Node 24 on the office machine. 7.2 (patients, payers and catalogue screens), 7.3 (bill screens), 7.4 (cashier screens) and 7.5 (the checker's inbox, credit notes and adjustments; split with its plan), 7.5b (reports) and 7.6 (dashboard) are written too: the frontend of R1 is written to its end. 8.1 (one-command local run) is written too, on `phase-8-delivery`, and so are 8.2 (performance and browser tests) and 8.3 (release R1, tagged `v1.0.0`): R1 is written to its end. On 2026-11-24 the owner reported the first run on the personal machine: `docker compose up --build` with the demo, and `dotnet test` with Docker, which passed; its output is still to be sent, so the rows of runbook section 8 stay until then. No prompt is left in R1. Phase 9, the interface, has begun: 9.1 (the brand, the tokens and the sign-in page) is written, without the photograph, which is still to come, and so are 9.2 (the shell and the shared components) and 9.3 (the money screens' shared components, bills, credit notes and adjustments); and 9.3b (payments, refunds, the statement and the inbox) is written too; next is 9.4 (the dashboard, the reports and the finishing pass); what else follows is in `docs/backlog.md` and the release plan of CLAUDE.md. What was decided for each built prompt is recorded under it below.**

## How to use these prompts

- Open the Claude Code session in `C:\aptran2026\BillingRecon\BillingRecon` so that CLAUDE.md loads.
- Give one prompt per session, in order. Do not start a prompt until the one before it is committed.
- Paste the standing preamble first, then the prompt.
- One commit per prompt, one branch per phase, merged to `main` when the phase is green.

### Standing preamble (paste before every prompt)

```
Read CLAUDE.md fully before anything else, then the documents this prompt names.
Follow the working agreement in CLAUDE.md section 6: show me a short plan (files, steps, tests,
risks, any new package) and wait for my approval before you edit code.
Obey the twelve financial rules of section 4 and the conventions of section 5. Do not weaken or
skip a test. Ask before adding any package.
This machine builds and tests .NET 10 and the frontend but has no Docker and little free disk
(CLAUDE.md section 2): run the database tests in a throwaway copy on LocalDB, tell me plainly what
was and was not verified, and give me the exact commands to run on my personal machine.
When the work is done: update CLAUDE.md (sections 3, 7, 9), the affected docs and ADRs, delete the
README placeholder of any module folder that received code, and propose one commit message.
```

---

## Part A. Already built (for reference; do not run again)

**0.1 Project memory.** Create CLAUDE.md for a multi-tenant hospital billing, payments and
accounts-receivable platform in India: purpose, release plan R1 to R5, tech stack (.NET, ASP.NET Core
controllers, EF Core, SQL Server, xUnit, Shouldly, NSubstitute, Testcontainers, NetArchTest),
modular-monolith layout with nine modules, the layer and module dependency rules, the twelve
financial correctness rules, coding conventions, working agreement, and ADR 0000 with the ADR format.

**0.2 Requirements.** Write `docs/requirements.md`: vocabulary (bill, payer, payment, allocation),
roles and permission matrix, R1 scope and exclusions, the numbered BR catalogue (BR-001 to BR-901),
user stories US-01 to US-43 with acceptance criteria, non-functional requirements, R2 to R5 outlines.

**0.3 Domain model.** Write `docs/domain-model.md`: ubiquitous language, module map, value objects,
every aggregate with its invariants citing BR ids, the state machines, transaction boundaries and the
allocation exception, domain events, ports, open questions.

**0.4 Database design and ADRs.** Write `docs/database.md` (conventions, ER diagram, every R1 table,
the number-series lock, ledger accounts and posting templates, balance identities and reconciliation
SQL, idempotency store, permissions, migrations) and ADRs 0001 to 0010.

**1.1 Solution scaffold.** `BillingRecon.sln` with Domain, Application, Infrastructure, Api and four
test projects; `Directory.Build.props` (nullable, warnings as errors, analyzers), central package
management, `.editorconfig`, `global.json`, module folders with README placeholders, architecture
tests for the layer rule. ADR 0002, later ADR 0014 (.NET 10).

**1.2 Cross-cutting infrastructure.** Validated options, Serilog with correlation id and PII masking,
`Result`/`Error` and the RFC 9457 ProblemDetails contract, global exception handler, health checks,
`/api/v1` route prefix, OpenAPI and Scalar in Development, Docker Compose for SQL Server 2022 and Seq.
ADRs 0011 to 0013.

**1.3 Persistence foundation.** `BillingReconDbContext` with conventions (typed ids, tenant query
filter and write guard, audit stamps, rowversion, explicit decimal and string sizes), the banned-API
list, `IClock`, the `Tenant` aggregate and migration `0001_Tenants`. ADR 0015.

**1.4 Test infrastructure.** Testcontainers SQL Server 2022 with Respawn, the `Database` collection,
`DatabaseApiFactory` acting for a tenant, Bogus `TestData`, the database-backed tests the persistence
step promised, the module dependency rule with its listed exceptions. ADR 0016.

**1.5 CI and image.** GitHub Actions workflow (restore with audit, format gate, build, all tests,
coverage, vulnerability report, migration script, image build and smoke run), the API Dockerfile,
Dependabot, LF line endings. ADR 0017.

**2.1 Money and the shared kernel.** `Money`, `UnitPrice`, `Quantity`, `Percent` as readonly record
structs with `Of`/`TryOf`, `Money.Round` (away from zero, 2 places), EF converters, `Tenant.
PostingApprovalThreshold` as `Money?`, tests. ADR 0004 accepted.

**2.2 Patients and payers.** `Patient` (MRN unique per tenant) and `Payer` (self-pay only, at most one
per patient, guarantor allowed) with deactivate and reactivate; the use-case contracts
`ICommandHandler`/`IQueryHandler`, FluentValidation, `IUnitOfWork`, paging, ETag and `If-Match`;
controllers, repositories, read services; unit, API and SQL Server tests; `docs/api.md`. ADR 0018.

---

## Part B. Remaining prompts

### Phase 2. Reference data and bill drafts (branch `phase-2-billing`)

#### 2.2-close. Verify and commit Prompt 2.2

```
Prompt 2.2 (patients and payers) is written but uncommitted, and CLAUDE.md section 9 item 9 is out
of date: the migration now exists as 20261018182819_0002_SyncCurrentModel (EF Core 10.0.12).
1. Compare that migration and the model snapshot with docs/database.md section 3.2 and with the
   entity configurations. Report any difference. Do not rename or edit the migration; record its
   real name in docs/database.md section 8.
2. Give me the exact commands for my personal machine (dotnet tool restore, dotnet build,
   dotnet format --verify-no-changes, dotnet test with Docker running) and wait for my output.
3. From my output: fix real defects, then update CLAUDE.md section 9 (items 1, 8, 9), section 2
   (the SDK list I send) and README.
4. docs/build-prompts.md renumbers the remaining phases (ledger and audit are now Phase 3, identity
   stays Phase 5). Correct the "Phase 4" labels in CLAUDE.md section 9 to match.
5. Propose the commit "Prompt 2.2: patients and payers (ADR 0018)". I will push main and the branch
   and watch the first CI run; then we close section 9 item 7 together.
Done when: build and every test are green on .NET 10 with Docker, and the work is committed.
```

#### 2.3 Service catalogue

```
Read docs/domain-model.md section 4.3 and docs/database.md section 3.3 (ServiceItems).
Build the ServiceItem reference data in the Billing module, shaped exactly like the Payers slice
(ADR 0018): aggregate with code unique per tenant, name, default UnitPrice, default tax rate
(Percent), Active/Inactive status; create, update, get, list (paged, filter by status and search
text), deactivate, reactivate; ETag and If-Match; no DELETE.
Tests: domain, validators, handlers, API tests in the Database collection, tenant isolation.
Migration 0003_ServiceItems. Add the endpoints to docs/api.md.
Done when: a clerk can maintain the catalogue through the API and another tenant gets 404.
```

#### 2.4 Bill drafts

```
Read docs/requirements.md BR-201 to BR-205 and US-20, US-23; docs/domain-model.md sections 4.4,
5.1 and 6; docs/database.md section 3.3 (Bills, BillLines).
Build the Bill aggregate with BillLine children, Draft and Voided states only:
- line arithmetic per BR-202 through Money.Round, totals per BR-203 as sums of rounded lines;
- a line may name a ServiceItem (defaults for price and tax come through a Billing contract) or be
  free text;
- bill for an active patient, owed by an active payer (BR-102, BR-105, BR-106), checked through
  the Payers module's public contracts, never its entities;
- dates per BR-205 using IClock.TodayIst and the tenant settings;
- create draft, replace lines, update header, void with reason, get, list (paged, filters);
- the unit of work marks the root modified when only lines change, so the row version is checked
  and advanced (CLAUDE.md section 9 item 5).
Tests: property-style arithmetic tests with awkward values (0.005, 1/3 quantities, 100% discount),
state machine, API and SQL Server tests, CHECK constraints. Migration 0004_Bills.
Done when: drafts can be created, edited and voided, have no number and no ledger effect.
```

Merge `phase-2-billing` to `main` when 2.4 is green.

### Phase 3. The posting engine (branch `phase-3-posting`)

#### 3.1 Fiscal year and gapless number series

```
Read ADR 0008, BR-004, BR-801, BR-802 and docs/database.md section 4.
Build FiscalYear and DocumentNumber value objects (Common), the NumberSeries table and the
INumberSeries port. The implementation takes the next number inside the caller's transaction with
UPDLOCK, HOLDLOCK on the (TenantId, DocumentType, FiscalYear) row, creating the row on first use
with the tenant's default prefix. Format PREFIX/YY-YY/NNNNNN, at most 16 characters. Raw SQL is
needed here: sanction it in BannedSymbolsTests style, one file only.
Tests on real SQL Server: 31 March and 1 April boundaries, 50 parallel takers produce 1..50 with
no gap and no duplicate, a rolled-back transaction leaves no gap, the gapless verification query
of section 4.3. Migration 0005_NumberSeries. No document uses it yet.
Done when: the concurrency test passes repeatedly.
```

#### 3.2 Idempotency for financial POSTs

```
Read ADR 0006, BR-803 and docs/database.md section 6.
Build IIdempotencyStore and the API filter/attribute for financial POSTs: the key is a UUID
scoped to the tenant, reserved before the business transaction, completed inside it with the
request hash and the stored response; replay returns the original response unchanged; same key
with another payload is 422 idempotency_key_reuse; in flight is 409 idempotency_in_progress;
missing or malformed is 400 idempotency_key_missing. Retention 30 days (state the clean-up, do not
schedule it yet). Prove it on a test-only probe endpoint in the integration tests, including two
simultaneous requests with one key. Migration 0006_IdempotencyKeys.
Done when: the probe endpoint shows all four outcomes against SQL Server.
```

#### 3.3 Receivables ledger

```
Read ADR 0005, BR-401 to BR-406 and docs/database.md sections 3.7 and 5 (all of it).
Build the Receivables module: LedgerPosting and LedgerEntry (append-only, balanced, six accounts
with subject and payer rules of BR-403), the PayerBalances cache, and the IReceivablesLedger port
that business modules call inside their own unit of work. Mark the ledger entities IAppendOnly: the
EF-layer guard that refuses updates and deletes of append-only tables exists since Prompt 3.4
(ADR 0020).
Build the reconciliation query of section 5.6 as a query service that returns drift rows.
Create the zero PayerBalances row when a payer is created (through the Receivables port) and a
migration step for payers that already exist.
Tests: unbalanced posting refused, negative subject balance refused, update/delete of a ledger row
refused, reconciliation returns nothing when clean and reports a seeded drift.
Migration 0008_Receivables.
Done when: a posting written through the port reconciles to zero drift.
```

**Owner's decisions for 3.3, recorded 2026-11-20 (give 3.3 alone, after 2.2 to 3.4 are committed; verification is deferred, see `docs/runbook.md` section 8):**

**Settled by the owner on 2026-11-20 and built:** the port (not a subscriber) opens the `PayerBalances` row; the
foreign keys to `Payers` come from a typed-id convention of the context, with no new module exception; the ledger
and the reconciliation are two more sanctioned raw-SQL files; `PayerBalances` has no `LastPostingId`;
`PostedByUserId` is nullable with no key until `Users` exists. ADR 0021 and the built note of ADR 0005 record them.
The list below is kept as the record of what was asked.

- The plan shown for 3.3 must include, besides what the prompt says:
  1. **Renumber every later migration**, here and in `docs/database.md` section 8: `0008_Receivables`,
     `0009_BillPosting` (3.5, which needs columns on `Bills`), `0010_Approvals`, `0011_Payments`, `0012_CreditNotes`,
     `0013_Adjustments`, `0014_Refunds`, `0015_Users`.
  2. **How the zero `PayerBalances` row is created.** The owner prefers a Receivables subscriber to the
     payer-created event over Payers calling a Receivables port, unless the plan shows why the port is better. The
     case for the port, to be put to the owner in that plan: a subscriber in Receivables has to name
     `Domain.Payers.PayerCreated`, which is a new module-dependency exception, and it contradicts
     `docs/domain-model.md` section 8 and ADR 0020 point 7 ("Audit is the only subscriber", with its architecture
     test); the port needs no exception, because Payers may call a public Application contract of Receivables.
     The owner decides.
  3. **No new module-dependency exception without the owner's approval.** The three Receivables tables have foreign
     keys to `Payers`; declaring them the way `BillConfiguration` does needs an exception
     (`Infrastructure.Receivables` to `Domain.Payers`). The plan must either ask for it or propose the typed-id
     foreign key convention that ADR 0003 (amendment of 2026-11-20) said to weigh again at this point.
  4. **The data step of `0008`** is `INSERT ... SELECT ... FROM Payers WHERE NOT EXISTS (...)`, the tenant taken
     from `Payers`. It is added by hand to the scaffolded migration; that exception to "never edit a migration by
     hand" is recorded in an ADR, and a test fails with the line to paste when it is missing.
- Also to be asked in that plan, because both go against a rule of CLAUDE.md section 2: the atomic change of
  `PayerBalances` and the reconciliation query of `docs/database.md` section 5.6 cannot be written without raw
  SQL (Entity Framework has no lock hints and no "add to the column" without the banned `ExecuteUpdate`, and the
  reconciliation joins tables of several modules). Each would be one more sanctioned raw-SQL file.
- A first draft of 3.3 and 3.5 was written on 2026-11-20 before these decisions and taken out of the repository
  again. It is parked in `C:\aptran2026\BillingRecon\parked-3.3-3.5-draft` (outside the repository) as reference
  only: it uses the port, an unasked exception, and raw SQL without the explicit lock read, so it does not match
  the decisions above.

#### 3.4 Domain event dispatch and the audit trail

```
Read BR-701 to BR-703, docs/domain-model.md sections 4.13 and 8, docs/database.md section 3.8.
Make the unit of work dispatch the aggregates' domain events in-process before commit, stamping
tenant and audit columns first and reading the clock once per use case (CLAUDE.md section 9 item 5).
Build the Audit module: AuditRecord (append-only), IAuditWriter, a generic subscriber over
IDomainEvent that writes who, when, tenant, entity, action, before/after JSON and correlation id in
the same transaction. Wire the events Patient, Payer, ServiceItem and Bill already raise.
Tests: every existing state change writes exactly one audit row; a rolled-back use case writes
none; audit rows cannot be updated or deleted. Migration 0007_Audit.
Done when: the audit trail of a patient shows register, update, deactivate with correlation ids.
```

#### 3.5 Post a bill

```
Read BR-206, BR-208, BR-006, BR-007, US-21, US-25, docs/domain-model.md sections 5.1 and 7,
docs/database.md section 5.3.
Build POST /api/v1/bills/{id}/post (Idempotency-Key and If-Match required): validate, take the
gapless number for the fiscal year of BillDate, post Dr Receivable(bill) / Cr Revenue for Total,
set BalanceDue = Total, status Posted (Settled when Total is zero), freeze the bill, write the
audit record: all in one IUnitOfWork commit. A posted bill refuses edit and void.
Tests: the happy path asserts number, ledger, caches, audit and zero reconciliation drift; a
failure injected at each step leaves no number consumed, no ledger and no audit rows (US-25); two
simultaneous posts of the same bill give one success and one 409; replay returns the same response.
Migration 0009_BillPosting.
Done when: every scenario ends with zero drift rows and a gapless series.
```

**Owner's decisions for 3.5, recorded 2026-11-20 (give 3.5 in a later session, after 3.3 is committed):**

**Settled by the owner on 2026-11-20 and built (ADR 0022):** BR-401 and identity 2 are amended for a bill that comes
to nothing; a second posting gets 409 `concurrency_conflict` for a stale version and 409 `bill_already_posted` for
the current one, and a posted bill answers `bill_already_posted` to an edit or a void as well. The list below is kept
as the record of what was asked.

- **Accepted:** a draft without lines is refused with 422; the payer must be active at posting (BR-105); the
  patient is not re-checked (BR-102 only stops new bills).
- **Zero-total bill:** numbered, audited, status `Settled`, no ledger posting. **Open, to settle with the owner
  before coding:** read literally, BR-401 says otherwise ("a posted document without ledger entries ... is a
  defect"), and so does identity 2 of `docs/database.md` section 5.5, while `CK_LedgerEntries_OneSide` forbids an
  entry of zero. Recommended: amend BR-401 and identity 2 to except a document whose total is zero, and have the
  reconciliation skip such bills; the alternative is to allow zero entries, which weakens the constraint.
- **Lock order, to be written as an ADR:** the number series first; then an explicit `UPDLOCK` read of the
  `PayerBalances` row; both before `SaveChanges`. The ADR states where the bill row falls: it is read without a
  lock at the start and written by the save, after both locks, under its row version.
- **Three separate tests, each with its own error code:**
  - the same key while the first request is in flight: 409 `idempotency_in_progress`;
  - two different keys at once: one succeeds, the other gets 409 `concurrency_conflict` from the row version check;
  - a different key after the commit. Proposed: with the old version in `If-Match`, 409 `concurrency_conflict`
    (the API answers a stale version with 409 everywhere, CLAUDE.md section 4 rule 6, and has no 412); with the
    current version, 409 `bill_already_posted`, a new code for a bill that is Posted, PartiallyPaid or Settled,
    instead of the general 422 `bill_not_draft`, which stays for a voided bill. The owner confirms.
- **More tests:** a posted bill refuses edit and void; a tenant whose threshold is null needs no approval.
- **BR-207:** until 3.6, a bill at or above the threshold is refused with 422 `bill_requires_approval`. That is
  temporary and has an item in CLAUDE.md section 9 for 3.6 to remove.
- **Migration:** `0009_BillPosting` (number, fiscal year, sequence number, posted stamps, the later statuses,
  `CK_Bills_NumberIffPosted`, `CK_Bills_StatusMatchesBalance`, the two filtered unique indexes).

#### 3.6 Maker-checker approvals

```
Read BR-603 to BR-606, BR-207, US-22, docs/domain-model.md sections 4.9 and 5.4,
docs/database.md section 3.6.
Build ApprovalRequest (Common): submit, approve, reject with comment, list pending; maker not
equal to checker enforced in the domain and by a CHECK constraint; at most one pending request per
document. Approval handlers live with their documents (ADR 0003 amendment). First use: a bill
with Total at or above Tenant.PostingApprovalThreshold goes to PendingApproval, is locked, gets no
number, and the checker's approval is the posting (same transaction as 3.5); rejection returns it
to Draft. Until Identity exists, tests act as two users with the existing test identity helpers.
Migration 0010_Approvals.
Done when: US-22 passes, including approval by the maker being refused.
```

Merge `phase-3-posting` to `main`.

### Phase 4. Payments and corrections (branch `phase-4-payments`)

#### 4.1 Record a payment

```
Read BR-301, BR-302, BR-309, US-30, docs/domain-model.md section 4.6, docs/database.md section 3.4.
Build Payment in the Payments module: amount > 0, method (Cash, Card, UPI, BankTransfer, Cheque),
reference required except for Cash, date per BR-205, active payer only. Recording is posting:
gapless payment number, Dr Cash / Cr UnappliedCash(payment), UnappliedAmount = Amount, audit, one
transaction, Idempotency-Key required. Get and list. Migration 0011_Payments.
Done when: US-30 passes with zero drift.
```

**Owner's decisions for 4.1, recorded 2026-11-20:** the ledger port answers the id of the posting and the payment
stores it; the `Payments` table holds only what recording needs (reversal columns and states with 4.5); an amount
is at most 9,999,999,999.99. Built on `phase-2-billing`, not on a new branch, like everything since 2.2.

#### 4.2 Allocate a payment to bills

```
Read BR-303, BR-304, BR-208, US-31 and docs/domain-model.md section 6 (the allocation exception),
docs/database.md section 5.4.
Build allocation: one request may allocate to several posted bills of the same payer; caps per
BR-303; paired entry Dr UnappliedCash(payment) / Cr Receivable(bill); Payment.UnappliedAmount,
Bill.BalanceDue, bill status and PayerBalances change in the same transaction. Payments reaches
bills only through Billing's public contracts. Lock order is fixed and documented to avoid
deadlocks.
Tests: over-allocation refused, cross-payer refused, two cashiers allocating the last rupee of one
bill at once (one wins, one gets 409), partial then full settlement moves the status
Posted -> PartiallyPaid -> Settled, zero drift after each.
Done when: US-31 passes under the concurrency test.
```

**How 4.2 was built, recorded 2026-11-21 from the code (ADR 0024, which the owner still confirms):** Payments
reaches a bill through `IBillSettlement`, a contract of Billing, with no new module-dependency exception; the lock
order is that of ADR 0022 without a number (checks first, bills in the order of their ids, then the payer's row of
`PayerBalances` under the ledger's lock, then the save under the row versions of the payment and of every bill); the
loser of the last rupee gets 409 `concurrency_conflict`, whether its save or the ledger notices; the ledger now
refuses a balance below zero instead of failing a CHECK constraint; one request names at most 50 bills, each once;
a deactivated payer's payment can still be allocated. The table is its own migration, **`0012_PaymentAllocations`**,
so the later migrations move up by one: `0013_CreditNotes`, `0014_Adjustments`, `0015_Refunds`, `0016_Users`. Built
on `phase-2-billing`, like everything since 2.2.

#### 4.3 Credit notes

```
Read BR-210 to BR-214, US-24, docs/domain-model.md section 4.5, docs/database.md section 3.3.
Build CreditNote with lines and allocations: against one posted bill, caps per BR-210 and BR-211,
draft then submit (maker-checker, BR-604); approval takes the gapless number, posts
Dr Revenue / Cr CreditAvailable, then applies min(Total, Bill.BalanceDue) to the bill; the rest
stays as AvailableAmount and can be applied to another posted bill of the same payer.
Migration 0013_CreditNotes.
Done when: US-24 passes with zero drift, including a credit note larger than the balance due.
```

**Owner's decisions for 4.3, recorded 2026-11-21 (the plan was approved "as recommended"; ADR 0025):** the caps of
BR-210 and BR-211 are checked before any lock (422) and again under the payer's ledger lock (409
`concurrency_conflict`), with no new column on `Bills`; a credit note that comes to nothing is refused at `post`
(422); the payer need not be active, and the date is checked again at approval; `CreditNoteAllocations` has no
reversal columns and the status `Active` alone; a line may name a line of the bill and takes its defaults from it;
applying available credit to another bill needs no second person. Built in the same session as the documents of 4.2,
at the owner's word, after 4.2 was committed. **Still open, asked in the plan and not answered:** no remaining
prompt reverses an application of credit (4.5 names payment allocations and payments only); add it to 4.5 or leave it
out of R1.

#### 4.4 Adjustments

```
Read BR-501 to BR-503, docs/domain-model.md section 4.8, docs/database.md section 3.5.
Build Adjustment (Discount, WriteOff, Rounding under Rs 1.00): against one posted bill, amount
capped by BalanceDue at posting, reason required, maker-checker, gapless number,
Dr Adjustments / Cr Receivable(bill). Raise domain-model open question 1 with me before coding.
Migration 0014_Adjustments.
Done when: an approved write-off settles a bill with zero drift.
```

**Owner's decisions for 4.4, recorded 2026-11-21 (the plan was approved "as recommended"; ADR 0026):** open
question 1 of the domain model is answered **no**: a rounding under one rupee is not exempt from maker-checker in
R1; the cap against the balance due is asked when the maker submits and again at approval; the adjustment date is
not before the bill date, which BR-501 now says; the payer need not be active; a posted adjustment is not reversed.
Built without a new module exception: Adjustments reaches the bill through `IBillSettlement` of Billing.

#### 4.5 Reversals

```
Read BR-305, BR-306, US-32, US-33, docs/domain-model.md section 5.3.
Build allocation reversal and payment reversal as maker-checker actions: mirror ledger pairs,
caches and bill status restored, original rows marked Reversed, nothing deleted. Payment reversal
first reverses every active allocation, then posts Dr UnappliedCash / Cr Cash; refused when a
refund exists. A rejected reversal leaves the document as it was.
Done when: US-32 and US-33 pass and a fully reversed payment leaves every balance where it began.
```

**Owner's decisions for 4.5, recorded 2026-11-21 (the plan was approved "as recommended"; ADR 0027):** reversing an
application of credit is its own prompt, 4.5b below; the check "refused when a refund exists" is built with refunds
(4.6), and until then the aggregate throws if part of a payment went anywhere but to allocations; a payment cannot
be asked for reversal while one of its allocations waits for a decision, nor the other way round (409); a reversal
takes no document number, and its mirror posting names the posting it reverses; the reason is required on the
request, kept by the approval request, and copied to the row when the checker approves. The columns are migration
**`0015_PaymentReversals`**, so refunds and users move to `0016_Refunds` and `0017_Users`.

#### 4.5b Reverse an application of credit

```
Read BR-213, docs/domain-model.md section 4.5, ADR 0025 point 5 and ADR 0027.
Build the reversal of a CreditNoteAllocation as a maker-checker action, shaped exactly like the reversal of a
payment allocation (ADR 0027): the cashier asks with a reason (202), a second person approves or rejects;
approval posts the mirror pair Dr Receivable(bill) / Cr CreditAvailable(creditNote), raises
CreditNote.AvailableAmount and Bill.BalanceDue, moves the bill's status back, and marks the row Reversed.
Nothing is deleted. Decide with me first whether the application a credit note makes to its own bill at
posting may be reversed, or only later applications to other bills. Migration 0016_CreditApplicationReversals
(refunds and users move up again).
Done when: an application reversed leaves the credit available again and the bill owed again, with zero drift.
```

**Owner's decisions for 4.5b, recorded 2026-11-21 (the plan was approved "as recommended"; ADR 0028):** only a
later application, made through `POST /credit-notes/{id}/applications`, can be reversed; the application a credit
note makes to its own bill at posting cannot (422 `credit_application_made_at_posting`), and the row says which one
it is in a stored flag, `AppliedAtPosting`; one decision at a time for each application (409), while new
applications from the same credit note stay allowed; no document number, and the reason is required on the
request, kept by the approval request and copied to the row at approval; migrations `0003` to `0015` were committed
on their own first (commit `740fb31`), so that **`0016_CreditApplicationReversals`** was added the ordinary way.
Refunds and users move to `0017_Refunds` and `0018_Users`.

#### 4.6 Refunds

```
Read BR-307, BR-308, US-34, docs/domain-model.md section 4.7.
Build Refund from one source (a payment's unapplied amount or a credit note's available amount):
cap checked at posting, method and reference rules of BR-301, active payer, maker-checker, gapless
number, Dr UnappliedCash or CreditAvailable / Cr Cash. Not reversible. Migration 0017_Refunds.
Then write one end-to-end scenario test that walks bill -> part payment -> credit note ->
adjustment -> allocation reversal -> refund and asserts zero drift and gapless series at the end.
Done when: US-34 and the scenario test pass.
```

**Owner's decisions for 4.6, recorded 2026-11-21 (the plan was approved "as recommended"; ADR 0029):** the BR-306
check is asked when the reversal of a payment is requested, through the refund repository (422 `payment_refunded`),
with no cached refunded amount on `Payments`; the cap and the source's state are asked when the maker submits and
again at the approval, and the payer must be active at the draft and at the approval; the refund date is not before
the date of its payment or credit note, a sentence BR-307 gains (422 `refund_date_before_source_date`); a payment
that waits for its reversal, or was reversed, refunds nothing (409), while a refund that only waits does not stop a
reversal from being asked for; the payment and the credit note record `Refunded` in their own audit trails. The table
is migration **`0017_Refunds`**; users move to `0018_Users`.

Merge `phase-4-payments` to `main`.

### Phase 5. Identity and security (branch `phase-5-identity`)

#### 5.1 Users and roles

```
Read BR-601, US-01, docs/domain-model.md section 4.12, docs/database.md section 3.1.
Build User with roles (TenantAdmin, Supervisor, BillingClerk, Cashier, Viewer): e-mail unique per
tenant, enable/disable, password hashed with a slow salted BCL algorithm, admin password reset.
Users and Roles tables, the development user row, and the foreign keys from CreatedByUserId and
UpdatedByUserId that were waiting for it. Audited. Migration 0018_Users.
Done when: US-01 passes.
```

**Owner's decisions for 5.1, recorded 2026-11-21 (the plan was approved "as recommended"; ADR 0030):** every column
that names a user becomes a foreign key to `Users`, not only the creation and update stamps; the development database
is disposable and is dropped before `0018` (no data step); passwords are PBKDF2-HMAC-SHA-256 with 600,000 iterations,
a 16-byte salt and a 32-byte hash, 12 to 128 characters without rules of composition, and an administrator's reset
sets a given password with no forced change; a user holds at least one role, the tenant's last active administrator
keeps the role and cannot be disabled, and nobody disables themselves; no role check before 5.2 and 5.3; the
development user is a TenantAdmin with a password nothing matches. Settled while building, and confirmed by the owner
with the plan of 5.2: the user POSTs take no Idempotency-Key, because the stored fingerprint of a body with a password
would be a fast hash of the password (ADR 0030 point 7).

#### 5.2 Sign-in and JWT

```
Read BR-602, BR-002, US-02, US-04, ADR 0009. Write an ADR for token issuing first.
Build POST /api/v1/auth/login issuing a 60-minute JWT with tenant id, user id and roles; JWT
validation; ITenantContext and ICurrentUser resolved from the token and from nowhere else; the
development identity stays for Development only. LoginUserLookup is the one sanctioned use of
IgnoreQueryFilters (add it to BannedSymbolsTests). Default-deny authorisation on every controller.
Tests: no token is 401, disabled user refused, a token of tenant A reading tenant B by id gets 404
on every resource built so far.
Done when: every existing API test runs with real tokens.
```

**Owner's decisions for 5.2, recorded 2026-11-21 (the plan was approved "as recommended"; ADR 0031):** the package
`Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.12 in the Api project; HS256 with a key of at least 256 bits from
`Jwt:SigningKey`, a key for Development in `appsettings.Development.json` that is refused anywhere else; every refused
sign-in answers the same 401 `invalid_credentials` (unknown or suspended tenant, unknown address, wrong password,
disabled user), with the reason in the log only; the sign-in is recorded (`LastSignInAtUtc` and a `SignedIn` audit
record), the sign-in request acting as the user it has verified through the `ISignInScope` port; a token stays valid
for its 60 minutes after its user is disabled; in Development a request without a token acts as the development
user; the tests use tokens minted by the API's own issuer for every request; the sign-in takes no Idempotency-Key, and
the owner confirmed ADR 0030 point 7 for the user POSTs with it. Rate limiting of sign-ins is a follow-up.

#### 5.3 Role permissions and tenant settings

```
Read the permission matrix in docs/requirements.md section 1, BR-603, BR-607, US-03.
Apply the matrix as named policies on every endpoint, with one table-driven test that walks every
endpoint with every role. The checker must hold Supervisor or TenantAdmin. Build the tenant
settings endpoints (approval threshold, back-date window, default due days, default prefixes;
prefix change is maker-checker, BR-802), audited.
Done when: the matrix test passes and US-03 passes.
```

**Owner's decisions for 5.3, recorded 2026-11-21 (the plan was approved "as recommended"):** the prompt is built in two
steps, each with its own commit: **5.3, the role permissions (ADR 0032)**, then **5.3b, the tenant settings**. For the
permissions: a blank cell of the matrix is no access, not even reading, except that a Cashier reads credit notes (a
refund may come from one); applying a credit note's credit is in the credit notes' row (TenantAdmin, Supervisor,
BillingClerk); the checker's inbox `GET /approval-requests` is for TenantAdmin and Supervisor; the audit trail is read
by every role, but the trail of a user by a TenantAdmin only; the checker's role is checked by the policy of every
approve and reject endpoint and again by the decision itself, with 403 `approval_checker_role`. For 5.3b: a prefix
change is one maker-checker request for one document type, kept in a new table (`PrefixChanges`), whose approval
changes the tenant's default prefix and the prefix of that type's series of the current fiscal year, so the next number
uses it and numbers already issued keep theirs; the five document types keep distinct prefixes.

#### 5.3b Tenant settings and prefix changes

```
Read BR-607, BR-802, BR-604, US-03, ADR 0008, ADR 0023, ADR 0032 and the decisions of 5.3 above.
Build GET and PATCH /api/v1/settings (TenantAdmin, If-Match): the approval threshold (empty or above
zero), the back-date window and the default due days (0 to 365), applied from the next request and
audited. Build the prefix change: a TenantAdmin asks for a new prefix of one document type, a
Supervisor or TenantAdmin who is not the maker approves or rejects; the approval changes the default
and the series of the current fiscal year; prefixes stay distinct and 1 to 3 characters [A-Z0-9].
Migration 0019_PrefixChanges. The trail of the tenant's settings is the TenantAdmin's alone.
Done when: US-03 passes and the matrix test names the new endpoints.
```

**Owner's decisions for 5.3b, recorded 2026-11-21 (the plan was approved "as recommended"; ADR 0033):** the checkers
(TenantAdmin and Supervisor) read the prefix changes, which also reach their inbox; the approval request's unused
document type `NumberSeries` is renamed `PrefixChange`; a prefix belongs to one document type for good (never another
type's default, nor a prefix another type's series or changes ever had); the trails of the tenant, of its prefix
changes and of its number series are the TenantAdmin's alone. No new raw SQL. Built as decided; the build added no
Idempotency-Key on the prefix-change posts and answers the request with 201 (ADR 0033 built note). **Found while
building, closed 2026-11-23:** a first number of a type in a fiscal year, taken in the milliseconds of an approval,
kept the old prefix for that year (ADR 0033 built note point 5). The owner chose option B with the plan of 5.4: the
approval holds the series with the allocator's locked read alone (one more statement in the allocator's file, no new
sanctioned file), built and committed on its own before 5.4 (ADR 0033 "Amended 2026-11-23"; CLAUDE.md section 9,
item 31).

#### 5.4 Database logins and hardening

```
Read docs/database.md section 7 and the open review findings in CLAUDE.md section 9 item 6.
Create the application and migrator logins with their grants (no DELETE on financial tables,
insert-only audit and ledger, SELECT on __EFMigrationsHistory); run the database tests as the
application login. TLS and HSTS, allowed hosts, server header removed, rate limit on login, CORS
for the frontend origin. Write docs/security.md in full, with a threat model.
Done when: a test proves the application login cannot delete a bill or update an audit row.
```

**Owner's decisions for 5.4, recorded 2026-11-23 (the plan was approved "as recommended"):** the prompt is built in two
steps, each with its own commit, after the fix of ADR 0033 built note point 5 is committed: **5.4, the database logins
and grants (ADR 0034)**, then **5.4b, the HTTP hardening and `docs/security.md` in full**. For 5.4:
- the grants are in a migration, **`0020_DatabaseRoles`**, scaffolded empty and filled by hand with
  `migrationBuilder.Sql`, idempotent, with a `Down()`: it creates the database role `billingrecon_application`, so the
  grants exist wherever the migrations run; the logins, their passwords and their database users are made per
  environment by a script in the runbook (a new section 10), never in git;
- the grants are schema-wide with exceptions taken from the model's markers: `SELECT, INSERT, UPDATE` on the schema
  `dbo`; `DENY UPDATE, DELETE` on the append-only tables (`LedgerPostings`, `LedgerEntries`, `AuditRecords`);
  `DELETE` only on the tables of the `IHardDeletable` entities (`BillLines`, `CreditNoteLines`, `UserRoles`,
  `IdempotencyKeys`: four, where `docs/database.md` section 7 lists two, which is corrected); `Roles` and
  `__EFMigrationsHistory` read-only. A table-driven test compares every table's effective permissions with its markers,
  so a future append-only table without its `DENY` fails it with the line to add;
- the migrator login holds `db_ddladmin`, `db_datareader`, `db_datawriter` and `db_securityadmin` (it creates the role
  and grants), where section 7 named two roles;
- the database tests run as the application login wherever production code meets the migrated test database (the API
  host and the contexts of the production code); Respawn, the migration, the catalogue checks, the probe database and
  the Development and Pending databases stay on the administrator's connection. The test login has a random password
  per run. LocalDB 2019 on the office machine runs in mixed mode, so this is pre-checked there.

#### 5.4b HTTP hardening and the security document

```
Read the 5.4 decisions above, ADR 0031 point 12, the review finding 2.4 in CLAUDE.md section 9 item 6,
the security row of docs/requirements.md section 5 and docs/security.md.
Propose first, for my approval: where TLS ends (the API or a proxy in front of it) and what that means
for HSTS, HTTPS redirection and forwarded headers; how allowed hosts are configured and checked at
startup; the rate limit of sign-ins (the built-in limiter, its partition and limits, the 429 answer as a
ProblemDetails with its errorCode and Retry-After); CORS for the frontend origin from configuration.
Then build them with the server header removed, and write docs/security.md in full with a threat model.
No new package. Keep the CI smoke run of the image green.
Done when: a test proves each header and refusal, and docs/security.md has no "planned" row left
without a prompt or an accepted risk.
```

**Owner's decisions for 5.4b, recorded 2026-11-23 (the plan was approved "as recommended"; ADR 0035):** TLS ends at a
reverse proxy in front of the API; forwarded headers are believed from the loopback addresses and
`ReverseProxy:KnownProxies` only, one proxy deep; no HTTPS redirection: outside Development a request that did not
arrive over HTTPS is answered 400 `https_required`, the two health endpoints excepted; HSTS for one year, this host
only, not preloaded. `AllowedHosts` checked by the API's own middleware (400 `host_not_allowed`), required and without
`*` outside Development. Two sign-in limits, both 429 `too_many_sign_ins` with `Retry-After`: 30 a minute per client
address, and an account held 15 minutes after 10 refusals (the right password included; a success counts nothing),
fixed in code, in memory per instance. CORS from `Cors:Origins` (none by default, `http://localhost:5173` in
Development, https elsewhere), no credentials. No `Server` header; `nosniff` on every response and `no-store` with a
content security policy on the answers of `/api`. The threat model's accepted risks are the rows marked so in
`docs/security.md` section 10; where the browser keeps the token goes to 7.1, the frontend's own headers and TLS at the
local demo's nginx to 8.1, each prompt below gaining a sentence. Built as decided (ADR 0035 built note).

Merge `phase-5-identity` to `main`.

### Phase 6. Reporting (branch `phase-6-reporting`)

#### 6.1 Registers and balances

```
Read BR-901, US-40, US-42. Build read-only query services in the Reporting module (it never
writes): bill register, payment register, daily collection by method for an IST business date,
outstanding by payer, unapplied cash and available credit, pending approvals. Paged, filterable,
tenant-filtered, every figure derived so that it ties to ledger entries. Tests compare each report
with the ledger after the Phase 4 scenario.
Done when: US-40 and US-42 pass.
```

**Owner's decisions for 6.1, recorded 2026-11-23 (the plan was approved "as recommended"; ADR 0036):** Reporting's
Infrastructure reads the tables of every business module through five listed exceptions to the module rule (ADR 0003,
amendment of 2026-11-23), and an architecture test keeps Reporting from writing. The registers list documents and their
totals are held against the ledger; the balances are the cached balances (BR-404); the daily collection is read from
the Cash entries of the business date (received, reversed under the payment's method, refunded under the refund's
method, net). Eight GET endpoints under `/api/v1/reports`; a paged report answers its totals over every page and, for a
register, its period (both ends or neither, at most 366 days, the current IST month by default). A new policy `Reports`
for all five roles. The work stays on `phase-2-billing`. Built as decided; the build added three words of Reporting's
own for the Billing and Payments enums, which its Application and Api layers may not name (ADR 0036 point 7), and sums
the totals in memory, since the database cannot sum a converted `Money` (built note).

#### 6.2 Reconciliation and audit trail

```
Read US-41, US-43, docs/database.md section 5.6. Expose the reconciliation report (cache versus
ledger, drift rows) and the audit trail of any document. Add a test helper that asserts zero
drift, and call it at the end of every financial integration test that does not already do so.
Complete docs/api.md with every endpoint and the full error-code catalogue.
Done when: US-41 and US-43 pass and a seeded drift is reported.
```

**Owner's decisions for 6.2, recorded 2026-11-23 (the plan was approved "as recommended"; ADR 0036, "Amended
2026-11-23 (Prompt 6.2)"):** the reconciliation report is `GET /api/v1/reports/reconciliation` under `Reports`, through
the Receivables module's contract, paged with its counts per identity, and logged as an error when it finds drift;
gapless numbering joins the reconciliation as identity 11, `NumberSeriesGap`, in the statement already sanctioned; the
zero-drift helper is automatic, the reconciliation of every tenant after every database test, with an explicit, named
opt-out for the tests that leave a defect on purpose; US-43 is proven for a bill read by a Viewer; `docs/api.md` is
completed with every endpoint and an error-code catalogue, kept complete by two architecture tests. Built as decided;
the first run with the automatic check found the 41 tests that seed defects or take numbers without documents, and none
that leaves drift by mistake. Found: the decimals of a creation's audit image differ in scale from later images (ADR
0036 amendment).

#### 6.3 Dashboard figures

```
Read BR-901, US-40 to US-43, docs/database.md sections 5.5 and 5.6, and the Reporting query
services of 6.1 and 6.2.
The dashboard is new R1 scope: first propose the additions to docs/requirements.md (one user
story with acceptance criteria, and its row in the permission matrix) and wait for my approval.
Build read-only query services in the Reporting module (it never writes) and
GET endpoints under /api/v1/dashboard, for a period given as from/to IST business dates
(default: the last 30 days), tenant-filtered:
- summary: total billed (bills posted in the period), amount collected (payments recorded in
  the period), outstanding dues, unapplied cash, pending approvals, reconciliation drift rows;
  each with the same figure for the previous period of equal length and the change in percent
  (null when the previous figure is zero);
- trend: billed and collected per business date or per week, cumulative within the period, and
  the daily series each summary card needs for its sparkline;
- ageing: outstanding BalanceDue of posted bills by days past DueDate as of TodayIst, in the
  buckets 0-30, 31-60, 61-90 and over 90, with amount and share;
- top outstanding payers (first 5), recent payments (last 5), pending approvals (first 3) and
  drift rows (first 3), reusing the 6.1 and 6.2 services, not copying their SQL.
Raise with me before coding: outstanding dues and unapplied cash "as of" a past date can be
derived from ledger entries, and pending approvals from the request timestamps, but drift rows
have no history. Propose either no trend for that card or a snapshot table, with the cost of each.
All amounts leave the API as exact decimals; no rounding to lakh on the server.
Tests: after the Phase 4 scenario, every summary figure equals the figure derived from ledger
entries; the ageing buckets sum to outstanding dues; a bill due today falls in 0-30; period
boundaries at IST midnight; another tenant's data never appears; empty period returns zeros.
Add the endpoints to docs/api.md. No migration unless I approve the snapshot table.
Done when: every dashboard figure ties to the ledger and the ageing total equals outstanding dues.
```

Merge to `main`. The backend of R1 is complete here.

**Owner's decisions for 6.3, recorded 2026-11-23 (the proposal was approved "as recommended"; ADR 0037):** the
requirements gain US-44 (a Supervisor, a TenantAdmin or a Viewer sees the dashboard of a period against the previous one,
with the prompt's tests as its acceptance criteria), BR-902 (the period, the comparison and what each figure is) and the
matrix's row "Dashboard" for TenantAdmin, Supervisor and Viewer; **no trend for the drift card** (its count of now, no
snapshot table, no migration); the figures of past days are summed from the ledger by business date in SQL, through
`ILedgerMovements` in Receivables and a sixth sanctioned raw-SQL file, `LedgerMovementsQuery`; collected is gross of
reversals and refunds; seven GET endpoints, one per panel, the four lists calling the handlers of the 6.1 and 6.2
reports; the payment register gains `order=NewestFirst` for the recent payments; the policy `Dashboard`; no package.
Built as decided. Found: Entity Framework cannot sum `Money` behind its converter (a probe failed), which is why the SQL;
`ReportingConventionTests` refuses `.Add(` in Reporting, so the series are filled by index (the test was not loosened).
The merge to `main` is the owner's step; nothing has been merged.

### Phase 7. Frontend (branch `phase-7-frontend`)

Node 22.22 or newer is needed (React Router 8's minimum, `engines` in `frontend/package.json`); the office machine has
Node 24.22.2 since 2026-11-23 (CLAUDE.md section 2).

#### 7.1 Scaffold and sign-in

```
Propose the frontend stack for my approval first (React, TypeScript, Vite; routing, data fetching,
forms and a component approach, each with a reason), then write the ADR. Build frontend/: login,
token handling, a typed API client generated from /openapi/v1.json, one error component that reads
ProblemDetails errorCode, role-aware navigation, lint and test set-up, and a CI job.
Theming is part of the scaffold. Propose it in the stack ADR: design tokens as CSS variables
(surface, text, border, accent, success, warning, danger, and a fixed series palette for
charts), a light and a dark set switched by data-theme on <html>, and no raw colour value in any
component (add a lint rule or a test that fails on one). The user chooses System, Light or Dark
from the top bar; System follows prefers-color-scheme and reacts when it changes; the choice is
kept in localStorage and applied by an inline script in index.html before React mounts, so there
is no flash. Name the chart library in the same ADR, with the reason, and say how its colours
follow the theme. Text contrast meets WCAG AA in both themes.
Say in the stack ADR where the browser keeps the token and why (docs/security.md section 10,
row S5): memory, sessionStorage or localStorage, weighed against a script injected into the page.
Done when: I can sign in and see a role-appropriate empty shell, and the empty shell switches
between the three settings without a reload and without a flash on a hard refresh.
```

**Owner's decisions for 7.1, recorded 2026-11-23 (the proposal was approved "as recommended"; ADR 0038):** React 19,
TypeScript 5.9 (the lint and the type generator accept nothing later yet), Vite 8; React Router 8 in data mode; TanStack
Query; a client typed from the API's OpenAPI document (openapi-typescript, openapi-fetch), committed as a snapshot that
`OpenApiSnapshotTests` keeps equal to the API's and rewrites on request; React Hook Form without a schema library; our
own components on CSS Modules and the tokens, no UI kit; Recharts named for 7.6; the token in `sessionStorage`; ESLint 9
with the accessibility rules, Prettier as the format gate; the branch stays `phase-2-billing`; 27 npm packages pinned
exactly, resolved as of two weeks before. Built as decided. Found: the OpenAPI document gave enums as numbers, because the
generator reads the minimal-API JSON options, so `Program.cs` gives them the enum converter too; it gives amounts as
`number | string`, which is true of what the API reads and is left for the owner; one advisory in a lint tool's tree was
fixed by its next patch. No screen of users and settings is planned in any prompt; the TenantAdmin's section says so.

#### 7.2 Patients, payers and catalogue screens

```
Build list, create, edit, deactivate and reactivate screens for patients, payers and service
items, with paging and search. Send If-Match on every change and show a clear "someone else
changed this record" message on 409. Component tests for the forms.
```

**Owner's decisions for 7.2, recorded 2026-11-23 (the plan was approved "as recommended"; ADR 0039):** lists keep their
search, status and page in the address; every change sends `If-Match` from the record's `version`; who may change is a
copy of the API's policies, held to `Policies.cs` by a test; deactivating asks once more, in place; decimals are typed as
text and sent as JSON numbers, refused beyond 15 significant digits; **payers have a section of their own**, created from
the patient's page or as guarantors from the list; **a conflict says nothing was saved, lists what changed, and offers
"Load the latest version" or "Keep my changes"**. Built as decided, with no package. Found: the OpenAPI snapshot of 7.1
held Windows line breaks inside its descriptions and would have failed on the Linux runners; the test now writes every
line break as LF.

#### 7.3 Bill screens

```
Build the bill draft editor (lines from the catalogue or free text, live totals that mirror
BR-202 and BR-203 but always display the server's figures after save), void, post with a generated
Idempotency-Key that is reused on retry, and the bill view with status, balance and audit trail.
```

**Owner's decisions for 7.3, recorded 2026-11-23 (the plan was approved "as recommended"; ADR 0040):** **one editor for a
draft**, its header and lines saved as a `PATCH` then a `PUT` with the version the first answered, each only when it
changed; **the key of an operation in memory**, sent again with the same request after no answer, a fault or "still
running", dropped after a success or a refusal; **live totals by exact arithmetic written on `BigInt`**, with no package;
**names in the bill list read per patient and payer** and cached, the API unchanged. Approve and reject stay with 7.5; a
bill waiting for approval says so. Built as decided. Found: the browser's and the server's line arithmetic are now held
to one file of cases (`frontend/src/bills/lineCases.json`) by a test on each side; the parties of a new draft are asked
for together with its fields.

#### 7.4 Cashier screens

```
Build record payment, allocate to bills (outstanding bills of the payer, caps enforced in the
form and by the server), request reversal, draft refund, and the payer statement (outstanding,
unapplied cash, available credit).
```

**Owner's decisions for 7.4, recorded 2026-11-23 (the plan was approved "as recommended"; ADR 0041):** **allocating lists
the payer's bills that owe something, oldest first**, with "Apply oldest first" filling them from the unapplied cash and
the caps checked in exact paise before sending; **a refund starts from its source**, a payment's page or the payer
statement's lists, with no free-standing new refund; **the payer statement is a page of its own**, linked from the
payer's page. Approving and rejecting reversals and refunds stay with 7.5; a document waiting for a decision says so.
Built as decided, with no package; one hook, `useDocumentAction`, carries every financial action on a document.

#### 7.5 Approvals and reports

```
Build the checker's inbox (approve, reject with comment; hidden from the maker), credit note and
adjustment drafting, and the six R1 reports with date filters and CSV export.
Done when: the Phase 4 scenario can be performed entirely in the browser by three users.
```

**Owner's decisions for 7.5, recorded 2026-11-23 (the plan was approved "as recommended"; ADR 0042):** **the prompt is
split**: 7.5 builds the inbox, the credit notes and the adjustments and is done with the three-user scenario, and the
reports are **7.5b** below; **the pending approvals report answers `parentDocumentId` and `makerComment`**, a change of
the API, so that an allocation or an application of credit is decided through its payment or credit note and the checker
reads the maker's reason; **decisions are made in the inbox**, a request reviewed in place, the maker's own request
without a review; **a credit note's lines come from its bill**, by quantity at the bill line's price and tax, with free-text
lines beside them. Built as decided: the allocation form of 7.4 is shared with the application of credit
(`ApplyToBills`), and credit notes and adjustments are drafted from the posted bill they correct.

#### 7.5b Reports

```
Read the reports in docs/api.md section 8.15 and BR-901, and the screens ADRs 0039 to 0042.
Build the six R1 reports of BR-901 under /reports: the bill register, the payment register with
the daily collection by method, outstanding by payer with a payer's balance, unapplied cash and
available credit, pending approvals, and the reconciliation. Each with its filters in the address
(the registers a period of business dates in India, the current month by default), its totals over
every page as the API answers them, exact amounts with Indian grouping, and a CSV export of every
row of the filter, not only the page shown, with the totals and the period.
Decide with the plan where the CSV is written (in the browser from every page, or by the API).
Tests: each report's filters sent and kept in the address, its totals the server's, the CSV's rows,
amounts and escaping, and access by role.
Done when: every report after the Phase 4 scenario shows the figures the API answers, and its CSV
opens in a spreadsheet with the same rows and totals.
```

**Owner's decisions for 7.5b, recorded 2026-11-23 (the plan was approved "as recommended"; ADR 0043):** **the CSV is
written in the browser** from every page of the filter and held to the server's totals (read once more if they differ,
refused if they differ again; at most 10,000 rows; UTF-8 with a BOM, plain decimals, quoted fields, the formula guard);
**the six reports are eight screens**; **`/reports` is an index**, each screen's filters in its address and its totals
the server's. Built as decided, with no package and no change of the API.

#### 7.6 Dashboard

The layout reference `docs/ui/dashboard.png` must be in the repository before this prompt is given.

```
Read the dashboard user story in docs/requirements.md, the dashboard endpoints in docs/api.md
and the theming section of the frontend ADR. The layout reference is docs/ui/dashboard.png;
follow its structure, not its sample data.
Build the dashboard as the landing page for the roles the permission matrix allows:
- period picker (last 7, 30, 90 days, custom), IST business dates;
- six summary cards: total billing, amount collected, outstanding dues, unapplied cash, pending
  approvals, reconciliation issues. One component, one colour token per card: the background is
  the token mixed into the card surface (color-mix, a stronger share in dark), the icon tile is
  the solid token, and the sparkline is a 2px line with a fill beneath it fading to transparent,
  running flush to the card's bottom edge. The change against the previous period is coloured by
  meaning, not direction: a fall in outstanding dues, unapplied cash, approvals or issues is good;
- billing against collections line chart (collections dashed, tooltip with both values);
- ageing donut with the total in the centre and a legend showing amount and share;
- recent payments, top outstanding payers (with proportional bars), pending approvals and
  reconciliation exceptions, each with a "view all" link to the screen built in 7.4, 7.5 or 7.5b.
  The approvals panel follows the checker's-inbox rule of 7.5 (a maker does not see a Review
  action on their own request).
Money: cards may show a compact figure (₹12.8L) with the exact amount in a tooltip and for
screen readers; tables show exact amounts with Indian grouping, right-aligned, tabular digits.
Every figure is the server's; the client computes nothing but the compact display form.
Each panel has its own loading skeleton, empty state and error state (the ProblemDetails
component of 7.1), so one failed request does not blank the page. Charts carry a text
alternative. The grid reflows from six cards per row down to one.
Tests: component tests for the card (good and bad direction, null change, zero), the compact
money formatter (99,999 / 1,00,000 / 12,80,000 / 1,00,00,000), each panel's empty and error
states, and a test that renders the page in both themes and fails on any colour not taken from
a token.
Done when: the dashboard after the Phase 4 scenario shows the same figures as the 6.1 reports,
in both themes, for a Supervisor, and a role without the permission does not see it.
```

**Owner's decisions for 7.6, recorded 2026-11-23 (the plan was approved "as recommended"; ADR 0044):** the layout
reference is the owner's prototype screenshot, copied to `docs/ui/dashboard.png` with its dark and phone views;
Recharts is installed at 3.10.1 (3.11.0 was under a week old). Built as decided; the dashboard and Recharts load with
the dashboard's route.

Merge to `main`.

### Phase 8. Delivery (branch `phase-8-delivery`)

#### 8.1 One-command local run

```
Add api and frontend services to docker-compose.yml (frontend served by nginx, API behind it),
a migration step that runs as the migrator login, and an opt-in demo seeder: one tenant, five
users (one per role), patients, payers, a catalogue and a week of bills and payments.
nginx ends TLS with a local certificate and is the API's known proxy (docs/runbook.md section 11):
outside Development the API refuses plain HTTP. Give the frontend's pages their own headers: a
content security policy, frame-ancestors 'none' and nosniff (docs/security.md section 10, rows S5
and E6).
Done when: docker compose up on a clean machine gives a working, signed-in demo.
```

**Owner's decisions for 8.1, recorded 2026-11-23 (the plan was approved "as recommended"; ADR 0045):** the demo is
entered through the API, after an opt-in bootstrap of the demo hospital and its administrator; nginx makes a local
self-signed certificate on its first start; the migrations run as in runbook section 10 (logins as the administrator, the
idempotent script as the migrator, the role as the administrator); the branch `phase-8-delivery` is cut from
`phase-2-billing`. Built as decided; the migration step uses the runbook's script, not a bundle.

#### 8.2 Performance and browser tests

```
Read the performance row of docs/requirements.md section 5. Write a load test for bill posting
and payment recording at 50 concurrent users, report p95 and number-allocation time, and fix what
misses the target. Add a small Playwright suite for the main journey (ask before adding it).
Done when: p95 is under 500 ms and reconciliation shows zero drift after the load run.
```

**Owner's decisions for 8.2, recorded 2026-11-24 (ADR 0046):** the plan was approved "as recommended": the load test a
Node script with no package, run as a compose service behind a profile; `@playwright/test` 1.65.1 for the journey; the
number series' wait and hold told by the API in `Server-Timing`, off by default; the 20 ms read as the hold. The first
full run on the office machine, with no pause between requests, missed (posting p95 1,130 ms: the 50 users queued on the
series of bills) and then filled the machine's disk. The owner then chose: judge 50 users with a one-second pause between
their requests and report the no-pause run as a stress figure; read the idempotency key's row before the number (one
round trip out of the hold); and run smaller on the office machine (2,000 bills), leaving the full 10,000 to the
personal machine. Built as decided; every target met there with no drift.

#### 8.3 Release R1

```
Finish docs/runbook.md (migrations outside Development, backup and restore, incident steps,
reconciliation drift procedure), bring README, CLAUDE.md and every ADR status up to date, close or
re-file every item of CLAUDE.md section 9, walk every user story US-01 to US-43 and list the test
that proves it, then tag v1.0.0.
Done when: the traceability table has no empty row.
```

**Owner's decisions for 8.3, recorded 2026-11-24:** given in the session of 8.2, after 8.2 was committed, at the owner's
word. The plan was approved "as recommended": every ADR still Proposed (0003, 0007, 0009, 0010, 0024) and every point
still awaiting review (ADR 0015's additions, ADR 0019 point 5, ADR 0027 point 9) is accepted as built; the tag
`v1.0.0` is annotated, on the 8.3 commit, local only (not pushed), and says what is still owed (runbook section 8);
`main` is an ancestor of `phase-8-delivery`, so a fast-forward merge keeps it on `main`. The traceability table covers
US-01 to US-44 (US-44 was added by 6.3) and is held by an architecture test; what section 9 of CLAUDE.md left open is in
`docs/backlog.md`.

Merge to `main`; R1 is the tag `v1.0.0`.

### Phase 9. The interface (branch `phase-9-interface`)

R1 is released and every screen works, but the screens are plain. Phase 9 gives the whole application one designed
interface, at the level of the owner's reference for the sign-in page, **without changing what any screen does**. Asked
for by the owner on 2026-11-24: "make all the screens one of the best, like this", and the company's logo and name in the
product. Four prompts, one per session, in order: the brand, the tokens and the sign-in page (9.1); the shell and the
shared components (9.2); the money screens (9.3); the dashboard, the reports and the finishing pass (9.4). Each begins
with a plan and ends with screenshots the owner can judge by eye.

**Before 9.1, in the repository** (9.1 stops without the first two):

1. `docs/ui/sign-in.png`: the reference, 1672 x 941, as the owner gave it on 2026-11-24. **Not in the repository yet.**
2. `frontend/src/assets/brand/signin-hospital.webp`: the photograph of the reference on its own (the hospital building,
   no tint, no text, no card), at least 2,400 px wide, a photograph the owner has the right to use. **Not in the
   repository yet.** The page must stay legible without it: the tint, not the photograph, carries the text.
3. `frontend/src/assets/brand/ashline-logo.svg`: the company's mark. **In the repository since 2026-11-24**, copied path
   for path from the company's site.

**The company** is **ashline**, the owner's studio (https://ashish-singh-ai-studio.vercel.app/). Its logo there is drawn
inline: a violet hexagon (`#7c3aed`) holding a white A whose crossbar runs out to a dot (`#1a1033` on a light ground,
`#c4a1ff` on a dark one), followed by the name in lower case, "ash" at medium weight in the text colour and "line" at
regular weight in the violet (`#7c3aed` on light, `#b78cff` on dark). When the pointer is over it the dot travels in along
the line (0.35 s, not with reduced motion). The file above is that mark with its light colours; the name is set as text
beside it, as on the site. The product is credited as "A product of ashline" and "BillingRecon by ashline".

**What the reference shows** (measured by eye on its 1672 x 941 frame; 9.1 measures it properly and says so in its plan):
the card spans about 41% of the width, from about 52% to 93% across and from about 9% to 93% down, with corners of about
28 px; its fields are about 575 px wide and 56 px high with corners of about 12 px; the button is about 58 px high; the
left block starts about 5.5% in, with a mark of about 120 px. The type looks like Inter.

**The bar of Phase 9.** Every prompt of the phase is held to all of it:

1. **One system.** Every colour, gradient, shadow, radius, space, type size, duration and easing is a token of
   `frontend/src/theme/tokens.css`, in a light and a dark set. No screen writes one of its own: the colour scan of ADR 0038
   covers every new file, and gradients, shadows and masks are tokens too.
2. **Both themes, WCAG 2.2 AA.** Text at 4.5:1; large text, controls and focus rings at 3:1; the contrast test holds every
   pair in use. Nothing is said by colour alone: a status has an icon and a word. Every control works by keyboard, shows
   its focus and is at least 24 px to hit. Windows forced colours stay usable.
3. **Dynamic, with a purpose.** Motion says what happened: something arrived, changed, is pending, was refused. Durations
   and easings are tokens (about 120, 200 and 320 ms). Only `transform` and `opacity` are animated. Nothing loops but the
   brand's waves. With `prefers-reduced-motion: reduce` nothing moves and states change at once. Nothing shifts the layout
   after the first paint.
4. **Fluid.** From 360 to 1,920 px wide with no sideways scroll of the page; sizes scale with the window (`clamp`); layouts
   change at breakpoints that are tokens; at 200% zoom nothing is clipped.
5. **Fast and calm.** The first paint has the theme, the layout and the type's metrics: no flash, no jump. The typeface is
   self-hosted and preloaded, images have their sizes, a route's code loads when the route is visited, and loading is a
   skeleton of what is coming.
6. **Nothing from elsewhere.** No font, script, style, image or request from another origin, and no inline style attribute
   or script but the pinned theme script. nginx's content security policy (`frontend/deploy/nginx.conf`) stays as it is; a
   browser test applies that exact policy to the built app and fails on any refusal, because the office machine has no
   nginx.
7. **No change of behaviour.** The API, the routes, the policies and what each screen sends stay as they are. So do the
   labels, names and headings that the tests, the browser journey of 8.2 and `docs/traceability.md` use: a test title the
   table names is not renamed.
8. **Proven and shown.** Every existing test passes unweakened; what is new has tests; each prompt leaves screenshots of
   its screens in both themes in `docs/ui/`, and an ADR.
9. **No package without asking.** Our own components on CSS Modules stay (ADR 0038: no UI kit). A package the plan argues
   for (icons, motion, an accessibility check) is asked for with its name, a version at least a week old, its licence and
   its size.

**Not in this phase** (each sends a request no screen sends today, or is a feature; ask for it as its own prompt): a count
of waiting approvals in the navigation; a dashboard that refreshes itself; the screens for users and settings (backlog
B18); a command palette; languages other than English.

#### 9.1 Brand, design tokens and the sign-in page

```
Read ADR 0038 (stack, theming, the raw-colour rule, the pinned theme script), ADRs 0039 to 0046,
docs/security.md section 10 (rows S5, E6 and I14), frontend/deploy/nginx.conf (the headers nginx
sends since 8.1) and "The bar of Phase 9" in docs/build-prompts.md.

The reference is docs/ui/sign-in.png, 1672 x 941: match it exactly, in layout, proportions,
colours, type sizes, icons and wording. The photograph is
frontend/src/assets/brand/signin-hospital.webp and the company's logo is
frontend/src/assets/brand/ashline-logo.svg: use both files as they are, do not redraw the logo,
and load nothing from another origin (the content security policy). If the reference or the
photograph is not in the repository, stop and tell me: never substitute, fetch or generate one.

Show me the plan first:
- what you measured on the reference: each element's position and size on the 1672 x 941 frame
  and as a share of it, the type sizes and weights, the radii, the colours;
- the tokens for light and dark (the brand blues, surfaces, text, borders, states, the gradients,
  the shadows, the photograph's mask, radii, spacing, the type scale, durations and easings) with
  the contrast of every text and surface pair in use (WCAG 2.2 AA). White text over the
  photograph is judged against the tint alone, at the lightest point any text sits on;
- the typeface (the reference looks set in Inter: confirm it or name a closer open-licence face),
  its licence and weights, how it is self-hosted (woff2 in the repository, subset, preloaded, a
  fallback with matched metrics so that nothing moves when it arrives), that it has the rupee
  sign and tabular figures, and what it weighs;
- the icons as our own inline SVG on one grid, drawn with currentColor (ask before any package);
- what changes in index.html and in nginx's headers, if anything. The inline theme script and its
  pinned hash should stay as they are; if they cannot, the hash in nginx.conf and its test move
  in the same change.

Build the sign-in page as the reference shows it, measured on its frame and scaling with the
window:
- the background: the photograph over the left two thirds, fading out to the right, under a blue
  tint that runs from deep navy at the left to the pale page colour at the right; two waves along
  the bottom edge; a pale right side with one soft curved shape;
- left, on the tint: the BillingRecon mark (a blue cross with rounded arms and a darker cross
  inside it), "BillingRecon" in heavy white type, "Reconcile. Verify. Resolve." beneath it; then
  three lines with outline icons: "Secure hospital billing reconciliation" (a shield with a
  tick), "Trusted financial insights" (a bar chart), "Better care through accurate billing" (a
  handshake);
- right: a white card with large rounded corners and a soft shadow, about 41% of the width; a
  blue pulse line at its top; "Sign in to BillingRecon"; "Access your hospital's billing
  dashboard"; the fields Hospital code, E-mail address (placeholder you@hospital.org, an envelope
  at its right) and Password (placeholder "Enter your password", a show/hide button at its right
  with aria-pressed), each label with a solid blue icon (a building, an envelope, a padlock); a
  full-width blue "Sign in" button with its icon and a pending state; a divider reading "Need
  help?" and "Contact support" beneath it with its icon, a mailto from the build's configuration
  (VITE_SUPPORT_EMAIL, passed to the frontend image from .env; no request is made for it), the
  divider and the link absent when none is set;
- the company: "A product of" and the ashline logo with its name ("ash" at medium weight, "line"
  at regular weight in the company's violet), bottom left over the wave, on a plate that keeps
  the logo legible on blue. This is one of two additions to the reference;
- the theme choice of today, small, in the top right corner over the pale side: the other one.

Make it move, with a purpose, from the motion tokens. The tint, the waves and the card are in the
first paint, and the photograph fades in over the tint once it has decoded: no flash, no jump.
The card rises a few pixels as it fades in, the three lines follow one after another, and the
pulse line draws itself once and rests. The two waves drift slowly, the only thing that loops. A
field's border and ring ease in on focus, the show/hide icon cross-fades, and the button answers
the pointer and the press and, while the request runs, shows a spinner in place of its icon. A
refusal eases in above the fields and is announced. Over the company's logo the dot travels in
along its line, as on the company's site. Only transform and opacity are animated, and with
prefers-reduced-motion nothing moves.

Keep what sign-in does today: the same request; the refusals through the ProblemDetails component
(429 too_many_sign_ins shows the wait from Retry-After); the two notices of a session that ended;
the remembered hospital code; the return address; autocomplete attributes; labels tied to their
fields; visible focus; the theme choice with its three settings. Under 900px the three lines and
the photograph fold away (the photograph is not requested at all) and the card stays; at 360px
the page has no sideways scroll. A dark set of the same tokens for the dark theme.

Then the brand and the controls everywhere: the favicon (the BillingRecon mark), the page title
("Sign in · BillingRecon"), theme-color following the theme from a token (index.html holds no raw
colour), and the shared buttons and fields restyled from the same tokens, so that the screens of
7.2 to 7.6 already wear the new palette and controls. Their layouts are 9.2 to 9.4.

No change of behaviour: no change of the API, of a route, of a policy or of what a screen sends.
Every existing frontend test passes unweakened, and the test that refuses a raw colour holds for
the new files too (the gradients, the shadow and the photograph's mask are tokens). The journey
of 8.2 fills the field labelled "Password": with a show/hide button beside it, make that selector
exact in the same change and say so; its one exception, the browser's request for /favicon.ico,
goes away with the favicon.
Tests, in Vitest: the form (labels, show/hide, pending, a refusal, the 429 wait, the notices, the
support link present and absent), the logo and the photograph rendered with their alternative
text (the photograph decorative), every colour from a token in both themes, the token pairs'
contrast.
Tests, in the browser (frontend/e2e): the built page at 1672 x 941 held to your measurements of
the reference within a stated tolerance; the built app under nginx's exact policy, read from
nginx.conf and applied in the test, with no securitypolicyviolation and no console error, in
both themes; no layout shift after the first paint; with reduced motion no animation runs; the
tab order; 390 and 360 px wide without a sideways scroll.
Add screenshots of the built page (both themes at 1672 x 941, and at 390 px) to docs/ui/. Write
ADR 0047, and add the nginx check of this prompt to runbook section 8.
Done when: beside docs/ui/sign-in.png in the browser at 1672 x 941 the built page differs only by
the ashline credit and the theme control; I sign in through it in both themes without a flash;
with reduced motion nothing moves; and the console shows no content security policy refusal.
```

**Owner's decisions for 9.1, recorded 2026-11-24 (ADR 0047):** the reference was found in the owner's Downloads and
copied in; there is no photograph apart from it, so the page was built to take one later and stands without it. The plan
was approved "as recommended": field borders darker than the reference's, to meet 3:1 (the alternative, matching the pale
border, was offered); Inter through `@fontsource-variable/inter` 5.3.0, the one new package; our own icons; the theme
control as the second addition to the reference. Built as decided.

#### 9.2 The shell and the shared components

```
Read ADR 0047 (the tokens, the typeface, the icons, the motion) and "The bar of Phase 9" in
docs/build-prompts.md; ADRs 0038 to 0043 for what the shell and each shared component do today;
docs/ui/sign-in.png and the screenshots 9.1 left in docs/ui/.

Show me the plan first: the shell at 1440, 900 and 390 px wide (a drawing in words is enough);
the shared components and the states each one gets; what a table does under 720 px (rows that
fold into labelled cards, or a table that scrolls inside its own frame with its first column
pinned: recommend one); the icon of each section of the navigation; any new token; any package
you want (ask).

The shell:
- a sidebar: the BillingRecon mark and name at its top; the sections of the permission matrix,
  each with its icon, the current one marked by more than colour. It collapses to a rail of
  icons with tooltips (the choice remembered in this browser), and under 900 px it is a drawer
  behind a menu button: focus stays inside it, Escape closes it, focus returns to the button;
- a top bar: the hospital's name, the user's name and roles, the theme choice and "Sign out",
  which stays a visible button;
- a footer: the ashline logo with "BillingRecon by ashline";
- a "Skip to content" link; every page sets the document title ("Bills · BillingRecon"), and
  after a navigation its heading takes the focus;
- where the browser has view transitions, the content cross-fades between pages and the shell
  does not move.

The shared components, each from the tokens and each with its states (rest, hover, focus,
pressed, disabled, pending, invalid, read-only):
- buttons: primary, secondary, danger, link, and icon-only with a name;
- fields: a label with an optional icon, a hint, an error with an icon, a required mark, a prefix
  or suffix such as the rupee sign or %, select, date, search, textarea;
- the list controls (search, the status filter, pages) and the data table: a header that stays
  in view, a row that answers the pointer, numbers right-aligned in tabular figures, a status
  badge with an icon and a word as well as a colour;
- the page header (title, subtitle, status, actions, the way back) and the details list;
- ProblemAlert, the conflict notice, the inline confirmation, the reason form, the record picker,
  and the audit trail drawn as a timeline;
- loading as a skeleton of the layout that is coming (aria-busy), never a bare spinner; an empty
  state with a small drawing of our own and the next action the role may take; the no-access and
  not-found pages and the "Users and settings" placeholder.
One mapping in one file gives every status of every document its colour token, its icon and its
word; a test holds it complete against the statuses of the API's types.

Then the screens of 7.2 as the proof of the pattern: patients, payers and service items, their
lists, records and forms.

A specimen page, in Development builds only (not in the production bundle, not in the
navigation, the one new route): every shared component in every state, in both themes.

No change of behaviour: no change of the API, of a policy or of what a screen sends, and no other
change of a route. A list keeps its search, status and page in the address. The names and roles
the tests and the journey of 8.2 use stay: the navigation's labels, "Sign out", the fields'
labels, the headings.
Tests: the navigation still follows the matrix for each role; the drawer by keyboard; the rail's
choice remembered; the document title and the focus after a navigation; a skeleton is busy and
then gone; a table at 390 px; the status mapping complete; every existing test unweakened. In the
browser: each of these screens in both themes at 1440 and 390 px with no sideways scroll, no
console error and no content security policy refusal; nothing animates with reduced motion.
Add screenshots (the shell, a list, a record, a form, the specimen page; both themes) to
docs/ui/. Write ADR 0048.
Done when: I walk patients, payers and service items by keyboard alone in both themes, at a
desktop and a phone width, and every list, record and form is of a piece with the sign-in page.
```

**Owner's decisions for 9.2, recorded 2026-11-24 (ADR 0048):** the plan was approved: tables scroll inside their own
frame with the first column pinned (rows folding into cards was offered); and, asked for by the owner during the plan, the
theme becomes a modern toggle, a pill of three with Light, Dark and System (a two-way switch was offered, which would have
lost System). Built as decided; fields with ₹ and %, the audit timeline and the one status mapping move to 9.3.

#### 9.3 The money screens (shared components, bills, credit notes and adjustments)

As approved on 2026-11-24 and then split by the owner: this prompt builds the shared components and the screens of bills,
credit notes and adjustments; 9.3b, below, the payments, refunds, the payer statement and the inbox. The wording below is
the prompt as given; its "screens" list is shared by the two.

```
Read ADRs 0047 and 0048 and "The bar of Phase 9" in docs/build-prompts.md; ADRs 0040 to 0042 for
what the bill, cashier, credit note, adjustment and inbox screens do today.

Show me the plan first: for each screen below, its layout at 1440 and 390 px and what the eye
meets first; how money is set; any new token or shared component; any package you want (ask).

Money, on every screen: tabular figures, the rupee sign, Indian grouping, right-aligned in
tables. A total stands out from its parts. A credit or a negative amount carries a sign and a
word, never a colour alone. An estimate is marked as one until the server's figure replaces it.

The screens:
- bills: the list (number, patient, payer, date, total, balance, status); the bill's page (its
  number and status at the top, the parties, the lines, the totals and the balance in a summary
  card, the banner while it waits for approval, its credit notes and adjustments, its history as
  the timeline); the draft editor (the pickers, the lines as a grid with each line's total, a
  totals card that stays in view while the lines scroll, post and void with their
  confirmations);
- payments and refunds: the lists; recording a payment; the payment's page with its allocations
  and reversal requests; allocating (the payer's open bills oldest first, "Apply oldest first",
  a meter of what is left to apply that warns before a cap is passed); a refund from its source;
- the payer statement: what is owed, the unapplied cash, the credit and the net as four figures
  at the top, then the bills, payments and credit notes with their actions;
- credit notes and adjustments: the lists, the pages, the editors;
- the checker's inbox: each request a card (its kind with an icon, the document, the maker, the
  amount, how long it has waited), Review opening in place with the document's facts and the
  decision; the maker's own request shown as not theirs to decide.

Motion: a line that is added or removed eases in or out; a total that changes is marked for a
moment (a figure itself is never animated); every action button shows that it is pending; a
decided request leaves the inbox; the first field the API refused comes into view.

No change of behaviour: no change of the API, of a route, of a policy or of what a screen sends,
and no figure is computed differently: lineMath.ts, money.ts and their shared cases are not
touched.
Tests: every existing test unweakened; the totals card and the meter read the figures the
screens show today; every status through the one mapping. In the browser: each of these screens
in both themes at 1440 and 390 px with no sideways scroll, no console error and no content
security policy refusal; nothing animates with reduced motion; the journey of 8.2 passes.
Add screenshots (the bill editor, a posted bill, allocating, the statement, the inbox; both
themes) to docs/ui/. Write ADR 0049.
Done when: the scenario of Phase 4, by a clerk, a cashier and a supervisor in the browser, reads
as one product from the sign-in to the approved refund, in both themes.
```

**Owner's decisions for 9.3, recorded 2026-11-24 (ADR 0049):** the plan (seven shared components, no new package) was
approved as one prompt and then split, at the owner's word, into 9.3 (the shared components, bills, credit notes and
adjustments) and 9.3b (payments, refunds, the statement and the inbox). 9.3b amends ADR 0049; 9.4 keeps ADR 0050.

#### 9.3b The cashier's screens and the inbox

```
Read ADRs 0047 to 0049 and "The bar of Phase 9" in docs/build-prompts.md; ADRs 0041 and 0042 for
what the payment, allocation, refund, statement and inbox screens do today.

Show me the plan first: for each screen below, its layout at 1440 and 390 px and what the eye
meets first; anything new beyond the components of 9.3 (Money, the status mapping, TotalsCard,
Banner, the timeline, fields with a unit and a required mark); any package (ask).

The screens, with the components of 9.3:
- payments and refunds: the lists; recording a payment (the amount with its unit, the payer
  beside it); the payment's page with its amount and unapplied cash as figures, its allocations
  and its reversal requests as banners; a refund from its source with the source's available
  amount as a figure;
- allocating, and applying a credit note's credit (the shared ApplyToBills): the payer's open
  bills oldest first, "Apply oldest first", and a meter of what is left to apply that warns in
  words before a cap is passed, reading the figures money.ts already computes;
- the payer statement: what is owed, the unapplied cash, the credit and the net as four figures
  at the top (the net the strongest), then the bills, payments and credit notes with their
  actions;
- the checker's inbox: each request a card (its kind with an icon, the document, the maker, the
  amount, how long it has waited), Review opening in place with the document's facts and the
  decision; the maker's own request shown as not theirs to decide; a decided request leaves.

No change of behaviour: no change of the API, of a route, of a policy or of what a screen sends,
and money.ts and lineMath.ts are not touched. If the inbox's test must find cards as list items
instead of rows, the assertions stay the same and the change is called out.
Tests: every existing test unweakened; the meter's figures and warning; the statement's four
figures against the API's. In the browser: each of these screens in both themes at 1440 and
390 px under nginx's policy, with no sideways scroll, no console error, no layout shift and
nothing animated with reduced motion; the journey of 8.2 passes.
Add screenshots (allocating, the statement, the inbox; both themes) to docs/ui/. Amend ADR 0049.
Done when: the scenario of Phase 4, by a clerk, a cashier and a supervisor in the browser, reads
as one product from the sign-in to the approved refund, in both themes.
```

**Owner's decision for 9.3b, recorded 2026-11-24 (ADR 0049, amendment):** the plan was approved with the inbox as a list of
cards, its test finding list items instead of rows with the same assertions (keeping rows drawn as cards was offered).

#### 9.4 The dashboard, the reports and the finishing pass

```
Read ADRs 0043, 0044 and 0047 to 0049 and "The bar of Phase 9" in docs/build-prompts.md;
docs/ui/dashboard.png (the dashboard's layout reference, ADR 0044); docs/backlog.md, row B21.

Show me the plan first: the dashboard and the report screens at 1440 and 390 px; the budgets
(the JavaScript and the CSS a first visit to the sign-in page loads, compressed) and how the
routes are split; what a printed page looks like; the accessibility checks, and any package for
them (ask).

The dashboard, laid out as docs/ui/dashboard.png and set in the tokens of 9.1: the period picker
as a segmented control; six cards (an icon, the label, the figure in compact money with the exact
amount in the tooltip and for screen readers, the change with an arrow and a word); billing
against collections with a themed grid, tooltip and legend, and the table of its figures; the
ageing donut with its total at the centre; the four panels, each with its own skeleton, empty
state and error. A chart draws itself once.

Tooltips on every figure and chart, one shared component from the tokens: two new ones,
--surface-popover and --border-strong, in both themes and held by the contrast test (neither
exists after 9.2), with the existing --shadow-raised and the motion tokens:
- KPI card figure: the exact amount, the previous period's figure, and the change with its arrow
  and word (the summary's value, previous and changePercent);
- sparkline: the business date (or week) and that point's figure from the server's daily series
  (the trend's daily);
- billing against collections: a vertical crosshair with a dot on each line, the date, billed and
  collected to date, and that day's or week's billed and collected (the trend's points: billed,
  collected, cumulativeBilled, cumulativeCollected);
- ageing donut: the hovered slice thickens and the centre shows that bucket's exact amount and
  name; the tooltip shows the bucket, the amount and the share as the API answers it;
- top payers bars: the payer's outstanding amount, their unapplied cash and their available
  credit (the top-payers answer already carries receivable, unappliedCash, creditAvailable and
  netOutstanding, checked 2026-11-24); never send a request for a tooltip.
Every amount in a tooltip is exact, in Indian grouping with tabular figures; the client computes
nothing. Tooltips open on hover, on keyboard focus and on tap, and close on Escape, on blur and
on a second tap. On the line charts the arrow keys move the crosshair point by point. A tooltip
stays inside the viewport and never covers the pointer. Recharts' Tooltip uses our own content
component, not its default styling. Tooltips fade in on opacity only and appear at once under
reduced motion. Nothing is shown only in a tooltip: each chart has a "Show the figures as a
table" holding the same figures (billing against collections has one since 7.6; the sparklines,
the donut and the top payers get theirs).

The reports: the index as cards, an icon and a line each; on each report a filter bar, the table
with its totals row always in view, and Export CSV showing its progress and its end.

Print: a bill, a payer statement and each report print cleanly on A4: no shell, dark text on
white, the hospital's name and the date at the top, no row cut in two.

The finishing pass, over every screen:
- from 360 to 1,920 px wide, and at 200% zoom: no sideways scroll, nothing clipped, nothing
  overlapping;
- by keyboard alone and by a screen reader's outline: one h1, headings in order, a name on every
  control, focus never lost, a live region for what arrives later; Windows forced colours and
  prefers-contrast still readable;
- routes load on demand (backlog B21), within the budgets, which a test of the build holds;
- a chosen set of screenshots in docs/ui/ (each kind of screen, both themes), and the full set,
  every route in both themes at two widths, as an artifact of the browser suite, not in git.

No change of behaviour: no change of the API, of a route, of a policy or of what a screen sends;
the dashboard's figures and the bytes of a CSV stay as they are.
Tests: every existing test unweakened; the budgets; print (the shell is not printed); each
tooltip's content against the API's figures, open and close by pointer, keyboard, tap and Escape,
the arrow keys on the line chart, both themes, reduced motion. In the
browser: every route in both themes with no console error, no content security policy refusal
and no layout shift, and nothing animated with reduced motion.
Update the README (a screenshot, "a product of ashline"), docs/testing-strategy.md, the runbook
and CLAUDE.md; close backlog row B21. Write ADR 0050 and propose the tag v1.1.0.
Done when: I open every screen in both themes at a desktop and a phone width and none looks
older than the sign-in page; the budgets hold; and the journey of 8.2 passes through nginx on my
personal machine with no content security policy refusal.
```

Merge to `main`; the owner tags `v1.1.0`.
