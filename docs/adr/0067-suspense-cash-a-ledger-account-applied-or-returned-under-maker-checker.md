# ADR 0067: Suspense cash: a ledger account, applied to a payer or returned under maker-checker

- **Status:** Accepted as design on 2026-11-28 (Prompt 13.0, approved "as recommended"). Built by Prompt 13.4, which
  adds its built note and the migration.
- **Date:** 2026-11-28
- **Deciders:** project owner, Claude Code
- **Rules:** BR-1014 to BR-1018; BR-403, BR-404 and BR-405 as amended
- **Amends:** ADR 0005 (a new account, three templates, identities 9, 13, 14), ADR 0056 (the nightly reconciliation
  checks the new identities), ADR 0062 (the export names a `Suspense` ledger)

## Context

Money reaches the bank with a reference like "NEFT N3341 KUMAR" and nobody at the hospital recorded it. It belongs to
someone, so the hospital owes it either to a bill or back to the sender; until it knows, it must not vanish and must
not be counted as anyone's payment. R1 has no place for money without a payer: every `UnappliedCash` entry names a
payer and a payment.

## Decision

1. **A seventh subject account, `Suspense`**, credit-normal, whose subject is a **suspense item** and which has no
   payer. `CK_LedgerEntries_Subject` gains its branch. `PayerBalances` is untouched: suspense is no payer's.
2. **Recording** from an `In` line of a bank or gateway channel: a suspense item numbered in its own gapless series
   (`SUS/YY-YY/NNNNNN`, document type `SuspenseItem`), dated the line's date within BR-205, posted
   `Dr Cash / Cr Suspense(item)`, matched to the line in the same transaction. **No checker:** the money did arrive.
3. **Applying** to a payer, all or part: maker-checker (BR-603, BR-604), the approval is the posting (BR-606). It
   creates a payment of the payer in the payment series, with the line's method and reference and the origin
   `Suspense`, posted `Dr Suspense(item) / Cr UnappliedCash(payment)`. The payment is then allocated like any other.
   Over the two documents the ledger says what a counter payment says, `Dr Cash / Cr UnappliedCash`.
4. **Returning** to the sender: maker-checker; a suspense return in its own series (`SRN`), `Dr Suspense(item) /
   Cr Cash`, never by cash; its `Out` line then matches it.
5. **Never reversed.** A payment from suspense that is reversed (BR-306) gives its money back to suspense:
   allocations first, then `Dr UnappliedCash(payment) / Cr Suspense(item)`. So money from an unknown sender cannot leave
   the books by a reversal.
6. **Identities:** each item's `Remaining` equals its `Suspense` balance (13); the account's balance equals items less
   applications plus reversed suspense payments less returns (14); the trial balance includes `Suspense` (9). The
   nightly reconciliation (ADR 0056) checks them; the accountant's export (ADR 0062) gains a named `Suspense` ledger,
   and a period using it without the name refuses the file as for any ledger.
7. **Reports and the dashboard:** suspense by age, an item open more than 30 days among the exceptions; "collected"
   (BR-902) stays the Cash debits of payments, and suspense is a figure of its own until applied.

## Consequences

- Suspense is visible and ages, so it gets cleared instead of sitting in a spreadsheet.
- The daily collection by method (BR-901) does not count suspense on the day it arrived nor the payment on the day it
  was applied (that payment has no Cash entry). The collection report shows a "Suspense recorded" row so that the day's
  bank receipts still add up.
- Three document types and their series; the gapless check (identity 11) counts them.

## Alternatives considered

- **No posting: an unmatched line is the suspense.** Simpler, no new account; but the ledger's Cash would not include
  money that is in the bank, the books would not show what is owed back, and nothing would age it. Rejected.
- **Suspense as a payment of a "suspense payer":** no new account; but every report of payers would carry a payer
  that is not one, and applying it would be a refund and a new payment. Rejected.
- **A checker for recording too:** the money is a fact of the bank; a checker would only delay it. Rejected.
