# BillingRecon

A multi-tenant billing, payments and accounts-receivable platform for hospitals and clinics in India, built as a .NET
modular monolith with a React front end. The core promise is ledger-grade financial correctness: gapless document
numbering, immutable postings, a double-entry receivables subledger with cached balances that a reconciliation holds to
it, maker-checker approval and an append-only audit trail.

**BillingRecon by ashline**, a product of [ashline](https://ashish-singh-ai-studio.vercel.app/).

![The dashboard of BillingRecon: six figure cards, billing against collections, the ageing of what is owed, and the
recent payments, top payers, pending approvals and reconciliation panels](docs/ui/dashboard-built.png)

> **Status: Release 1 (`v1.0.0`, 2026-11-24).** Every user story of R1 is built and proven by tests
> ([docs/traceability.md](docs/traceability.md)): patients and self-pay payers, the service catalogue, bills and credit
> notes, payments and their allocation, reversals, refunds and adjustments under maker-checker, the receivables ledger
> and its reconciliation, users, roles and sign-in, the reports and the dashboard, and the screens for all of it.
> Built and tested on .NET 10 with SQL Server LocalDB 2019 on the development machine; the whole test suite was reported
> green with SQL Server 2022 in Docker on 2026-11-24. What is still to be run on a machine with Docker is listed in
> [docs/runbook.md](docs/runbook.md), section 8; what comes after R1 is in [docs/backlog.md](docs/backlog.md).
> **Phase 9, the interface (`v1.1.0` proposed, 2026-11-24):** every screen redrawn to the level of the sign-in page in
> a light and a dark theme, with no change of behaviour: the brand and the design tokens, the shell, the money screens,
> the cashier's screens and the inbox, the dashboard with a tooltip on every figure and chart, the reports, print on A4,
> and a browser suite over every route with axe's WCAG 2.2 AA checks ([docs/build-prompts.md](docs/build-prompts.md),
> ADRs 0047 to 0050; screenshots in [docs/ui](docs/ui)).
> **Phases 10 and 11, toward go-live (2026-11-24 to 2026-11-27, not yet merged to `main`):** users and settings with a
> forced password change, onboarding a hospital by an operator's command, the import of a hospital's records and
> opening balances, printed bills, receipts and refund vouchers on the hospital's letterhead with its logo and
> signature, the production deployment, sign-out on the server, live search, an independent review and its fixes;
> then the owner's eight stages: search on the document lists, the cashier's shift with the day-end closing, a second
> step at sign-in for administrators and supervisors (TOTP) and a forgotten password reset by mail, and the
> accountant's export for Tally; then operations (the nightly reconciliation of every hospital, alerts by mail, a
> watchdog on the server, on-call) and go-live readiness (ADRs 0051 to 0062; `v1.2.0` proposed). **Before a first
> hospital goes live:** the merge to `main` and the first CI run, the checks of [docs/runbook.md](docs/runbook.md)
> section 8 on a machine with Docker and on staging, and the twenty sign-offs of [docs/go-live.md](docs/go-live.md),
> which also holds the acceptance script, the cut-over plan and the training notes of each role.
> Start with [CLAUDE.md](CLAUDE.md) (stack, architecture, the twelve financial rules, the working agreement).

## Release plan

| Release | Theme |
|---|---|
| **R1** | MVP: done |
| R2 | Reconciliation with bank and gateway statements |
| R3 | Recovery: ageing, dunning, disputes, write-offs |
| R4 | Insurance and corporate payers |
| R5 | Maturity: hardening, observability, backup drills |

## Layout

```
BillingRecon.sln, Directory.Build.props, Directory.Packages.props, global.json, .editorconfig
docker-compose.yml            the whole application on one machine: SQL Server, Seq, migrate, api, frontend (nginx), demo-seed
deploy/                       migrate/ (the migration step), demo/ (the demo hospital), load/ (the load test), backup/
src/
  BillingRecon.Domain/          entities, value objects, Result and Error, invariants (no dependencies)
  BillingRecon.Application/     use cases, validators, ports, DTOs
  BillingRecon.Infrastructure/  EF Core context and migrations, the ledger, numbering, idempotency, audit, reports
  BillingRecon.Api/             controllers, authentication and permissions, the error contract, transport security
tests/
  BillingRecon.Domain.Tests/  BillingRecon.Application.Tests/
  BillingRecon.IntegrationTests/  BillingRecon.ArchitectureTests/
frontend/                     React + TypeScript + Vite; e2e/ is the browser journey (Playwright)
docs/                         requirements, domain model, database, API, security, testing, runbook, traceability, ADRs
```

Each source project has one folder per module: Common, Payers, Billing, Payments, Receivables, Adjustments, Reporting,
Identity, Audit.

## Run it

With Docker: copy `.env.example` to `.env`, fill it in (the licence acceptances, the passwords, the signing key, and
`DEMO_ENABLED=true` with `DEMO_PASSWORD` for the demo hospital), then

```bash
docker compose up --build
```

and open https://localhost: hospital code `demo`, `DEMO_PASSWORD`, and one of `admin@demo.local`,
`supervisor@demo.local`, `clerk@demo.local`, `cashier@demo.local`, `viewer@demo.local`
([docs/runbook.md](docs/runbook.md), section 13).

## Build and test

Prerequisites: a .NET 10 SDK (`global.json` accepts 10.0.4xx), Docker for the database tests (they start SQL Server
2022 themselves), Node 22.22 or later for the frontend.

```bash
dotnet tool restore
dotnet build
dotnet test
```

```bash
cd frontend
npm ci
npm test
```

Every command, the CI gates and the procedures (migrations, logins, backup and restore, incidents, the load test and the
browser journey) are in [docs/runbook.md](docs/runbook.md). Continuous integration is `.github/workflows/ci.yml`.

## Documentation

- [docs/requirements.md](docs/requirements.md): actors, scope, the business rules (BR-nnn), user stories, NFRs
- [docs/traceability.md](docs/traceability.md): each user story and the tests that prove it
- [docs/domain-model.md](docs/domain-model.md), [docs/database.md](docs/database.md), [docs/api.md](docs/api.md)
- [docs/security.md](docs/security.md): authentication, permissions, tenancy, transport, the threat model
- [docs/testing-strategy.md](docs/testing-strategy.md), [docs/runbook.md](docs/runbook.md)
- [docs/backlog.md](docs/backlog.md): open questions and follow-ups
- [docs/build-prompts.md](docs/build-prompts.md): how it was built, prompt by prompt, and its status table
- [docs/build-history.md](docs/build-history.md): what was decided for each built prompt
- [docs/adr/](docs/adr/): Architecture Decision Records, 0000 to 0062 (0056 is kept for Prompt 10.6)
- [docs/go-live.md](docs/go-live.md): what must be signed off, by whom and when, before a hospital goes live; the
  acceptance script, the cut-over plan, hypercare and the training notes
