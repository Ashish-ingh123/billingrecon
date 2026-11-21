# ADR 0029: Refunds: one source, the checks asked twice, and the payment that is never reversed

- **Status:** Accepted. The owner approved the plan of Prompt 4.6 on 2026-11-21 with the five recommended answers,
  which are points 1 to 5 below. Built and tested on .NET 10 (SDK 10.0.404, EF Core 10.0.12) on the office machine:
  build, format gate and the tests without a database in the repository, the database tests in a throwaway copy
  against SQL Server LocalDB 2019. SQL Server 2022 in Docker is still to come.
- **Date:** 2026-11-21
- **Deciders:** project owner, Claude Code
- **Refines:** ADR 0022 (the order of a posting use case), ADR 0023 (maker-checker), ADR 0026 (the cap asked twice,
  the document reached through a Billing contract), ADR 0027 (the reversal of a payment).
- **Implements:** BR-307 (with one added sentence), BR-308, BR-309 for refunds, the rule of BR-306 that a refunded
  payment is not reversed, BR-604 for refunds; US-34. Amends BR-307.

## Context

A refund pays money back to a payer (Prompt 4.6): from what a payment has not applied to any bill, or from what a
posted credit note has not applied. It is a maker-checker document like an adjustment, numbered when the checker
approves, and never reversed (BR-308). Two of its rules reach other aggregates: the source must still hold the amount,
and a payment from which a refund was posted may not be reversed (BR-306, left open by ADR 0027 point 2).

## Decision

1. **BR-306 is asked when the reversal of a payment is requested.** `RequestPaymentReversalHandler` asks
   `IRefundRepository.AnyPostedFromPaymentAsync` and refuses with 422 `payment_refunded`. The schema stays as
   `docs/database.md` designed it: no cached `RefundedAmount` on `Payments`, and so no extra drift identity for it.
   The answer cannot go stale before the approval: a refund cannot be posted from a payment that waits for its
   reversal (point 4), and a request racing a refund's approval saves the same payment row, so the second of the two
   fails on the row version (409). `Payment.Reverse` keeps its guard and still throws if anything but allocations
   took money from the payment.
2. **The cap and the source's state are asked twice, the payer's activity at draft and at approval.** When the maker
   submits (`post`), for an early 422 `refund_exceeds_available`, and again when the checker approves, because the
   source may have been allocated, applied or refunded meanwhile (BR-307 says "at posting"), as ADR 0026 point 2 does
   for adjustments. The payer must be active when the refund is drafted and when it is approved (BR-309; 422
   `payer_inactive`), not when it is merely handed over.
3. **The refund date is not before the date of its source**, the payment date or the credit note date, as BR-214 says
   for credit notes and BR-501 for adjustments. BR-307 gains that sentence; BR-205 applies as before. 422
   `refund_date_before_source_date`.
4. **A source that waits for its reversal, or was reversed, refunds nothing** (409 `payment_pending_reversal`,
   `payment_reversed`), at the draft, the post and the approval. A refund that only waits for approval does not stop
   the payment's reversal from being asked for; per BR-306 only a posted refund does. If that reversal is approved
   first, the refund's approval answers 409 and the refund can only be rejected or voided.
5. **The source records the refund in its own audit trail too:** the payment shows `Refunded` (`PaymentRefunded`) and
   the credit note shows `Refunded` (`CreditNoteRefunded`), each with the refund's number, beside the refund's own
   trail: `Drafted`, `Updated`, `Voided`, `SubmittedForApproval`, `ReturnedToDraft`, `Posted`. (The plan said
   `Replaced` for a corrected draft; it is `Updated`, as for an adjustment, because the event is `RefundUpdated`.)
6. **The aggregate** (`Refund`, Payments module) has exactly one source (`RefundSource`: a payment or a credit note,
   `CK_Refunds_OneSource`), takes the payer of that source, which never changes, and has one amount, a method and a
   reference under the rules of a payment (BR-301). A draft is replaced as a whole with `PUT /refunds/{id}`; the source
   stays. The number (`REF/26-27/000001`) is taken at the approval, for the fiscal year of the refund date.
