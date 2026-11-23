# ADR 0003: Modular monolith and module boundaries

- **Status:** Proposed (accept on review of the R1 design documents)
- **Date:** 2026-09-29
- **Deciders:** project owner, Claude Code

## Context

BillingRecon is a financial system built by a small team over five releases. Its defining
requirement is transactional correctness across concerns that belong to different modules: posting
a bill (Billing) must write ledger entries (Receivables), update a payer's cached balance
(Receivables), consume a gapless number (Common) and write audit records (Audit) in **one** database
transaction (BR-007, BR-401, BR-404). An allocation changes a payment (Payments) and a bill (Billing)
together (BR-303).

ADR 0002 fixed the physical layout: four layer projects, nine module folders and namespaces, and
architecture tests. It did not record the architectural style itself, why modules are not services,
or how a module may use another module. Those rules are in `CLAUDE.md` §3 and need an ADR so that
they are not re-litigated when the first cross-module feature is built.

## Decision

We will build BillingRecon as a **modular monolith**:

- **One deployable** ASP.NET Core API process and **one SQL Server database**. All module
  interaction is an in-process method call inside the same request and the same transaction.
- **Nine modules** (`Common`, `Payers`, `Billing`, `Payments`, `Receivables`, `Adjustments`,
  `Reporting`, `Identity`, `Audit`) exist as namespaces inside each of the four layer projects, per
  ADR 0002. Each business module **owns its tables**: `Billing` owns `Bills`, `BillLines`,
  `CreditNotes`, …; `Payments` owns `Payments`, `PaymentAllocations`, `Refunds`; `Receivables` owns
  `LedgerPostings`, `LedgerEntries`, `PayerBalances` (`docs/database.md` §3).
- **Coupling is through contracts only.** A module may reference another module's *Application*
  contracts: port interfaces (`IReceivablesLedger`, `IBillReadService`), DTOs and domain events. It
  must never reference another module's entities, repositories or `DbSet`s. `Common`, `Identity`
  and `Audit` are cross-cutting and may be used by every module but reference no business module.
- **One unit of work per use case, spanning modules.** A use case in one module calls the ports of
  other modules and commits everything with a single `IUnitOfWork.CommitAsync`. There are no
  per-module transactions, no compensating actions and no eventual consistency between modules.
- **Domain events are in-process and synchronous**, dispatched inside the unit of work before
  `SaveChanges` so subscribers (audit, projections) join the transaction. No outbox in R1.
- **Banned:** message brokers, distributed caches and microservices (CLAUDE.md §2). Lifting any of
  these needs a new ADR.
- **Enforced by tests** in `tests/BillingRecon.ArchitectureTests`: the layer rule, the module rule
  (no business module depends on another module's `Domain.*` or `Infrastructure.*` namespace, only
  on `Application.*` contracts), and cross-cutting modules not depending on business modules.

## Alternatives

- **Microservices, one per module.** Posting a bill would need a distributed transaction or a saga
  across Billing, Receivables and Audit, which contradicts BR-007 and BR-404 (cache equals ledger
  after every commit) and adds infrastructure a small team cannot operate. Nothing in the R1–R5 plan
  needs independent deployment or scaling of a module.
- **A plain layered monolith without modules.** Simplest, but with nine business areas the code
  becomes one namespace with no ownership; every table is reachable from everywhere and the
  "contracts only" rule cannot be tested.
- **One project per module per layer.** Strongest compile-time isolation; rejected in ADR 0002 for
  build cost (36 projects) and awkward cross-module contracts. Remains the fallback if the
  namespace-based rule proves too weak.
- **Vertical-slice folders without a Domain layer.** Fast for CRUD, but the financial invariants
  (BR-2xx–5xx) belong in a dependency-free Domain that the architecture tests can protect; slices
  tend to put them in handlers next to EF code.
- **Event-driven modules with an outbox from day one.** Adds asynchrony and a second consistency
  model for no consumer that exists in R1. An outbox can be added in R5 for external integrations
  (accounting export, notifications) without changing module boundaries.

## Consequences

- Cross-module postings are ordinary method calls inside one transaction; correctness rules are
  simple to state and to test.
- Scaling is vertical (one API can still run several instances against one database). The database
  is the shared resource; the number-series and payer-balance hot rows (ADR 0005, ADR 0008) are the
  known contention points.
- Module discipline rests on architecture tests and code review, not on the compiler. A test that
  starts failing is fixed by adding a contract, never by relaxing the rule.
- Because coupling is through explicit contracts, a module could later be extracted into its own
  process by replacing a port implementation, but that is not a goal.
- Every module folder currently holds a README placeholder (ADR 0002); the first real type in a
  module replaces it.

## Amendment 2026-10-17: the module rule, precisely, and its exceptions

Prompt 1.4 asked for the module rule to be written out with its allowed exceptions, and the review
of 2026-10-03 (CLAUDE.md section 9, item 6.1) had found that the architecture tests as first written
rejected parts of the documented design. The rule, as `tests/BillingRecon.ArchitectureTests/ModuleDependencyTests.cs`
now checks it:

