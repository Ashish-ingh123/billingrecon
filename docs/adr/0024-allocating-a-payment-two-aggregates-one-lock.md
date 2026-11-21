# ADR 0024: Allocating a payment: two aggregates, the order of work, and the one lock

- **Status:** Proposed. Written on 2026-11-21 from the code of Prompt 4.2, which was written on 2026-11-20 and cites
  this ADR; the points below describe what that code does. The owner confirms them, and the Status becomes Accepted
  then. Pre-checked on .NET 9, with the database tests run against SQL Server LocalDB 2019; the first run on .NET 10
  and SQL Server 2022 is still to come.
- **Date:** 2026-11-21
- **Deciders:** project owner, Claude Code
- **Refines:** ADR 0003 (module boundaries), ADR 0005 (the ledger), ADR 0007 (the row version), ADR 0022 (the order
  of a posting use case).

## Context

Allocating a payment to bills (Prompt 4.2; BR-303, BR-304, BR-208, US-31) is the use case that
`docs/domain-model.md` section 6 calls the exception: it changes two aggregates, the payment and one or more bills,
in one transaction. Four things had to be fixed.

- Payments may not name the Billing domain (ADR 0003), yet a bill's balance and status have to change.
- One request may name several bills, and two requests may name the same bills in different orders. Without a fixed
  order they can deadlock.
- Two cashiers can try to settle the last rupee of one bill at the same moment, each with a payment of their own.
  Their payments have different row versions, so the payment's version alone does not stop the second.
- The ledger could, until now, only fail a balance going below zero through the CHECK constraint of
  `PayerBalances`, which is a fault (500), not an answer.

## Decision

1. **Payments reaches a bill only through `IBillSettlement`**, a public Application contract of Billing:
   `ApplyAsync(billId, payerId, amount)`. Billing loads the bill with its own repository, checks that it is owed by
   that payer and calls `Bill.ApplyCredit`, the one change a posted bill accepts (BR-006): the balance due falls and
   the status follows it (`PartiallyPaid`, then `Settled`; BR-208). The bill is tracked by the context of the
   request, so the commit of the calling use case saves it. Nothing is saved inside the contract. No new
   module-dependency exception is needed: a module may call a public Application contract of another (ADR 0003,
   amendment of 2026-10-17).
2. **The order of work and of locks** (the six steps of ADR 0022, without a number: an allocation takes none):
   1. The shape of the request: 1 to 50 lines, no bill named twice, every amount above zero and exact to the paisa.
   2. Read the payment with its allocations, without a lock, and compare the version the client sent (`If-Match`)
      with the one read: 409 `concurrency_conflict` when they differ.
   3. Every check that can refuse, with nothing locked and nothing written: the sum of the request against
      `Payment.UnappliedAmount` (422 `allocation_exceeds_unapplied`), then each bill **in the order of the bill
      ids**, whatever order the client sent: it exists (404 `bill_not_found`), is owed by the payer of the payment
      (422 `allocation_payer_mismatch`), is posted (422 `bill_not_posted`) and is owed at least the amount
      (422 `allocation_exceeds_balance_due`). A request is all or nothing: the first refusal ends it.
   4. **The balances of the payer**: one ledger posting per allocation,
      Dr `UnappliedCash(payment)` / Cr `Receivable(bill)`. The first of them reads the payer's row of
      `PayerBalances` under `UPDLOCK, ROWLOCK` and holds it to the commit (ADR 0005). Payment and bills have the
      same payer, so **this one row puts every allocation for a payer in a queue**; it is the only lock the use case
      waits for.
   5. **The save**: the payment's row and each bill's row are written with `WHERE RowVersion = <the version read>`,
      the allocations, the ledger rows and the audit records are inserted, then the commit.
3. **The last rupee.** Of two requests that read the same bill before either committed, the first to reach step 4
   wins. The second waits there, and then one of two things tells it that what it read is stale: its save finds a
   newer row version of the bill, or the ledger, under its lock, finds that the payer's receivable would go below
   zero. **Both are answered 409 `concurrency_conflict`**, nothing is written, and the loser's payment keeps its
   amount and its version. A request that reads the bill after the winner committed gets 422
   `allocation_exceeds_balance_due` from the bill itself.
