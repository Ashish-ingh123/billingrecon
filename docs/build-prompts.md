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
| 2.3, 2.4, 3.1, 3.2 and 3.4 (service catalogue, bill drafts, number series, idempotency, event dispatch and audit) | Written on 2026-11-20 on top of the uncommitted 2.2, each pre-checked on .NET 9 with LocalDB only; **not built on .NET 10, not committed**; migrations `0003` to `0007` still to be scaffolded (CLAUDE.md section 9, items 9 to 14) |
| 3.3 Receivables ledger | Written on 2026-11-20 after 3.4, with the owner's decisions (ADR 0005 built note, ADR 0021); pre-checked on .NET 9 with LocalDB only; **not built on .NET 10**; migration `0008_Receivables` still to be scaffolded and completed by hand (`docs/runbook.md` section 8) |
| 3.5 Post a bill | Written on 2026-11-20 with the owner's decisions (ADR 0022); pre-checked on .NET 9 with LocalDB only; **not built on .NET 10**; migration `0009_BillPosting` still to be scaffolded (`docs/runbook.md` section 8) |
| 3.6 Maker-checker approvals | Written on 2026-11-20 with the owner's decisions (ADR 0023); pre-checked on .NET 9 with LocalDB only; **not built on .NET 10**; migration `0010_Approvals` still to be scaffolded (`docs/runbook.md` section 8). The role of the checker is left to 5.3 |
| 4.1 Record a payment | Written on 2026-11-20 with the owner's decisions (CLAUDE.md section 9, item 20); pre-checked on .NET 9 with LocalDB only; **not built on .NET 10**; migration `0011_Payments` still to be scaffolded (`docs/runbook.md` section 8) |
| 4.2 Allocate a payment to bills | Code and tests written on 2026-11-20, pre-checked and documented on 2026-11-21 (ADR 0024, Proposed; CLAUDE.md section 9, item 21); pre-checked on .NET 9 with LocalDB only; **not built on .NET 10**; migration `0012_PaymentAllocations` still to be scaffolded (`docs/runbook.md` section 8) |
| 4.3 Credit notes | Written on 2026-11-21 with the owner's decisions (ADR 0025; CLAUDE.md section 9, item 22); pre-checked on .NET 9 with LocalDB only; **not built on .NET 10**; migration `0013_CreditNotes` still to be scaffolded (`docs/runbook.md` section 8) |
| 4.4 Adjustments | Written on 2026-11-21 with the owner's decisions (ADR 0026; CLAUDE.md section 9, item 23). **Built and tested on .NET 10** (SDK 10.0.404, installed on the office machine that day) in a throwaway copy, with the database tests on LocalDB 2019; not run on SQL Server 2022; migration `0014_Adjustments` still to be brought into the repository (`docs/runbook.md` section 8) |
| 4.5 Reversals | Written on 2026-11-21 with the owner's decisions (ADR 0027; CLAUDE.md section 9, item 24). Built and tested on .NET 10 in a throwaway copy, with the database tests on LocalDB 2019; not run on SQL Server 2022; migration `0015_PaymentReversals` still to be brought into the repository (`docs/runbook.md` section 8). It also corrects what a late decision answers in every approve and reject use case since 3.6 (409, not 500) |
| Everything else in R1 | Not started: reversal of a credit application (4.5b), refunds, users and JWT, reports, frontend, delivery |
| 6.3 Dashboard figures and 7.6 Dashboard (added 2026-11-21, with theming in 7.1) | Not started. 6.3 runs after 6.2, 7.6 after 7.5; the layout reference `docs/ui/dashboard.png` is still to be added; the drift card's trend (none, or a snapshot table and a new migration) is the owner's open decision, asked in 6.3 |

**Verification is deferred (owner, 2026-11-20): the personal-machine run comes later, and `docs/runbook.md` section 8 is the one running list of every command it owes, in order. Meanwhile every prompt from 2.2 on is committed unverified, one commit per prompt. Phase 3 is written to its end (3.1 to 3.6), and 4.1 (record a payment), 4.2 (allocate a payment to bills), 4.3 (credit notes) and 4.4 (adjustments) are written. Since 2026-11-21 the office machine has the .NET 10 SDK: the whole tip of the branch built and passed there on .NET 10 with LocalDB 2019, and only the run on SQL Server 2022 in Docker and the migration files in the repository are still owed. 4.5 (reversals) is written too. Next prompt: 4.5b (reverse an application of credit), then 4.6 (refunds). What was decided for each built prompt is recorded under it below.**

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
This machine cannot build .NET 10 and has no Docker (CLAUDE.md section 2): pre-check in the .NET 9
copy, tell me plainly what was and was not verified, and give me the exact commands to run on my
personal machine.
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

#### 4.6 Refunds