1. A business module (`Payers`, `Billing`, `Payments`, `Receivables`, `Adjustments`, `Reporting`)
   depends on another business module only through that module's `BillingRecon.Application.<Module>`
   namespace: port interfaces, DTOs, commands and queries. The other module's `Domain`,
   `Infrastructure` and `Api` namespaces are off limits, from every layer of the depending module.
2. The `Domain` of a business module depends on no other business module at all.
3. A cross-cutting module (`Common`, `Identity`, `Audit`) depends on no business module.
4. Every type of the production code lives in `BillingRecon.<Layer>` or
   `BillingRecon.<Layer>.<Module>` (sub-namespaces below the module are fine), for a known layer and
   a known module.

**Allowed exceptions**, each listed in the test with its reason, so that adding one is a visible
decision and never a relaxation of the rule:

| Depending namespace | May depend on | Why |
|---|---|---|
| `BillingRecon.Application.Payments` | `BillingRecon.Domain.Billing` | Allocating a payment and reversing an allocation command the `Payment` and the `Bill` in one transaction (`docs/domain-model.md` section 6). |
| `BillingRecon.Application.Adjustments` | `BillingRecon.Domain.Billing` | Posting an adjustment commands the `Adjustment` and the `Bill` in one transaction (section 6). |

Credit notes command a `Bill` too, but live in `Billing` themselves and need no exception. The test
cannot tell which type a dependency reaches for, so an exception allows the whole target namespace;
review keeps it to the aggregate the reason names.

**Domain events need no exception.** Events are records in `BillingRecon.Domain.<Module>` and have
one subscriber, `Audit`, which handles `IDomainEvent` generically and so depends on `Domain.Common`
only. An effect in another module is an explicit port call from the use case, as the ledger posting
already is: the Payers use case creates the `PayerBalance` row through a Receivables port, and the
Billing use case opens an `ApprovalRequest` through the approval port of `Common`, rather than
Receivables or Common subscribing to events of business modules. `docs/domain-model.md` section 8 is
corrected accordingly. The approval *handlers* live in the module that owns the document (Billing
approves bills, Adjustments approves adjustments, and so on); only the `ApprovalRequest` aggregate
and its port are in `Common`.

**Aggregates take ids, not each other.** The handler calls `payment.Allocate(bill.Id, amount)` and
`bill.ApplyCredit(amount)`; `Domain.Payments` never references `Domain.Billing`, which keeps rule 2
intact and the exceptions above at the Application layer.

## Amendment 2026-11-20: a foreign key across modules (accepted by the owner, 2026-11-20)

Prompt 2.4 built the first table of one business module that refers to tables of another: `Bills` has
`FK_Bills_PatientId` and `FK_Bills_PayerId` (`docs/database.md` section 3.3). Entity Framework declares a foreign
key by naming the entity type at its other end, so `BillingRecon.Infrastructure.Billing.BillConfiguration` names
`Patient` and `Payer`, which rule 1 forbids. The architecture test caught it, as it should.

**Decision.** A third allowed exception, listed in the test with its reason:

| Depending namespace | May depend on | Why |
|---|---|---|
| `BillingRecon.Infrastructure.Billing` | `BillingRecon.Domain.Payers` | `BillConfiguration` names `Patient` and `Payer` to declare the two foreign keys of `Bills`, and uses nothing else of them. |

As with the other exceptions, the test cannot tell which type a dependency reaches for, so it allows the whole
namespace and review keeps it to the two `HasOne<...>()` calls. Everything else in Billing still reaches Payers
through its Application contracts only: the use cases ask `IBillableParties` whether a patient may be billed and
a payer may be given the bill, and never read a patient, a payer, their statuses or their errors.

**Alternatives considered.**

- *No foreign key in the database.* Rejected: the use case checks the two ids, but the database is the last line
  of defence, and every other reference in the schema has its key.
- *Name the entity types by string*, so that nothing compiles against them. Tried and rejected: it hides the same
  dependency from the compiler, and the architecture test finds it anyway.
- *A convention of the context* that gives every typed-id column a foreign key to the table whose key has that
  type, so that no module names another's entity. It would remove this exception and the ones to come (payments,
  credit notes, adjustments and refunds all refer to payers and bills), at the price of a convention that also
  reaches columns that must not have a key. Not built; it is the alternative to weigh if these exceptions multiply.

The owner accepted this exception on 2026-11-20. The same question returns with every later table that refers to a
patient, a payer or a bill: each one is a new row in this table, decided then, or the convention above replaces them
all.

## Amendment 2026-11-23: Reporting reads every business module (accepted by the owner, 2026-11-23)

Prompt 6.1 built the reports of BR-901, which join what several modules own (a bill, its payer, its ledger postings; a
request and its document). `docs/domain-model.md` section 2 has always given Reporting that role: it "reads any module's
tables through query services; never writes".

**Decision.** Five more allowed exceptions, listed in the test with their reason: `BillingRecon.Infrastructure.Reporting`
may depend on `BillingRecon.Domain.Payers`, `.Billing`, `.Payments`, `.Receivables` and `.Adjustments`, to read their
entities through the context, with the tenant filter. No other layer of Reporting has an exception. In exchange,
`ReportingConventionTests` keeps Reporting from writing: no port that writes, no repository, no call that changes or saves
an entity, every set read untracked, only GET actions (ADR 0036).
