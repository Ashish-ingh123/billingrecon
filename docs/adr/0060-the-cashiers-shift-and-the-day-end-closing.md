# ADR 0060: The cashier's shift and the day-end closing

- **Status:** Accepted. On 2026-11-27 the owner approved the requirements of Prompt 11.2 "as recommended" (a cash
  payment needs an open shift; a difference is recorded and not posted; a Supervisor or TenantAdmin who is not the
  cashier approves a closing with a difference) and then the plan "as written", with its split into 11.2a (this
  record: the API) and 11.2b (the screens and the closing slip, which will amend this record) and its three points: a
  cash payment reversed after its shift is closed leaves the shift as it was; a shift keeps the business date of its
  opening; the count of cash documents on a shift raises no audit record of its own.
- **Date:** 2026-11-27
- **Deciders:** project owner, Claude Code
- **Amends:** ADR 0005 (a ledger posting may name a shift), ADR 0023 (a tenth kind of approval request and a fifth
  action), ADR 0029 and ADR 0027 (the approval of a cash refund and of the reversal of a cash payment look for a
  shift), ADR 0032 (three policies), ADR 0036 (a ninth report, which is not every role's).
- **Builds on:** ADR 0003 (the module rule), ADR 0007 (row versions), ADR 0020 (audit by events), ADR 0021 (typed-id
  foreign keys), ADR 0022 (the order of a posting use case), ADR 0033 (a maker-checker document that is not financial).

## Context

A hospital's cash counter works in shifts. A cashier starts with a float, takes cash and pays some out, and at the end
counts the drawer; someone compares the count with what the system says was taken; a difference is explained and
signed off. R1 has the daily collection by method (US-42) for the hospital as a whole, and nothing per cashier: a
shortage cannot be laid at a drawer, and nobody attests to a count.

Three things had to be settled before any design, and the owner settled them on 2026-11-27: cash needs an open shift;
a difference is recorded, not posted; a second person approves a closing with a difference.

## Decision

1. **A shift is an aggregate of Payments, `CashierShift`**, with the states `Open`, `PendingApproval` and `Closed`. It
   belongs to the user who opened it, carries a float of zero or more and the business date in India of its opening,
   and holds no money of its own. A user has at most one shift that is not closed (BR-310): the use case asks, and a
   filtered unique index (`[Status] <> 'Closed'`) answers for two openings at the same instant.
2. **A ledger posting names the shift whose drawer it moved cash in or out of**: a nullable `LedgerPostings.ShiftId`,
   set when the posting is written and never after (the ledger is append-only). The posting template carries it; the
   use case sets it. Receivables answers "the Cash entries of the postings that name shift X" through a new contract,
   `IShiftCash`, and that is the only source of a shift's expected cash (BR-312). Payments never reads the ledger's
   tables and Receivables never reads Payments': no new exception to the module rule, and no raw SQL. The foreign key
   `FK_LedgerPostings_ShiftId` comes from the context's convention for typed ids (ADR 0021).
3. **Cash needs an open shift (BR-311).** Recording a payment whose method is Cash looks for the open shift of the
   user who records it: 422 `shift_not_open` without one. A cash refund is paid out of the open shift of the user who
   drafted it, found when a checker approves it: 422 `shift_not_open` when that user has none; the refund still waits.
   Card, UPI, bank transfer and cheque need no shift and name none.
4. **A cash document in a shift carries the business date of the shift.** A cash payment sent without a date takes it;
   one dated another day is 422 `shift_date_mismatch`; so is the approval of a cash refund dated another day than the
   shift of its drafter. A shift that goes on past midnight keeps the date of its opening, so cash taken after
   midnight is dated the day before, within `MaxBackdateDays`; with that setting at 0 the cashier closes the shift and
   opens another. Every posting that names a shift therefore has the shift's business date, and that is what makes
   the day-end report equal the daily collection, which goes by business date (point 9).
5. **A cash payment that is reversed.** When the reversal is approved while the shift that took the payment is still
   open, on that shift's business date, the reversal's posting names the shift and its expected cash falls.
   Otherwise the shift is not touched: a closed shift never changes (BR-315), and the reversal is cash outside the
   shifts in the day-end report. A shift that waits for approval is not open and takes nothing.
6. **Closing is a count by denomination** (BR-313): the pieces of 2000, 500, 200, 100, 50, 20, 10, 5, 2 and 1 rupee,
   and the paise as loose change below one rupee. The list is fixed in the domain (`CashCount.Denominations`). The
   count is one value of the shift, held as its canonical text in one column, not rows of a table of its own (see the
   alternatives). The expected cash is the float and the shift's Cash entries; the difference is counted less
   expected. With no difference the shift is closed at once.
7. **A difference is recorded, never posted (BR-314).** It is a column of the shift with its required reason. The
   ledger stays a receivables subledger whose Cash account is the money the documents say was received: no eighth
   account, no new document type, no new line in `CK_LedgerEntries_Subject`, and the twelve reconciliation identities,
   the daily collection and the dashboard's "collected" are untouched. The accountant's export of Prompt 11.4 may
   carry a shift's difference as a voucher of its own.
8. **A closing with a difference is a maker-checker request** in the shape of ADR 0023: the closing answers 202 and
   opens an `ApprovalRequest` (`CashierShift`, action `Close`) in its own commit, with the cashier as the maker;
   `approve` and `reject` are POST actions of the shift; the checker holds Supervisor or TenantAdmin and is not the
   maker, so a Supervisor who ran a shift cannot approve its closing. An approval closes the shift. A rejection, with
   its comment, opens it again without its count, to be counted again; the audit record of the rejection keeps the
   count that was refused. While a closing waits its cashier opens no other shift.
9. **The day-end report (BR-903)** is the ninth report, `GET /api/v1/reports/day-end?date=`: every shift of a business
   date with the Cash entries of the postings that name it, and one more line, the cash of that date that no shift
   holds. Their sum equals the Cash row of the daily collection of the date, always, because both are the same ledger
   entries cut two ways. It is built in Reporting's Infrastructure beside the daily collection and writes nothing.
10. **A closing and a cash document at the same instant.** Every cash document that joins a shift raises the shift's
    `CashDocuments`, and so its row version; a closing saves the shift under the version it read before it read the
    ledger. Whichever commits second answers 409 `concurrency_conflict`: a shift is never closed on a figure the ledger
    no longer has, and a payment is never taken into a shift that has just closed. No lock is taken and no raw SQL is
    sent. The count is not a state change of its own and raises no event: the audit record of the payment or the
    refund says what happened, and one more record per payment would say it twice.
11. **Not a financial document, so no `Idempotency-Key`.** A shift has no number and posts nothing. A second opening
    is refused by the rule of one shift at a time, and a closing, an approval or a rejection sent again by `If-Match`.
    `CashierShiftsController` is listed with the controllers that are not financial (`IdempotencyConventionTests`).
12. **Three policies** (ADR 0032): `WorkShift` (TenantAdmin, Supervisor, BillingClerk, Cashier: whoever may record a
    payment, because a BillingClerk who takes cash needs a shift too) for opening and closing; `ReadShifts` (every
    role) for reading, where a Cashier and a BillingClerk are given the shifts they opened and 404 for any other, by
    the use case (`ShiftReaders`); `DayEnd` (TenantAdmin, Supervisor, Viewer) for the day-end report and for the trail
    of a shift. Deciding a closing is the existing `Decide`.

## Consequences

- A cashier without an open shift can take a card or a UPI payment and cannot take cash. That is the control, and it
  is a change of behaviour for every existing caller that records cash: the demo seed, the load test and the browser
  journey open a shift first, and every test that recorded cash without one does too.
- A cash payment cannot be back-dated on its own: it is dated the day of its shift, and a shift cannot be opened for a
  past date. Cash that was taken yesterday and not entered is entered today, with a note. This is in
  `docs/backlog.md` (B38) as a limitation to review with a client.
- A cash refund that waited overnight for its checker is dated the day it was drafted, and the drafter's shift of that
  day is closed by then: its approval is refused, and it is rejected and drafted again with today's date. Strict, and
  what keeps the day-end equal to the collection.
- Two checkers who approve two cash refunds of one cashier at the same instant touch the same shift: one answers 409
  and is sent again. So does a cashier's payment against a checker's approval of that cashier's refund. Rare, and the
  same answer every other stale write gives.
- A cash payment costs one more row update, the shift's. The load test of Prompt 8.2 records only cash, each user in
  a shift of their own: its p95 is to be measured again on the personal machine (runbook section 8, row 11.2).
- The Cash entries recorded before this migration name no shift: in a day-end report of an earlier date they are all
  "cash outside the shifts". Nothing is back-filled.
- `ApprovalDocumentType` and `ApprovalAction` gain a value each, and their CHECK constraints are rewritten by the
  migration. The pending approvals report shows a shift's request without a payer or an amount, as it shows a prefix
  change; `PendingApprovalPolicies` shows it to who may decide, and `AuditTrailPolicies` gives its trail to `DayEnd`.

## Alternatives considered

- **Post the difference to an eighth ledger account** (cash over and short). It would make the Cash account "money in
  the drawers" instead of "money the documents say was received", and add a term to the daily collection, the
  dashboard and the reconciliation. The subledger is about receivables; a drawer's shortage is not a payer's. Rejected
  on the owner's decision.
- **A `ShiftId` column on `Payments` and `Refunds`** instead of on the ledger posting. Then the expected cash would be
  a sum of documents, not of ledger entries, or Receivables would have to read Payments' tables to find a shift's
  entries. The posting is where the cash moved, and it already has the business date and the user.
- **The count as rows of a child table.** A rejected closing is counted again, so its rows would have to be deleted
  or kept beside the new ones: a fifth table with hard deletes (rule 10), or a count with attempts. One column with a
  fixed list of ten denominations is displayed and audited as it is, and the audit record of a rejection holds the
  refused count. The plan of 11.2a named child rows; this was changed while building, for that reason.
- **A database lock on the shift** (`UPDLOCK`) to serialise a closing with a payment. It needs a seventh file with raw
  SQL (ADR 0021). The row version does it with what every aggregate already has.
- **A closing that waits for a checker even without a difference.** More ceremony for the ordinary case and no gain:
  a count equal to the ledger has nothing to approve.
- **Every payment in a shift, whatever its method**, with only the cash counted. It would refuse a card payment to a
  clerk with no shift for no control gained. The cashier's takings by other methods are in the payment register.

## Built note (2026-11-27, Prompt 11.2a)

- Migration `0029_CashierShifts`: the table `CashierShifts` with nine CHECK constraints (the float, the loose change,
  counted if and only if not open, the difference equal to counted less expected, a reason if and only if a
  difference, only a difference waits, closed if and only if closed), its two indexes, `LedgerPostings.ShiftId` with
  its filtered index and foreign key, and the two wider CHECKs of `ApprovalRequests`. No hand-written line: the table
  is neither append-only nor hard-deletable, so it takes the schema's grants.
- The domain: `CashierShift`, `CashCount` (a `readonly record struct` over its canonical text, as a value object
  stored through a converter must be), `CashierShiftStatus`, five events (`CashierShiftOpened`, `...Closed`,
  `...SubmittedForApproval`, `...ClosingApproved`, `...ClosingRejected`) and seven error codes in `PaymentsErrors`.
- The three use cases that move cash changed: `RecordPaymentHandler` (the shift is found with everything else that can
  refuse, before the number), `ApproveRefundHandler` and `ApprovePaymentReversalHandler`. A request without a user has
  no shift.
- The checker's inbox of the frontend learned the tenth kind in this prompt, because the generated types made every
  list of kinds incomplete: it reads the shift and sends the decision with `If-Match` and no key. The screens of the
  shift itself are 11.2b's.
- Existing tests that changed, none loosened: every database test that records cash opens the shift of its user first
  (`Support/ShiftsApi`: `EnsureShiftAsync`; `FreshShiftAsync` for the dashboard's scenario, which moves through the
  days); `SettingsApiTests` back-dates a UPI payment where it back-dated cash, because cash is no longer back-dated;
  `RecordPaymentTests` gives its handler a user with an open shift; four lists grew by the new table, key, index,
  endpoints and controller; the count of decisions in `PermissionConventionTests` is 20.
- The demo seed takes cash only today, in the cashier's shift, and the earlier days' money by UPI and card.
- Verified on .NET 10 with LocalDB 2019 in a throwaway copy, not on SQL Server 2022: see runbook section 8, row 11.2.

## Amended 2026-11-27 (Prompt 11.2b): the screens and the closing slip

1. **One section, Shifts** (`/shifts`, every role; `frontend/src/shifts`). Its page shows the user's own shift first,
   to those who may work one: none, with the way to open one; open, with what the drawer should hold and the way to
   count it; or waiting for approval. Below it is the list, a cashier's own or all (the API decides), by status and
   business date in the address. Opening is `/shifts/new`, a shift's page `/shifts/:id`, counting `/shifts/:id/close`.
2. **The count is added up in the browser in exact paise** (`shifts/api.ts`: `countedPaise`, on `BigInt`, as every
   amount the browser compares, ADR 0041) and shown against the expected cash the API read from the ledger, with the
   difference in words ("Short by", "Over by"), never a bare sign. The expected cash is shown before the count is
   sent, as the plan said; a blind count was not asked for. With a difference the form asks its reason and its button
   says that the closing goes for approval; the API adds the count up again and is the authority.
3. **A conflict starts the count again.** When cash joined the shift while it was counted, the closing answers 409;
   the page reads the shift again and the form starts from its new version and expected cash, with empty boxes: no
   count is carried over to a figure it was not made against.
4. **Recording cash** (`RecordPaymentPage`): when Cash is chosen the user's shift is read. Without an open one a
   banner says so with the way to open one, and Record payment is disabled; with one, the payment date is the
   shift's business date and cannot be changed. The API refuses either way (BR-311).
5. **The closing slip** is `/shifts/:id/print`, for whoever may read the shift, once it is counted: `SheetFrame` with
   a pure body, `ShiftSlipBody`, as the three printed documents are drawn (ADR 0059). It has **print settings of its
   own**: a fourth `PrintedDocumentType`, `ShiftClosing` (A4, A5 or the roll; the letterhead, margins, signature
   block, amount in words, footer and terms apply; the payer, patient contact and cashier switches do not, and the
   settings screen does not offer them). **Its print is not recorded** and it is neither original nor duplicate: it
   is no financial document. Its mark is where the shift stands, closed or waiting for approval. Migration
   `0030_ShiftClosingSlip` widens the CHECK of `PrintSettings.DocumentType`, and that of `DocumentPrints`, which shares
   the enum, though no row of that kind is written: `POST .../prints` exists for bills, payments and refunds only.
6. **The day-end report** is `/reports/day-end` (policy `DayEnd`), in the reports index for those roles only, with the
   CSV of the other reports and a link to the daily collection of the same date.
7. **The browser journey** opens the cashier's shift of today through the API with the page's own token: the journey
   is about the payment, and the shift screens have their own tests.

### Built note of the amendment

- A difference the API answers below zero is read with `signedPaise`; `paiseOfServer` refuses a negative amount, which
  the component tests found on the first shortage.
- No package, no change of nginx's policy, no inline style: the slip takes its paper and margins through
  `useLayoutRules`, as the other sheets do.
- Verified: 656 component tests; the seven shift routes of `e2e/routes.spec.ts` in Edge against the built app, both
  themes, desktop and phone, with axe and nginx's policy applied by the test. Not verified: through nginx, on SQL
  Server 2022, and on paper (runbook section 8, row 11.2b).
