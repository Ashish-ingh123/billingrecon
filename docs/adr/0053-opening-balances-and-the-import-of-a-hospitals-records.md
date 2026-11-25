# ADR 0053: Opening balances and the import of a hospital's records

- **Status:** Accepted. The owner approved the plan of Prompt 10.3 on 2026-11-25 with its four choices: opening
  balances per bill, opening documents that are the existing documents, credit balances brought in as advances, and
  the screen left to a Prompt 10.3b.
- **Date:** 2026-11-25
- **Deciders:** project owner, Claude Code
- **Builds on:** ADR 0005 (the subledger), ADR 0006 (idempotency), ADR 0008 (numbering), ADR 0020 (audit), ADR 0022
  (the order of a posting), ADR 0023 (maker-checker), ADR 0033 (prefixes), ADR 0058 (the reference hospital).
  **Amends:** ADR 0005 and `docs/database.md` section 5 (a seventh account, two posting kinds, identity 12, the gapless
  identity by origin); ADR 0008 (two more series, with prefixes that are not settings); ADR 0033 (a prefix change
  cannot name them); ADR 0015's note on defaults (one more default).
- **Implements:** BR-101, BR-104, BR-401, BR-404, BR-603, BR-801 for records that come from the system before.

## Context

A hospital that starts here does not start empty. Its patients exist, some owe on bills raised in the system before,
some have paid in advance. Without them the receivables report is wrong on the first day, and a payment for an old bill
has nothing to be allocated to. R1 had no way to bring them in but typing each one.

## Decision

1. **Two steps: the parties, then the balances.** A ledger posting moves the cached balances of a payer by a locked
   statement on the payer's row, so the payer must be saved before anything is posted for it. So patients and guarantors
   are imported and committed first (`POST /api/v1/imports/parties`), and the opening batch names them by MRN and by
   reference. The first step is reference data and needs one person; the second is financial and needs two.
2. **The import of parties** (`ImportPartiesHandler`). A patient is found by MRN, without regard to case: under the same
   name it is *matched* and left as it is; under another name the row is rejected, and nothing is merged or changed; a
   new one is registered with the self-pay payer it is. A guarantor is found by the reference it had before, which the
   payer keeps (`Payers.LegacyReference`, unique in the tenant when set). Each row is checked by the validators a
   user's own registration meets. One rejected row writes nothing; a dry run writes nothing. Because what is there is
   matched, the same files can be sent again, and the action takes no `Idempotency-Key`.
3. **Opening balances per bill.** Each unpaid bill of the system before becomes one opening receivable with its old
   number, bill date and due date, for what was outstanding. Ageing and the payer's statement stay true; one figure per
   payer would lose both.
4. **An opening receivable is a bill, an opening advance a payment.** `Bills.Origin` and `Payments.Origin`
   (`Standard`, `Opening`; default `Standard`, so every row before the column and every document a user raises says
   so) and `LegacyNumber`. `Bill.BringForward` makes a posted bill of one line with no tax; `Payment.BringForward` a
   posted payment whose reference is the old one. Allocation, credit notes, adjustments, refunds, statements and
   reports work on them unchanged, which three new kinds of document would each have had to copy. They are made by the
   approval of a batch only: no draft, no back-date window, no approval of their own.
5. **A credit balance comes in as an opening advance,** and says so in its notes. A credit note here belongs to a bill
   and is capped by it (BR-210, BR-211), and the bill behind an old credit balance is not imported. The payer can have
   it applied or refunded all the same.
6. **Series of their own.** `DocumentType` gains `OpeningReceivable` and `OpeningAdvance`. Their numbers are taken, like
   every number, from the locked series inside the transaction, in the fiscal year of the **cut-over date** (the
   document keeps its old date). Their prefixes, `OBR` and `OBA`, are the same for every tenant and are not settings:
   `Tenant.HasPrefixSetting` is false for them, a prefix change cannot name them, and no tenant can take either for
   another type. The unique index of a number's position gains the origin, since an opening receivable and a bill are
   both the first of their series.
7. **The ledger: a seventh account, `OpeningBalances`.** An opening receivable posts Dr Receivable(bill), Cr
   OpeningBalances (kind `OpeningReceivablePosted`); an opening advance Dr OpeningBalances, Cr UnappliedCash(payment)
   (kind `OpeningAdvancePosted`). So an opening receivable is not revenue here and an opening advance is not cash
   received here: the dashboard and the daily collection, which read Revenue and Cash, leave them out without knowing
   of them. Every opening posting is dated the day before the cut-over. Nothing else ever posts to the account.
8. **The batch** (`OpeningBatch`, module Receivables). Three CSV files for a cut-over date: bills, advances and a trial
   balance. The dry run, the submission and the approval check them through one class (`OpeningBatchChecker`), so the
   three cannot disagree. A row is rejected with its file, line and reason; the three totals must equal the trial
   balance to the paisa. The submission keeps the files as text (out of every audit image and never answered back),
   their counts and totals, and the SHA-256 of the date and the files, and opens an approval request (document type
   `OpeningBatch`, action `BringForward`).
