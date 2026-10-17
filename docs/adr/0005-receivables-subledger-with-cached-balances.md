# ADR 0005: Receivables subledger with cached balances

- **Status:** Proposed (accept on review of the R1 design documents)
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
