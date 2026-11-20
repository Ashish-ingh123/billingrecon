# ADR 0005: Receivables subledger with cached balances

- **Status:** Accepted since its implementation (Prompt 3.3, 2026-11-20); see "Built 2026-11-20" at the end for what the build decided
- **Date:** 2026-09-29
- **Deciders:** project owner, Claude Code

## Context

Every screen and report in BillingRecon shows a balance: what a bill still needs, how much of a
payment is unapplied, what a payer owes net. Those figures are read thousands of times a day and
must be exact to the paisa and explainable to an auditor ("why does this payer owe ₹2,000?").

Two naive designs fail. Keeping only cached balance columns is fast, but when a balance is wrong
there is no record of how it became wrong, and nothing detects it. Keeping only documents and
recomputing balances by summing bills, allocations, credit notes and adjustments on every read is
exact, but every list screen becomes a multi-table aggregation and there is still no double-entry
discipline that catches an unbalanced posting.

A full general ledger (chart of accounts, journals, periods, GST ledgers, trial balance, closing) is
what the hospital's accounting package (Tally, SAP, Zoho) already does; BillingRecon feeds it, it
does not replace it. CLAUDE.md rules 3 and 4 require ledger lines in the same transaction and
caches that always equal the ledger.

## Decision

We will keep a **double-entry receivables subledger** as the single source of truth for balances,
with **cached balance columns** derived from it in the same transaction, and a **reconciliation
query** that proves the two agree.

- **Six accounts** (BR-403): three *subject* accounts with a per-document balance, `Receivable`
  (subject: bill, debit-normal), `UnappliedCash` (subject: payment, credit-normal),
  `CreditAvailable` (subject: credit note, credit-normal); and three *control* accounts with no
  subject, `Revenue`, `Cash`, `Adjustments`, whose only job is to make each posting balance and to
  let R5 export journals to the accounting package.
- **Tables** `LedgerPostings` (one per financial event: kind, document, payer, business date,
  UTC instant, `ReversalOfPostingId`) and `LedgerEntries` (lines with `Debit` or `Credit`, never
  both, never negative). Both are append-only; the application login has no UPDATE or DELETE on
  them (BR-402, `docs/database.md` §7).
- **Posting templates** are fixed (`docs/database.md` §5.3). An allocation is the paired entry
  `Dr UnappliedCash(payment) / Cr Receivable(bill)`; a reversal is the mirror pair referencing the
  original posting. Only `IReceivablesLedger.PostAsync` writes the ledger, and it asserts
  `Σ Debit = Σ Credit` before inserting (BR-401).
- **Caches**: `Bills.BalanceDue`, `Payments.UnappliedAmount`, `CreditNotes.AvailableAmount` and the
  `PayerBalances` row (`Receivable`, `UnappliedCash`, `CreditAvailable`, computed `NetOutstanding`).
  They are updated in the same transaction as the ledger entries, by the same service, from the
  same amounts (BR-404). `PayerBalances` is updated with atomic set-based increments so concurrent
  postings for one payer serialise on a row lock rather than failing a rowversion check.
- **Identities** (`docs/database.md` §5.5) are asserted by integration tests after every scenario
  and exposed as the reconciliation report (BR-901). Drift is a Sev-1 defect; caches are never
  edited by hand, and a rebuild is a migration that recomputes from the ledger and is audited.
- CHECK constraints (`BalanceDue BETWEEN 0 AND Total`, one-sided entries, subject rules) are the last
  line of defence, not the first.

## Alternatives

- **Cached balances only, no ledger.** Cheapest to build; rejected because drift is undetectable
  and the history of a balance is unrecoverable, which fails the "ledger-grade" promise.
- **Ledger only, balances computed on read.** Exact, but list screens (outstanding by payer, open
  bills) would aggregate the entry table on every request; under load the reads contend with the
  posting writes. Caches cost one extra write per posting and make reads O(1).
- **A full general ledger inside BillingRecon.** Chart of accounts, periods, closing, GST ledgers and
  statutory reports would double R1's scope and duplicate the hospital's accounting system. The
  three control accounts are exactly what a later journal export needs.
