# ADR 0028: Reversing an application of credit

- **Status:** Accepted. The owner approved the plan of Prompt 4.5b on 2026-11-21 with the five recommended answers,
  which are points 1 to 5 below. Built and tested on .NET 10 (SDK 10.0.404, EF Core 10.0.12) on the office machine:
  build, format gate and the tests without a database in the repository, the database tests in a throwaway copy
  against SQL Server LocalDB 2019. SQL Server 2022 in Docker is still to come.
- **Date:** 2026-11-21
- **Deciders:** project owner, Claude Code
- **Refines:** ADR 0025 (credit notes; closes the question its point 5 left open), ADR 0027 (reversals, whose shape
  this one copies).
- **Implements:** BR-213 and BR-208 ("reversals move it back") for applications of credit, BR-603 to BR-606 for this
  action.

## Context

A credit note's available credit can be applied to other posted bills of its payer (BR-213, ADR 0025 point 7). An
application made to the wrong bill had no way back: unlike an allocation of a payment (ADR 0027), it could not be
reversed. Prompt 4.5b builds that reversal in the shape of ADR 0027 and asks first whether the application a credit
note makes to its own bill when it is posted (BR-212) may be reversed too.

## Decision

1. **Only a later application can be reversed.** The application to its own bill that the approval of a credit note
   makes (`min(Total, Bill.BalanceDue)`, BR-212) is part of the posting: the credit note exists to lower that bill. A
   request to reverse it answers 422 `credit_application_made_at_posting`. A credit note raised against the wrong
   bill stays a correction by a new bill, as BR-308 and `docs/domain-model.md` section 5.2 say for posted documents.
2. **The row says which application was made at posting:** `CreditNoteAllocations.AppliedAtPosting` (`bit`), set by
   `CreditNote.ApplyToOwnBillAtPosting`, which only the approval use case calls and only as the first application of
   a credit note just posted. Inferring it (the first row whose bill is the credit note's own) was rejected: a later
   application to the same bill would look the same. A CHECK constraint, `CK_CreditNoteAllocations_AtPostingStaysActive`,
   keeps such a row `Active` whatever writes to the table.
3. **One decision at a time, per application.** An application that waits answers 409
   `credit_application_pending_reversal` to a second request, and one that is reversed answers 409
   `credit_application_reversed` for good. Meanwhile the credit note stays usable: more of its available credit can
   be applied to other bills while one of its applications waits. Unlike a payment, a credit note is not reversed as a
   whole, so nothing waits on the credit note itself.
4. **No document number, and the reason is the maker's, as in ADR 0027 points 4 and 5.** The reason is required on
   the request, kept as the comment of the approval request (`DocumentType` `CreditNoteAllocation`, action
   `Reverse`), and copied to `ReversalReason` of the application by the approval, with who approved and when. The
   mirror posting (`CreditApplicationReversed`, document type `CreditNoteAllocation`) names the posting it reverses
   in `ReversalOfPostingId` and is dated the day of the approval.
5. **The migrations of 0003 to 0015 were committed on their own first** (commit `740fb31`), so that
   `0016_CreditApplicationReversals` is added the ordinary way, on top of migrations that are in the repository.
6. **The endpoints** (`docs/api.md` section 8.8), each with `If-Match` on the version of the **credit note** and an
   `Idempotency-Key`, each answering the credit note with its applications:
   `POST /credit-notes/{id}/applications/{applicationId}/reverse` (202), then `approve-reversal` (200, body optional)
   or `reject-reversal` (200, comment required). While it waits, the application is `PendingReversal` and its amount
   is still applied. The approval marks it `Reversed`, raises `CreditNote.AvailableAmount` and the bill's balance by
   its amount, moves the bill's status back (BR-208), and enters Dr Receivable(bill) / Cr CreditAvailable(credit
   note). The amount can be applied again. A rejection makes the application `Active` again and writes nothing
   financial.