4. **The ledger refuses instead of failing.** The locked statement that moves `PayerBalances` now answers whether
   one of the three balances would go below zero, and changes nothing then; `IReceivablesLedger.PostAsync` returns
   `ledger_balance_negative` (BR-405). `LedgerRefusal.IsBalanceBelowZero` lets a module that may not name the
   Receivables domain recognise it. The CHECK constraints of the table stay as the last line of defence.
5. **An allocation is a child of its payment** (`PaymentAllocation`, table `PaymentAllocations`): no row version of
   its own (the payment's covers it), no document number, status `Active` alone until reversals (Prompt 4.5). It
   stores the id of its ledger posting, as the payment does since Prompt 4.1. Its amount, bill and posting never
   change.
6. **The payer is not asked whether it is active.** BR-309 stops new money from an inactive payer; money already
   received can still be applied to that payer's bills.
7. **Two migrations for the payment and its allocations, written before either was scaffolded**, by the device of
   ADR 0022 point 7: `PaymentConfiguration` leaves the allocations out of the model, and
   `PaymentWithAllocationsConfiguration`, which Entity Framework applies after it, puts them in; the second file is
   moved out of the project while `0011_Payments` is scaffolded, and `0012_PaymentAllocations` holds the one table.
   Credit notes, adjustments, refunds and users move to `0013` to `0016`.
8. **The reconciliation gains four identities**: a payment's unapplied amount against its amount less its active
   allocations (`PaymentAllocations`), an allocation without its posting and a posting without its allocation
   (`AllocationWithoutPosting`, `PostingWithoutAllocation`), and an allocation whose two documents have different
   payers (`AllocationPayerMismatch`, BR-303).

## Alternatives

- **Lock the bill rows** (`UPDLOCK`, in the order of their ids) before the checks. The loser would then always read
  the settled bill and get 422. Rejected for the reason ADR 0022 gave: raw SQL in a repository of documents, and
  locks held across the whole use case, when the payer's row already serialises the work and the row version
  already detects the stale read.
- **Let Payments load the bill** through a new module-dependency exception. Rejected: the contract costs one small
  interface, and the rule "no new exception without the owner's approval" stands.
- **Answer the ledger's refusal as 422.** Rejected: both documents accepted the amount as they were read, so the
  only way the ledger can disagree is that somebody else committed in between. That is a conflict, and the client
  should read again.
- **A row version on each allocation.** Rejected: an allocation never changes in this prompt, and its reversal
  (4.5) goes through the payment.

## Consequences

- A cashier who loses the race reads the payment and the bill again; the second attempt gets the plain business
  answer.
- Requests for one payer run one after the other from step 4 to the commit. That is the price of the cached
  balances (ADR 0005), and it is the same queue postings and payments already stand in.
- No deadlock on the rows of the bills: every request that writes a bill of a payer holds that payer's row of
  `PayerBalances` first, so the bill rows are never waited for in two orders. Taking the bills in the order of their
  ids makes the checks, and so the first refusal a client sees, the same whatever order it sent them in.
- `PaymentConfiguration` carries an `Ignore` and a removal from the model that exist only for the scaffolding of
  `0011`. Once `0011` and `0012` are in the repository the two configuration classes can become one.
- Credit notes (4.3) and adjustments (4.4) lower a bill through the same contract.
- **Verification.** Pre-checked on 2026-11-21 in the throwaway .NET 9 copy: strict build, format gate, Domain 518
  and Application 298 tests green, Architecture 108 green and Integration 944 green on LocalDB 2019, with `0003` to
  `0012` scaffolded there separately by EF Core 9 (`0011` held `Payments` alone, `0012` `PaymentAllocations` alone
  with four foreign keys and two indexes). Failing there, as before and by design: the same eleven tests. The
  allocation, payment, receivables, bill-posting and migration tests (108), with two cashiers on the last rupee and
  ten payments allocated at once across overlapping bills, ran three times in a row without a failure. Still to do
  on the machine with .NET 10 and Docker: everything in `docs/runbook.md` section 8.