7. **The credit note is reached only through `ICreditNoteSettlement`,** a new contract of Billing shaped like
   `IBillSettlement` (ADR 0024, ADR 0026 point 6): `FindPostedAsync` answers the payer, the date, the number and the
   available amount in a record without a type of the Billing domain, or `credit_note_not_found` /
   `credit_note_not_posted`; `PayRefundAsync` calls `CreditNote.PayRefund`. No new module-dependency exception. The
   payment is in the Payments module itself and pays through `Payment.PayRefund`.
8. **The order of an approval** (ADR 0022): the version; the source, still usable; the refund waits and its date is
   still allowed; the checker is a user and not the maker; the pending request (409 when another decision came first,
   ADR 0027 point 9); the payer still active; the cap against what the source holds now; then the number; the source
   pays; the posting Dr UnappliedCash(payment) or CreditAvailable(credit note) / Cr Cash, dated the refund date, which
   locks the payer's row of `PayerBalances`; one save for the refund, its source and the request. A ledger that finds a
   balance going below zero answers 409, as for allocations.
9. **The reconciliation** subtracts posted refunds in the second forms of identities 4 and 5 (`PaymentAllocations`,
   `CreditNoteApplications`), as `docs/database.md` section 5.5 always said, and gains three identities:
   `RefundWithoutPosting`, `PostingWithoutRefund` and `RefundPayerMismatch`.
10. **Migration `0017_Refunds`** creates the one table: the columns of section 3.4 with the posted and voided stamps
    and a row version, nine CHECK constraints (three generated from the enums), five foreign keys from the typed-id
    convention (tenant, payer, payment, credit note, posting) and five indexes. Users move to `0018_Users`.
11. **The scenario test of the prompt** (`EndToEndScenarioTests`) walks one payer through two bills, a part payment,
    a credit note, an adjustment, the reversal of an allocation and a refund, checks the payer's balances and the
    reconciliation after every step, and ends with every series gapless (`docs/database.md` section 4.3) and every
    `NumberSeries.NextNumber` one past its last number.

## Alternatives

- **A cached `RefundedAmount` on `Payments`,** so that the aggregate itself refuses a reversal. Rejected with the
  plan: a column and an identity more for a question one indexed read answers, and the race is closed by the row
  version anyway.
- **Check the cap only at approval, as BR-307 words it.** Rejected: the maker would learn of an impossible refund only
  from the checker, as with adjustments.
- **Let a refund freeze its source while it waits.** A waiting refund is not money yet; the cap at approval and the
  row versions keep the data right without locking the cashier out of the payment.
- **Put the refund of a credit note in Billing.** The design places every refund in Payments (`docs/domain-model.md`
  section 4.7), with one aggregate and one number series for both sources.

## Consequences

- **Changed expectations in existing tests, none weakened.** Two tests of `MigrationsTests` asserted that nothing
  after migration `0014` and after `0016` created a table, meant as "this migration creates no table"; with `0017`
  that is no longer true of the rest of the script, so each now looks at its own migration only (`Between`), which is
  what they meant. `ReceivablesOnSqlServerTests` enters refund postings through the ledger port alone, without a
  refund row, and asserted no drift; the new identity `PostingWithoutRefund` reports them, so that class now leaves it
  out, exactly as it has left out `PostingWithoutPayment` since Prompt 4.1 for the same reason (`RefundsApiTests`
  asserts the identity in both directions). `MigrationsOnSqlServerTests` expects 17 migrations; `TenantQueryFilterTests`,
  `TypedIdForeignKeyTests` and `IdempotencyConventionTests` list the new table, keys and controller.
- `RequestPaymentReversalHandler` takes `IRefundRepository`; a payment with a posted refund answers 422
  `payment_refunded` where it answered 202 before (no refund could be posted before).
- The JSON of a payment and of a credit note does not change; what a refund took shows in their `unappliedAmount` and
  `availableAmount`, and in their audit trails.
- **Verification.** On .NET 10 (SDK 10.0.404): the repository's build with warnings as errors and
  `dotnet format --verify-no-changes` are clean; Domain 596, Application 341 and Architecture 113 tests pass in the
  repository; the integration project passes in full (1017 tests) in a throwaway copy whose fixture points at LocalDB
  2019, and the 20 concurrency tests named `BR804` then ran five more times in a row without a failure. `0017` was
  scaffolded with the repository's `dotnet-ef` 10.0.12 in that copy and copied back, and EF Core reports no pending
  model change. Still to do: SQL Server 2022 in Docker.
