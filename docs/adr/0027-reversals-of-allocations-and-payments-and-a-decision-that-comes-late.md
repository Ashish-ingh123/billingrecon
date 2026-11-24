# ADR 0027: Reversals of allocations and payments, and a decision that comes late

- **Status:** Accepted. The owner approved the plan of Prompt 4.5 on 2026-11-21 with the five recommended answers,
  which are points 1 to 5 below; point 9 was found and fixed while building, and the owner confirmed it on 2026-11-24 with the release of R1. Built and
  tested on .NET 10 (SDK 10.0.404, EF Core 10.0.12) in a throwaway copy on the office machine, with the database
  tests run against SQL Server LocalDB 2019; SQL Server 2022 in Docker is still to come.
- **Date:** 2026-11-21
- **Deciders:** project owner, Claude Code
- **Refines:** ADR 0022 (the order of a posting use case), ADR 0023 (maker-checker), ADR 0024 (allocations and the
  Billing contract).
- **Implements:** BR-305, BR-306, BR-208 ("reversals move it back"), BR-604 for reversals; US-32, US-33.
- **Corrects:** ADR 0023, for what a decision answers when another decision was committed first (point 9).

## Context

An allocation made to the wrong bill, or a payment that never arrived (a dishonoured cheque), is taken back by a
reversal (Prompt 4.5): a cashier asks, a second person decides, the ledger gets mirror postings, and no row is
deleted or edited away. A reversal is the first maker-checker action that is not a posting of a new document, and
the first that raises a bill's balance again.

## Decision

1. **Reversing an application of a credit note is not part of this prompt.** It mirrors point 6 inside Billing and
   becomes a prompt of its own, 4.5b, straight after (`docs/build-prompts.md`).
2. **The rule "a payment from which a refund was posted is not reversed" (BR-306) is checked with refunds**
   (Prompt 4.6): nothing can post a refund yet. Until then `Payment.Reverse` throws if part of the payment went
   anywhere but to allocations, so the gap cannot pass silently.
3. **One decision at a time.** A payment cannot be asked for reversal while one of its allocations waits for a
   decision of its own (409 `allocation_pending_reversal`), and an allocation cannot while its payment waits (409
   `payment_pending_reversal`). A payment reversal therefore never sweeps up a pending request.
4. **A reversal takes no document number.** BR-305 and BR-306 name none and BR-801 does not list them. The mirror
   posting names the posting it reverses (`ReversalOfPostingId`), keeps the document type and id of what it
   reverses, and is dated the day of the approval. The reversed payment keeps its own number: the series has no gap.
5. **The reason is the maker's, and it reaches the row only when the reversal happens.** It is required on the
   request and kept as the comment of the approval request; the approval copies it to `ReversalReason` of the
   allocation or the payment, with who approved and when. A rejected request leaves nothing on the payment: it is in
   the approval history and the audit trail.
6. **Reversing an allocation** (`POST /payments/{id}/allocations/{allocationId}/reverse`, 202; then
   `approve-reversal` or `reject-reversal`): while it waits, the allocation is `PendingReversal` and its amount is
   still applied. The approval marks it `Reversed`, raises `Payment.UnappliedAmount` and the bill's balance by its
   amount, moves the bill's status back (`Settled` to `PartiallyPaid`, or to `Posted` when nothing is settled any
   more; BR-208), and enters Dr Receivable(bill) / Cr UnappliedCash(payment). The amount can be allocated again.
7. **Reversing a payment** (`POST /payments/{id}/reverse`, 202; then `approve-reversal` or `reject-reversal`):
   while it waits, the payment is `PendingReversal` and nothing can be allocated from it. The approval reverses
   every allocation that is still applied, as in point 6 and with the payment's reason, in the order of the bills;
   then enters Dr UnappliedCash(payment) / Cr Cash for the whole amount. The payment ends `Reversed` with nothing
   unapplied and never changes again (409 `payment_reversed`).
8. **Payments raises a bill only through `IBillSettlement`,** which gains `RestoreAsync`, the mirror of `ApplyAsync`;
   in Billing it calls `Bill.ReverseCredit`. Giving back more than was ever applied is a bug in the caller and
   throws. No new module-dependency exception. The order of an approval is that of ADR 0022: the version, the state,
   the checker; then the payment and the bills in memory; the ledger, whose first posting locks the payer's row of
   `PayerBalances`; one save for the payment, every bill and the request, each under its row version.
