# ADR 0025: Credit notes: always a second person, the caps measured again under the lock, and applying the credit

- **Status:** Accepted. The owner approved the plan of Prompt 4.3 on 2026-11-21 with the six recommended answers,
  which are points 2 to 7 below. Written on the office machine and pre-checked on .NET 9, with the database tests run
  against SQL Server LocalDB 2019; the first run on .NET 10 and SQL Server 2022 is still to come.
- **Date:** 2026-11-21
- **Deciders:** project owner, Claude Code
- **Refines:** ADR 0019 (drafts with replaceable lines), ADR 0022 (the order of a posting use case), ADR 0023
  (maker-checker), ADR 0024 (two aggregates and the one lock).
- **Implements:** BR-209 to BR-214, BR-604 for credit notes; US-24.

## Context

A credit note (Prompt 4.3) corrects a posted bill. It is the first document that always needs a second person, the
first whose posting writes two ledger postings, and the first with a cap that is a sum over other documents: the
posted credit notes of a bill never come to more than the bill (BR-210), nor those for one line to more than that
line (BR-211). Three things had to be settled beyond what ADRs 0022 to 0024 already fix.

- A sum over other rows cannot be protected by a row version. Two credit notes of one bill can each pass the cap
  while neither is posted; and when the bill is already paid, approving them changes nothing on the bill, so the
  bill's row version does not stop the second either.
- The posting has to create the credit before it consumes it, or the ledger's refusal of a balance below zero
  (ADR 0024, point 4) rejects the application.
- What is left of the credit after its own bill is applied to other bills later, by a request of its own.

## Decision

1. **The shape of ADR 0023, with one difference: `post` never posts.** `POST /credit-notes/{id}/post` is the maker's
   request and always answers **202** with the credit note in `PendingApproval`, whatever its total, and opens the
   approval request in the same commit. `POST .../approve` is the posting and `POST .../reject` returns it to draft
   with the checker's reason. The checker is never the maker (403 `approval_checker_is_maker`); that the checker holds
   Supervisor or TenantAdmin waits for Prompt 5.3, as for bills.
2. **The caps are checked twice.** Once with nothing locked, when the maker asks and again when the checker
   approves, for the plain answers 422 `credit_note_exceeds_bill_total` and `credit_note_line_exceeds_bill_line`. And
   once more **after the first ledger posting of the approval has taken the lock on the payer's row of
   `PayerBalances`**: every approval of a credit note of that payer waits for that row, so what is read under it is
   what holds at the commit. A credit note that fitted and no longer does is answered **409
   `concurrency_conflict`**, and its number goes back with the rest of the transaction. The rule itself is
   `CreditNote.CheckWithinBill`; the reads are `ICreditNoteRepository.ListPostedForBillAsync`. No column was added
   to `Bills`.
3. **A credit note that comes to nothing cannot be submitted:** 422 `credit_note_total_zero`. So every posted credit
   note has its ledger posting, and BR-401 needs no second exception. One without a line is 422
   `credit_note_has_no_lines`.
4. **The payer is not asked whether it is active**, at the request or at the approval: a credit note takes no new
   money and lowers what is owed. The date is checked again at both (BR-205, and BR-214 against the bill date).
5. **`CreditNoteAllocations` has what applying needs**: status `Active` alone and no reversal columns, as
   `PaymentAllocations` (ADR 0024). **Open:** no prompt of R1 reverses an application of credit; Prompt 4.5 names
   allocations of payments and payments only. The owner decides whether 4.5 gains it or R1 goes without.
   *Closed on 2026-11-21:* Prompt 4.5b reverses later applications, and the application made at posting stays
   (ADR 0028; migration `0016_CreditApplicationReversals`).
6. **A line may name the line of the bill it credits**, by the id of that line, and then takes its description,
   unit price and tax rate from it unless the request gives its own; a line without one credits the bill as a whole
   and carries all three. The quantity is always given. Lines of a credit note have no service item of their own.
   For this the JSON of a bill line gained its `id`.
7. **Applying the rest needs no second person** (BR-604 does not list it):
   `POST /credit-notes/{id}/applications`, with `If-Match` on the credit note and an `Idempotency-Key`, built like the
   allocation of a payment (ADR 0024): 1 to 50 bills, each once, all or none, the bills reached through
   `IBillSettlement`, taken in the order of their ids.
8. **The order of an approval** (the six steps of ADR 0022):
   1. The version the checker read; the credit note waits and its date is still allowed; the checker is a user and
      not the maker; the caps (point 2).
   2. The number, for the fiscal year of the credit note date.
   3. The first posting, Dr Revenue / Cr CreditAvailable(credit note), for the total. From here the payer's row of
      `PayerBalances` is locked.
   4. The caps again, under that lock.
   5. The credit is applied to its own bill as far as the bill is owed, `min(Total, Bill.BalanceDue)`: the bill takes
      it (`Bill.ApplyCredit`, BR-208), the credit note keeps a `CreditNoteAllocation`, and the second posting,
      Dr CreditAvailable(credit note) / Cr Receivable(bill), is entered. A bill that is owed nothing gets nothing
      applied, and no second posting is written.
   6. One save for the credit note, its application, the bill and the approval request, each aggregate under its
      row version, with the ledger rows and the audit records.