- **A single signed `Amount` column instead of `Debit`/`Credit`.** Compact, but the normal-side
  convention becomes implicit, exports to accounting systems expect Dr/Cr, and the "never both,
  never negative" CHECK is a stronger invariant than "non-zero".
- **Database triggers maintaining the caches.** Hides financial logic where the Domain tests cannot
  reach it and makes the ledger service's transaction boundary invisible in code.
- **Event sourcing the aggregates.** The ledger already is the append-only event log for money;
  event-sourcing the documents on top would add rebuild machinery the team has not operated in
  production, for no additional guarantee.

## Consequences

- Every posting writes one `LedgerPostings` row, two or more `LedgerEntries` rows, and one to three
  cache updates. Storage grows with activity and is never pruned; that is the point.
- Every balance the UI shows can be explained by listing the entries for its subject, and every
  explanation is testable by a query that returns zero rows.
- The `PayerBalances` row is a hot spot for a busy payer (a large guarantor); the atomic-increment
  update keeps that to a short row lock per posting.
- Reporting (R1) and ageing (R3) read the caches; the reconciliation report reads the ledger.
- Exporting journals to an external accounting package (R5) is a projection over `LedgerEntries`
  grouped by control account and business date; no schema change is needed.

## Built 2026-11-20 (Prompt 3.3)

Written on the office machine and pre-checked on .NET 9 with LocalDB 2019; not yet run on .NET 10 or SQL Server 2022.
The cross-cutting rules this needed are in ADR 0021. What the build decided about the ledger itself:

- **The port takes a `PostingTemplate`** (Application, Receivables): kind, document, payer, business date,
  description and lines. Each financial event gets a named factory on it with the slice that builds its document
  (the first is the bill posting of Prompt 3.5); a business module never spells out accounts and sides. The
  constructor is public for those factories and for the ledger's tests.
- **`LedgerPosting.Create` checks the invariants and throws** on a template that breaks them (fewer than two
  lines, an amount on both sides or none, a subject where none belongs or the wrong kind, debits that differ from
  credits): such a template is a bug in the code that built it.
- **A balance may not go below zero (BR-405), and that is a result, not an exception.** Before a posting that
  lowers the receivable of a bill, the unapplied cash of a payment or the available credit of a credit note, the
  ledger adds up that subject's entries, those of the current request included, and answers
  `ledger_balance_negative` (422) if the posting would take it below zero. Nothing is written. The document checks
  its own cap first, under its row version; this is the ledger's second look.
- **The payer's balances move at once, under a lock.** `PostAsync` reads the row with `UPDLOCK, ROWLOCK` and
  changes it in one batch, in the transaction of the request, which it opens if nothing has opened it; the unit of
  work commits it, as with a document number (ADR 0008). A second posting for the same payer waits. A payer
  without the row is a fault. The posting row itself is inserted by the save of the unit of work.
- **Lock order for every posting use case:** the number series, then the balances of the payer, both before the
  save. Prompt 3.5 writes this up as its own ADR, with the place of the document's row.
- **The row of balances is opened by the use case that creates the payer**, through the Receivables port
  `IPayerAccounts`, in the same commit. Not by a subscriber to the payer's event: that would make Receivables name
  an event of Payers, and the audit trail is the only subscriber (ADR 0020). The owner decided this on 2026-11-20.
- **Differences from the first design, each in `docs/database.md` section 3.7:** `PayerBalances` has no
  `LastPostingId` (the balances change before the posting row exists, so the key could not hold);
  `LedgerPostings.PostedByUserId` is nullable with no key until `Users` exists; the unique index of the entries
  leads with the tenant.
- **The reconciliation covers the ledger's own identities so far:** unbalanced postings, the three cached balances
  of a payer against the ledger, a payer with entries and no row, a subject below zero. A document's identities
  (a bill's balance due against its receivable, a posted bill without its posting) join with the slice that posts
  the document: the columns they read do not exist before.
- **Not built, on purpose:** an endpoint for the reconciliation (Prompt 6.2); reads of a payer's balances for the
  API (6.1); the posting templates of the documents that do not exist yet.
