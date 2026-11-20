# ADR 0022: Posting a bill: the order of its locks, the bill that comes to nothing, and what a posted bill answers

- **Status:** Accepted. The owner decided the points on 2026-11-20, with the plan of Prompt 3.5 and in the decisions
  recorded for it in `docs/build-prompts.md`. Written on the office machine and pre-checked on .NET 9, with the
  database tests run against SQL Server LocalDB 2019; the first run on .NET 10 and SQL Server 2022 is still to come.
- **Date:** 2026-11-20
- **Deciders:** project owner, Claude Code
- **Refines:** ADR 0005 (the ledger), ADR 0006 (idempotency), ADR 0007 (the row version), ADR 0008 (the number
  series), ADR 0019 (bill drafts).
- **Changes a business rule:** BR-401 and identity 2 of `docs/database.md` section 5.5 gain one stated exception.

## Context

Posting a bill (Prompt 3.5; BR-206, US-21, US-25) is the first use case that takes a document number, writes to the
ledger, moves a payer's balance and changes a document, all in one transaction. Four things had to be fixed before
the next posting use cases copy it.

- Three rows are locked on the way, by three different pieces of code. Two requests that take them in different
  orders deadlock.
- BR-206 says a bill whose total is zero is posted as `Settled`. BR-401 says every posted document has a ledger
  posting, and `CK_LedgerEntries_OneSide` forbids an entry of zero.
- A posting can be asked for twice in several ways (the same key again, another key at the same moment, another key
  later), and the client has to be able to tell them apart.
- BR-207 sends a bill at or above the tenant's threshold to a second person. Maker-checker is Prompt 3.6.

## Decision

1. **The order of work and of locks, for this and every later posting use case:**
   1. Read the document, without a lock, and compare the version the client sent with the one read.
   2. Every check that can refuse: the state of the document, its date (BR-205), its parties, the threshold.
      Nothing is locked and nothing is written yet, so a refusal costs nothing and consumes nothing.
   3. **The number series**: `INumberSeries.NextAsync` reads the series row under `UPDLOCK, HOLDLOCK` and opens the
      transaction if none is open (ADR 0008).
   4. The document takes the number, in memory.
   5. **The balances of the payer**: `IReceivablesLedger.PostAsync` reads the `PayerBalances` row under
      `UPDLOCK, ROWLOCK` and changes it; the posting and its entries join the unit of work (ADR 0005).
   6. **The save**: the document's row is written here, with `WHERE RowVersion = <the version read in step 1>`, then
      the ledger rows and the audit record are inserted. Then the commit.

   So the row of the bill is the **last** lock taken, and it is never held while waiting for another. A second
   posting of the same bill waits at step 3, and when it gets there its save finds a newer row version and fails:
   `concurrency_conflict`, with the number and the balance rolled back. Two postings of different bills of one
   payer queue at step 3 and again at step 5, in the same order, so they cannot deadlock. When the request carries
   an idempotency key, its row was reserved and committed before step 1 in a transaction of its own (ADR 0006), and
   is completed inside this one after step 6.
2. **A bill that comes to nothing is numbered, audited, `Settled`, and has no ledger posting.** BR-401 is amended:
   a posted document whose total is zero has no posting, because a line of zero is not an entry. Identity 2 and the
   reconciliation skip such bills (`Total > 0`). The constraint on entries stays as strict as it was.
3. **What the bill answers once it is posted** (Posted, PartiallyPaid or Settled), to an edit, a void or a second
   posting: **409 `bill_already_posted`**. A voided draft keeps answering 422 `bill_not_draft`. The cases a client
   can meet when it posts, each with its own answer and its own test:

   | The client sends | Answer |
   |---|---|
   | the same key while the first request is still running | 409 `idempotency_in_progress` |
   | the same key after the first request finished | the stored answer again, `Idempotent-Replayed: true` |
   | another key at the same moment, the same version | one succeeds; the other 409 `concurrency_conflict` (step 6) |
   | another key later, with the version from before the posting | 409 `concurrency_conflict` (step 1) |
   | another key later, with the version of the posted bill | 409 `bill_already_posted` (step 2) |

   A stale version is 409 everywhere in this API (CLAUDE.md section 4, rule 6); there is no 412.