9. **A decision that finds no pending approval request answers 409 `concurrency_conflict`.** Every approve and
   reject use case reads its document, checks that it waits, and then loads the pending request. If another
   decision is committed between those two reads, the request is no longer pending. All ten handlers (bills since
   Prompt 3.6, credit notes, adjustments, and the four of this prompt) threw `InvalidOperationException` there, which
   the client saw as 500. Nothing was written, but the answer was wrong: the document was read stale, and the API
   answers a stale read with 409 (CLAUDE.md section 4, rule 6). `PendingApprovals.FindPendingAsync` now gives that
   answer in one place. The test `BillApprovalApiTests.BR804_a_decision_that_read_the_bill_before_another_decision_committed...`
   forces the order and failed with 500 before the change.
10. **Migration `0015_PaymentReversals`** adds four columns to `Payments` and four to `PaymentAllocations`, with a
    CHECK constraint on each that ties them to the status `Reversed` (and, for a payment, to nothing unapplied) and a
    foreign key from `ReversalPostingId` to `LedgerPostings`. The columns are kept out of `0011` and `0012` by a
    third configuration class, `PaymentWithReversalsConfiguration`, held back while those are scaffolded (the device
    of ADR 0022 point 7). The status constraints of both tables list the reversal states from `0011` and `0012` on,
    because they are generated from the enums. Refunds and users move to `0016` and `0017`.
11. **The reconciliation** learns that an allocation waiting for its reversal still counts as applied and that a
    reversed payment holds nothing, and gains three identities: `ReversedAllocationNoPosting`,
    `ReversedPaymentNoPosting` and `ReversalPostingNoDocument`.

## Alternatives

- **Let a payment reversal take a pending allocation reversal with it.** Rejected with the plan: it leaves an
  approval request with nothing to decide.
- **A document of its own for a reversal, with a number.** Rejected: the rules name none, and the row that is
  reversed already says who, when and why.
- **Keep the exception of point 9 and retry in the client.** A 500 says "the server is broken"; a monitoring rule
  would page for an event that two supervisors can cause any afternoon.
- **Take a lock on the approval request before reading the document.** It would close the window, at the price of
  raw SQL in a Common repository; the row versions already keep the data right, and only the answer was wrong.

## Consequences

- **A changed expectation in an existing test, called out as the working agreement asks.**
  `BillApprovalHandlerTests.A_bill_that_waits_without_a_request_is_a_fault` expected an exception and is now
  `BR804_a_bill_read_as_waiting_whose_request_is_no_longer_pending_is_a_conflict...`, expecting 409. Prompt 3.6 had
  decided the other way; point 9 says why that was wrong. A waiting document that truly has no request, which would
  be a defect in the data, now answers 409 on every attempt instead of 500; the reconciliation does not look for it
  yet.
- **Two more changed expectations**, both because a constraint is generated from an enum: `MigrationsTests` expects
  `CK_Payments_Status` and `CK_PaymentAllocations_Status` to list the three states in `0011` and `0012`.
- Allocating from a payment that waits for its reversal, or was reversed, is refused; before this prompt neither
  state existed.
- The JSON of a payment and of an allocation gained `reversedAtUtc` and `reversalReason`, null unless reversed.
- An approval of a payment reversal saves the payment, every bill it was allocated to and the request in one
  commit, and writes an audit record for each change.
- The intermittent failure recorded with Prompt 4.4 (`BillApprovalApiTests.BR804_two_checkers_at_once...`, once in
  about fifty runs) was point 9. Its message was captured during this prompt: the losing request answered 500.
- **Verification.** On .NET 10 (SDK 10.0.404), in a throwaway copy made from scratch with no version substitution:
  the repository's `dotnet-ef` 10.0.12 scaffolded `0003` to `0015` one by one (`0015` holds eight columns, two CHECK
  constraints and two foreign keys, and no table; `0011` and `0012` hold no column of a reversal); strict build and
  format gate clean; Domain 572, Application 322, Architecture 113 and Integration 989 tests, all green. The
  database tests ran against LocalDB 2019 through a patched fixture, in the copy only. The concurrency classes of
  `docs/runbook.md` section 8 D (231 tests) then ran twelve times in a row without a failure. Still to do: SQL
  Server 2022 in Docker, and the migrations brought into the repository.