```
Read BR-307, BR-308, US-34, docs/domain-model.md section 4.7.
Build Refund from one source (a payment's unapplied amount or a credit note's available amount):
cap checked at posting, method and reference rules of BR-301, active payer, maker-checker, gapless
number, Dr UnappliedCash or CreditAvailable / Cr Cash. Not reversible. Migration 0016_Refunds (0017 if 4.5b is built first).
Then write one end-to-end scenario test that walks bill -> part payment -> credit note ->
adjustment -> allocation reversal -> refund and asserts zero drift and gapless series at the end.
Done when: US-34 and the scenario test pass.
```

Merge `phase-4-payments` to `main`.

### Phase 5. Identity and security (branch `phase-5-identity`)

#### 5.1 Users and roles

```
Read BR-601, US-01, docs/domain-model.md section 4.12, docs/database.md section 3.1.
Build User with roles (TenantAdmin, Supervisor, BillingClerk, Cashier, Viewer): e-mail unique per
tenant, enable/disable, password hashed with a slow salted BCL algorithm, admin password reset.
Users and Roles tables, the development user row, and the foreign keys from CreatedByUserId and
UpdatedByUserId that were waiting for it. Audited. Migration 0017_Users (0018 if 4.5b is built first).
Done when: US-01 passes.
```

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

#### 5.3 Role permissions and tenant settings

```
Read the permission matrix in docs/requirements.md section 1, BR-603, BR-607, US-03.
Apply the matrix as named policies on every endpoint, with one table-driven test that walks every
endpoint with every role. The checker must hold Supervisor or TenantAdmin. Build the tenant
settings endpoints (approval threshold, back-date window, default due days, default prefixes;
prefix change is maker-checker, BR-802), audited.
Done when: the matrix test passes and US-03 passes.
```

#### 5.4 Database logins and hardening

```
Read docs/database.md section 7 and the open review findings in CLAUDE.md section 9 item 6.
Create the application and migrator logins with their grants (no DELETE on financial tables,
insert-only audit and ledger, SELECT on __EFMigrationsHistory); run the database tests as the
application login. TLS and HSTS, allowed hosts, server header removed, rate limit on login, CORS
for the frontend origin. Write docs/security.md in full, with a threat model.
Done when: a test proves the application login cannot delete a bill or update an audit row.
```

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

#### 6.2 Reconciliation and audit trail

```
Read US-41, US-43, docs/database.md section 5.6. Expose the reconciliation report (cache versus
ledger, drift rows) and the audit trail of any document. Add a test helper that asserts zero
drift, and call it at the end of every financial integration test that does not already do so.
Complete docs/api.md with every endpoint and the full error-code catalogue.
Done when: US-41 and US-43 pass and a seeded drift is reported.
```

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

### Phase 7. Frontend (branch `phase-7-frontend`)

Node 20.19 or newer is needed; the office machine has Node 6 (CLAUDE.md section 2).

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
Done when: I can sign in and see a role-appropriate empty shell, and the empty shell switches
between the three settings without a reload and without a flash on a hard refresh.
```

#### 7.2 Patients, payers and catalogue screens

```
Build list, create, edit, deactivate and reactivate screens for patients, payers and service
items, with paging and search. Send If-Match on every change and show a clear "someone else
changed this record" message on 409. Component tests for the forms.
```

#### 7.3 Bill screens

```
Build the bill draft editor (lines from the catalogue or free text, live totals that mirror
BR-202 and BR-203 but always display the server's figures after save), void, post with a generated
Idempotency-Key that is reused on retry, and the bill view with status, balance and audit trail.
```

#### 7.4 Cashier screens

```
Build record payment, allocate to bills (outstanding bills of the payer, caps enforced in the
form and by the server), request reversal, draft refund, and the payer statement (outstanding,
unapplied cash, available credit).
```

#### 7.5 Approvals and reports

```
Build the checker's inbox (approve, reject with comment; hidden from the maker), credit note and
adjustment drafting, and the six R1 reports with date filters and CSV export.
Done when: the Phase 4 scenario can be performed entirely in the browser by three users.
```

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
  reconciliation exceptions, each with a "view all" link to the screen built in 7.4 or 7.5.
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

Merge to `main`.

### Phase 8. Delivery (branch `phase-8-delivery`)

#### 8.1 One-command local run

```
Add api and frontend services to docker-compose.yml (frontend served by nginx, API behind it),
a migration step that runs as the migrator login, and an opt-in demo seeder: one tenant, five
users (one per role), patients, payers, a catalogue and a week of bills and payments.
Done when: docker compose up on a clean machine gives a working, signed-in demo.
```

#### 8.2 Performance and browser tests

```
Read the performance row of docs/requirements.md section 5. Write a load test for bill posting
and payment recording at 50 concurrent users, report p95 and number-allocation time, and fix what
misses the target. Add a small Playwright suite for the main journey (ask before adding it).
Done when: p95 is under 500 ms and reconciliation shows zero drift after the load run.
```

#### 8.3 Release R1

```
Finish docs/runbook.md (migrations outside Development, backup and restore, incident steps,
reconciliation drift procedure), bring README, CLAUDE.md and every ADR status up to date, close or
re-file every item of CLAUDE.md section 9, walk every user story US-01 to US-43 and list the test
that proves it, then tag v1.0.0.
Done when: the traceability table has no empty row.
```