9. **Approval is the posting** (ADR 0023). A second user, a Supervisor or TenantAdmin, approves. The kept files are
   checked again against the tenant as it is then, and one finding refuses the approval. Then, in the order of ADR 0022,
   each document takes its number, is added, and is posted; one commit writes the batch, its decision and all of its
   documents, or nothing. A batch holds at most 2,000 opening balances.
10. **Brought in once.** The hash is unique in the tenant among batches that wait or were approved (409
    `opening_batch_duplicate`); a rejected batch frees its files. And an old number is unique among the opening
    receivables, an old reference among the opening advances, so a row of another file that repeats one is rejected.
11. **An opening advance is never reversed** (422 `opening_advance_not_reversible`): a reversal credits Cash, and no
    cash came in here. What was brought in by mistake is refunded, with that as its reason.
12. **The reconciliation.** The gapless identity counts bills and payments by origin, each against its own series; the
    identities of a document without its posting look for the kind of its origin; and a new identity 12,
    `OpeningBalancesAccount`: the balance of the account equals the opening receivables less the opening advances.

## Consequences

- A credit note against an opening receivable debits Revenue for a bill whose revenue was never credited here: the
  period's net revenue is lower by it, as a return of an old sale would make it.
- The bill and payment registers list opening documents under their `OBR` and `OBA` numbers, by their old dates.
- A real export is mapped to the three layouts before it is imported (ADR 0058); the import reads no other format.
- The patients of a large hospital come in files of at most 5,000 rows, its balances in batches of at most 2,000, each
  part with its own trial balance.
- The two opening series are locked for the length of an approval. Only approvals take them, so nothing else waits.

## Alternatives considered

- **One transaction for parties and balances.** The unit of work would need to save in the middle of a use case
  without ending its transaction: a change to a port every use case relies on, for one use case.
- **New document types for opening balances.** Each would need its own allocation, crediting, refund and report.
- **Opening credit as a credit note without a bill.** It loosens BR-210 and BR-211 for every credit note.
- **Numbers from the bills' and payments' own series.** An opening receivable would look like a bill issued here, and
  the old bill's number would have no home.

## Built note (2026-11-25)

- The fixed prefixes are also written in the allocator's statement, which reads a tenant's prefix by document type;
  `OpeningBalancesApiTests` holds the numbers the two series give.
- `Bills.Origin` and `Payments.Origin` have a database default (`Standard`), an exception to the rule that the
  application sets everything: every row that existed needs the value, and the model says it rather than the
  migration being edited by hand.
- Tests changed because behaviour changed, none loosened: the tenant's prefixes are seven with two fixed, the schema
  lists name the new columns, indexes and constraints, the financial controllers include the batch's, and the
  decisions of an approval request are nine documents'.
- Verified on the office machine on LocalDB 2019; SQL Server 2022 and CI are owed (runbook section 8, row 10.3). The
  screen is Prompt 10.3b: the inbox already shows and decides a batch.

## Amended 2026-11-25 (Prompt 10.3b): the screens, and what one request can carry

The owner approved the plan of Prompt 10.3b with its three choices.

1. **Four screens under Administration, behind a third tab, Import,** for the TenantAdmin (`frontend/src/imports`): the
   two steps in their order with the batches that were submitted; the import of patients and guarantors; the import of
   opening balances; one batch with its state and history. No endpoint changed. On each import the files are checked
   first, and Import or Submit is offered only for files that were checked and had no rejected row; choosing another file
   closes it again. The approval stays in the inbox, where a batch already was.
2. **The files stay in the page.** A chosen file is read once into memory, used for the check and then for the request,
   and is gone when the page is left: nothing of it goes to the browser's storage or the address, since it holds
   patients' names and what they owe.
3. **The findings can be downloaded** as CSV, written in the browser with the reports' own quoting and formula guard: a
   real export can have hundreds of rejected rows, and they are worked through in the old system, not on this screen.
4. **The limits are what one request can carry.** The proxy takes a request of at most 1 MB (`client_max_body_size`,
   ADR 0045), and a request refused there never reaches the API's own message. So the API takes 600,000 characters of
   patients and 200,000 of guarantors in one request, and 250,000 of each file of an opening batch (they were 4,000,000
   and 2,000,000, which no request through the proxy could have carried); the rows per file and the documents per batch
   are unchanged. The browser weighs the request in bytes before sending and refuses one over 900,000 with a message to
   split the files. Raising the proxy's limit for every endpoint was the alternative, and would have weakened a
   protection for the sake of two requests.
5. **Verified** in Edge against the built app with a stubbed API (both themes, a desktop and a phone, axe, the keyboard,
   no layout shift, nginx's content security policy applied); through nginx against a real import it is owed
   (runbook section 8, row 10.3b). Backlog B37 is closed.