9. **A draft is replaced like a bill draft** (ADR 0019): its lines are replaced as a whole, and a line taken out is
   deleted. `CreditNoteLine` is therefore the second entity that carries `IHardDeletable`, and
   `FK_CreditNoteLines_CreditNoteId` the second foreign key with `ClientCascade`; the database still deletes nothing.
10. **The reconciliation gains eight identities**: `CreditNoteAvailable` (the cache against the ledger),
    `CreditNoteApplications` (the cache against the total less the active applications), `CreditNoteWithoutPosting`
    and `PostingWithoutCreditNote`, `ApplicationWithoutPosting` and `PostingWithoutApplication`,
    `ApplicationPayerMismatch`, and `CreditNotesExceedBillTotal` (BR-210).
11. **Migration `0013_CreditNotes`** creates the three tables. Their foreign keys to bills, bill lines, payers,
    patients and ledger postings come from the typed-id convention (ADR 0021); no new module-dependency exception.

## Alternatives

- **A `CreditedTotal` column on `Bills`**, raised by every approval, so that the bill's row version carries the cap.
  Rejected by the owner with the plan: it changes a table that is built, and it covers BR-210 only, not the cap per
  line.
- **Check the caps only under the lock.** The usual refusal would then cost a number taken and rolled back, and be
  answered 409 where 422 is the truth.
- **Number a credit note of zero and give it no posting**, as ADR 0022 does for a bill of zero. Rejected: nobody
  needs a credit note for nothing, and the exception to BR-401 stays one.
- **Maker-checker for applying credit to another bill.** Rejected: BR-604 does not list it, and the money moves
  between two bills of one payer.

## Consequences

- An approval of a credit note saves three aggregates in one commit (the credit note, the bill, the request) and
  writes an audit record for each change: `Posted` and `Applied` on the credit note, `CreditApplied` on the bill,
  `Approved` on the request.
- Approvals of credit notes of one payer run one after the other from the first posting to the commit, in the same
  queue as postings, payments and allocations (ADR 0005).
- Adjustments (4.4) lower a bill the same way and, if they are capped by a sum, follow point 2.
- **Changed expectations in existing tests, called out as the working agreement asks.**
  `HardDeleteGuardTests` expected a bill line and an idempotency key to be the only deletable entities and now
  expects the credit note line as well (the test is renamed for it). `RelationalConventionTests` expected one
  foreign key with `ClientCascade` and now expects two. Both follow from point 9; neither test checks less than it
  did.
- The refusal `allocation_payer_mismatch` is reused for credit applied to a bill of another payer; its message
  still speaks of a payment.
- **Verification.** Pre-checked on 2026-11-21 in a throwaway .NET 9 copy made from scratch: strict build, format
  gate, Domain 540 and Application 306 tests green, Architecture 108 green and Integration 958 green on LocalDB
  2019, with `0003` to `0013` scaffolded there separately by EF Core 9 (`0013` held the three credit note tables
  alone). Failing there, as before and by design: the same eleven tests. The credit note, allocation, approval,
  ledger and migration tests (73) ran eight times in a row without a failure. The test of two credit notes approved
  at once against a paid bill was checked against the code with point 2's second reading switched off: it failed six
  times out of six, and passes with it. Still to do on the machine with .NET 10 and Docker: everything in
  `docs/runbook.md` section 8.

## Amended 2026-11-26 (Prompt 10.8, review finding R1): adjustments count against the cap

The independent review of 2026-11 (`docs/review-2026-11.md`, R1) found that the cap of BR-210 looked at the bill's
total and its other credit notes only. A bill of 1,000.00 that nobody paid could be written off in full (an adjustment,
approved), then credited in full (a credit note, approved: none of it could go to the bill, whose balance was zero, so
all of it stayed as available credit), and that credit refunded (approved): cash paid out for money never received,
with every balance identity still holding. The owner approved the fix on 2026-11-26, with every kind of adjustment
counting (write-off, discount, rounding): each lowers the bill.

1. **The cap is what was billed and not taken off:** a credit note's total, with the bill's other posted credit notes
   **and its posted adjustments**, is not more than the bill's total. BR-210 says so now. The cap for each line
   (BR-211) is unchanged: an adjustment names no line.
2. **Asked where the cap is asked** (points 2 and 3 above): when the maker submits, when the checker approves, and
   again under the ledger's lock, where an adjustment that committed a moment ago is seen and answered as a conflict.
   Posting an adjustment takes the same lock of the payer's balances, so the two cannot pass each other.
3. **Billing asks Adjustments through a contract of Adjustments' Application layer**, `IBillAdjustments`
   (`SumPostedAsync`), the mirror of `IBillSettlement` (ADR 0026): no entity of Adjustments is named by Billing and no
   module exception is added.
4. **One error code:** `credit_note_exceeds_bill_total`, whose message names what adjustments took off when they did.

**Not done, and why:** the reviewer also proposed a reconciliation row for this cap. The reconciliation is one of the
sanctioned raw-SQL files (ADR 0021); a new identity is a change of its own, left for the owner to ask for.

**A hospital with data:** a bill already credited beyond what its adjustments left is not changed by this; no query was
run here to look for one, as there is no live data.

Verified on the office machine: Domain 714 and Application 480 tests, and the `Database` collection, 537, on LocalDB
2019 in a copy, with `CreditNoteAfterAdjustmentApiTests` (the finding's own sequence refused at its third step; a part
write-off; a credit note that waited while its bill was written off). No existing test was changed but the wiring of
one handler's constructor. SQL Server 2022 is owed with the rest (runbook section 8).
