# ADR 0066: Matching posts nothing; the automatic rules, the tolerances and who may match

- **Status:** Accepted as design on 2026-11-28 (Prompt 13.0, approved "as recommended"). Built by Prompts 13.2
  (automatic matching) and 13.3 (manual matching and the workspace), which add their built notes.
- **Date:** 2026-11-28
- **Deciders:** project owner, Claude Code
- **Rules:** BR-1006 to BR-1013, BR-1023, BR-1029

## Context

A payment posts `Dr Cash / Cr UnappliedCash` the moment it is recorded (BR-302). When the bank's statement shows the
money arriving, nothing about what the payer owes changes; what is learnt is that the recorded payment really reached
the bank. The questions were whether a match is a financial document, whether the machine may decide one, how close
amounts and dates must be, and whether a second person must agree.

## Decision

1. **A match is a record, not a posting.** It joins lines of one channel to items (payments, refunds, payment
   reversals, shifts' deposits, a settlement's net, suspense items and returns) whose amounts add up to the lines' to
   the paisa. It changes no balance, no ledger entry and no figure of R1. It is audited (BR-701).
2. **Unmatching is a new record.** The match is kept with its status `Ended` and who ended it, when and why.
3. **No checker for matching** (BR-603 does not apply): a wrong match moves no money and is undone by ending it. What
   does move money in R2 (applying and returning suspense) has a checker (ADR 0067).
4. **The machine confirms only the certain case:** the reference equal (without case, spaces and leading zeros), the
   amount equal, the date in the window, the payment neither reversed nor matched, exactly one candidate each way.
   Everything else is a proposal with a score and its reason in words (reference equal but not the rest; amount and
   date with one candidate; up to five payments of one payer that add up). A tie is never confirmed and never ranked.
5. **Tolerances:** the amount to the paisa, always. The date: the item on or before the line and at most the
   channel's window of days before it, 3 by default, 0 to 10 per channel; outside it only by hand and with a note.
6. **What a method can match:** UPI and Card a gateway's settlement or, where the hospital takes them straight into
   the bank, a bank line; BankTransfer and Cheque a bank line; Cash never by itself, only as a shift's deposit
   (counted cash of closed shifts). Nothing is posted for a deposit: counter and bank are one `Cash` account here.
7. **Money not in BillingRecon:** a forgotten payment is recorded from its line as a normal payment and matched in one
   transaction; money that is not a patient's (interest, bank charges, a transfer between the hospital's accounts) is
   excluded with a category and a reason; money from an unknown sender is suspense (ADR 0067).
8. **Concurrency:** each line is matched in a transaction of its own; filtered unique indexes on the link tables
   (where the match is active) and the lines' row versions make a second claim fail. Two runs at once match nothing
   twice.
9. **Who:** a TenantAdmin, a Supervisor or an Accountant (ADR 0069); a Viewer reads.

## Consequences

- Most lines of a normal day clear by themselves, because UPI and bank transfers carry a reference the counter
  already types (BR-301).
- An exported period (ADR 0062) is unaffected by matching or unmatching: the export reads postings, and a match has
  none.
- A wrong automatic match is possible only when two records carry the same reference and amount, which the uniqueness
  of BR-301 already prevents for one payer; a run never confirms across two candidates.

## Alternatives considered

- **Matching posts a transfer from a "cash in transit" account to the bank:** a true bank book, but a second Cash
  account, every payment posting twice, and a checker for every match. Rejected: the receivables ledger is not the
  bank book, and the accountant's package keeps the bank book.
- **Confirm by amount and date alone when there is one candidate:** more lines clear by themselves; a coincidence of
  amounts (two ₹500 consultations) would be matched wrongly. Rejected; it is a proposal instead.
- **A checker for every manual match:** doubles the work of the morning for a record that moves no money. Rejected.