4. **Until maker-checker exists, a bill at or above `Tenant.PostingApprovalThreshold` cannot be posted:**
   422 `bill_requires_approval`. A threshold of null means never, and such a tenant posts any bill. The refusal is
   temporary: Prompt 3.6 replaces it with the `PendingApproval` state and removes the error code.
5. **At posting the payer must be active (BR-105); the patient is not asked about again** (BR-102 stops new bills
   for an inactive patient, and the draft exists already). A draft without a line cannot be posted:
   422 `bill_has_no_lines`. The bill date is checked against today once more (BR-205).
6. **The number belongs to the fiscal year of the bill date**, not of the day of posting (BR-004, BR-206).
   `Bill.Post` throws for a number of another year or of another document type: that is a bug in the caller.
7. **Two migrations for one table, written before either was scaffolded.** `0004_Bills` creates the table as a
   draft needs it and `0009_BillPosting` adds the number and its constraints. Both are still to be scaffolded
   (`docs/runbook.md` section 8), so the columns of posting had to be kept out of the first: `BillConfiguration`
   leaves the five properties out of the model, and `BillPostingConfiguration`, which Entity Framework applies
   after it, puts them in; the second file is moved out of the project while `0004` is scaffolded. One thing could
   not be kept apart: `CK_Bills_Status` lists all five statuses from `0004` on, because the constraint is generated
   from the enum.

## Alternatives

- **Lock the bill first** (`UPDLOCK` on its row at step 1). The second poster would wait there instead of at the
  series and would then read a posted bill. Rejected: it needs raw SQL in every repository of a document, and the
  row version already gives the same outcome without a lock that is held across the whole use case.
- **A ledger posting of zero** for a bill that comes to nothing, so that BR-401 stays as written. Rejected: it means
  allowing entries with no amount, and a reader of the ledger would find postings that say nothing.
- **422 for a posted bill**, like a voided draft. Rejected by the owner: the usual cause is a second request for a
  bill that somebody has just posted, which is a conflict, and the client should be told that the bill is posted.
- **Post above the threshold anyway until 3.6.** Rejected: a rule that is switched off "for now" is posted money.

## Consequences

- Every later posting use case (payments, credit notes, adjustments, refunds) follows the six steps in this order.
  One that needs two documents (an allocation) adds both rows to step 6 and nothing to steps 3 and 5.
- A clerk who posts a bill that a colleague posted a moment earlier sees 409 either way; the code says whether to
  read the bill again (`concurrency_conflict`) or to stop (`bill_already_posted`).
- Before Prompt 3.5 the JSON of a bill had no `number`, `fiscalYear` or `postedAtUtc`. It has them now, null on a
  draft; the test `A_draft_has_no_number_in_any_form` asserts null members instead of absent ones, a changed
  expectation called out here.
- `BillConfiguration` carries five `Ignore` calls that exist only for the scaffolding of `0004`. Once `0004` and
  `0009` are in the repository the two configuration classes can become one.
- **Verification.** Pre-checked on 2026-11-20 in the throwaway .NET 9 copy: build, format gate, every unit and
  architecture test apart from the eleven that differ on .NET 9 by design, and the whole integration suite on
  LocalDB 2019, with `0003` to `0009` scaffolded there separately by EF Core 9: `0004` held no column of posting,
  `0009` added five columns, two indexes and three constraints. The posting and ledger tests, with twenty bills
  posted at once and two posts of one bill at once, ran eight times in a row without a failure. Still to do on the
  machine with .NET 10 and Docker: everything in `docs/runbook.md` section 8.