7. **The approval keeps the order of ADR 0022 and ADR 0027:** the version, the application (it waits), the checker
   (a user, not the maker); the pending request (409 when another decision came first, ADR 0027 point 9); then the
   credit note and the bill in memory, the bill through `IBillSettlement.RestoreAsync`, the contract the Payments
   module already uses; the ledger, which locks the payer's row of `PayerBalances`; one save for the credit note, the
   bill and the request under their row versions. A reversal and a payment allocation to the same bill at the same
   moment: the second to commit either went after the first or gets 409 and writes nothing. Neither posting can take
   a balance below zero, so the ledger's refusal of BR-405 is passed on as it comes.
8. **Migration `0016_CreditApplicationReversals`** adds five columns to `CreditNoteAllocations` (`AppliedAtPosting`,
   `ReversedAtUtc`, `ReversedByUserId`, `ReversalReason`, `ReversalPostingId`), widens `CK_CreditNoteAllocations_Status`
   to `Active`, `PendingReversal`, `Reversed`, ties the four reversal columns to the status `Reversed`
   (`CK_CreditNoteAllocations_ReversedIffReversed`), adds the constraint of point 2 and the foreign key from
   `ReversalPostingId` to `LedgerPostings`. No table is created. Refunds and users move to `0017_Refunds` and
   `0018_Users`.
9. **Existing rows get `AppliedAtPosting = 0`.** The column is added with that default, so an application made at
   posting before this migration would count as a later one and could be reversed. That is accepted because no
   database outside the tests holds a posted credit note yet: R1 has not been deployed. A data step that sets the
   flag on the first application of each credit note to its own bill was not written: it would be a hand-written addition to a migration, which needs the owner's approval
   (CLAUDE.md section 5). If a database with posted credit notes ever exists before this migration runs, that step is
   owed.
10. **The reconciliation** counts an application that waits as applied and a reversed one as not
    (`CreditNoteApplications`), gains `ReversedApplicationNoPosting` (a reversed application without the mirror
    posting that names its own), and extends `ReversalPostingNoDocument` to postings of kind
    `CreditApplicationReversed`.

## Alternatives

- **Let the application made at posting be reversed as well.** It would leave a posted credit note owing nothing to
  the bill it was written for, and make "credit note against the wrong bill" look correctable when R1 says it is
  not. Rejected with the plan.
- **Infer the application made at posting instead of storing it.** Rejected: point 2.
- **Freeze the credit note while one of its applications waits.** Nothing it does meanwhile can touch the waiting
  application, and a payment allocation does not freeze its payment either (ADR 0027 point 3 is about the payment's
  own reversal). Rejected with the plan.
- **Number the reversal.** Rejected for the reasons of ADR 0027 point 4.

## Consequences

- The JSON of an application gains `appliedAtPosting`, `reversedAtUtc` and `reversalReason`; its `status` is `Active`,
  `PendingReversal` or `Reversed`.
- The audit trail of a credit note shows `ApplicationReversalRequested`, `ApplicationReversalRejected` and
  `ApplicationReversed`, each with the reason and the number of the credit note; the bill shows `CreditReversed`.
- `ApproveCreditNoteHandler` now calls `ApplyToOwnBillAtPosting` instead of `Apply`; nothing else of Prompt 4.3
  changed.
- **Verification.** On .NET 10 (SDK 10.0.404): the repository's build with warnings as errors and
  `dotnet format --verify-no-changes` are clean; Domain 579, Application 330 and Architecture 113 tests pass in the
  repository; the integration project passes in full (1001 tests) in a throwaway copy whose fixture points at
  LocalDB 2019. `0016` was scaffolded with the repository's `dotnet-ef` 10.0.12 in that copy, because the tool
  manifest in the repository carries the "came from another computer" mark (CLAUDE.md section 2), and copied back.
  The 18 concurrency tests named `BR804`, in every class, then ran five more times in a row without a failure.
  Still to do: SQL Server 2022 in Docker.
