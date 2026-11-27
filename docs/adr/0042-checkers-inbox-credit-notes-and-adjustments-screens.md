# ADR 0042: The checker's inbox, and the credit note and adjustment screens

- **Status:** Accepted. The owner approved the plan of Prompt 7.5 on 2026-11-23 "as recommended", with its four answers
  (points 1 to 4 below). The built note at the end records what the build found.
- **Date:** 2026-11-23
- **Deciders:** project owner, Claude Code
- **Builds on:** ADR 0023 (maker-checker), ADR 0025 (credit notes), ADR 0026 (adjustments), ADR 0027 and ADR 0028
  (reversals), ADR 0033 (prefix changes), ADR 0036 (reports), ADR 0039 to ADR 0041 (screens).

## Context

Prompt 7.5 asks for the checker's inbox (approve, reject with a comment, hidden from the maker), the drafting of credit
notes and adjustments, and the six reports of R1 with date filters and CSV export; it is done when the Phase 4 scenario
(a bill, a part payment, a credit note, an adjustment, an allocation reversal, a refund) can be performed in the browser
by three users. Eight kinds of request wait for a checker, each decided through its own document with that document's
version: bills, credit notes, adjustments and refunds to post; payments, allocations and applications of credit to
reverse; and prefix changes. The pending approvals report (ADR 0036) answered each request's payer, amount and waiting
days, but for an allocation or an application of credit only the child's own id: the payment or credit note through
which it is decided, and whose version a decision sends, could not be found from it. Nor did it answer the maker's reason,
which a checker reads before approving a reversal.

## Decision

1. **The prompt is split.** 7.5 builds the inbox, the credit notes and the adjustments, and is done with the three-user
   scenario. **7.5b** builds the six reports with their date filters and CSV export, with a plan of its own (whether CSV
   is written in the browser from every page or by an endpoint of the API is decided there).
2. **The pending approvals report answers two more fields**: `parentDocumentId` (for the reversal of an allocation, its
   payment; of an application of credit, its credit note; null otherwise) and `makerComment` (the maker's reason for a
   reversal or a prefix change, null for a posting). Every role can already read the reason in the request's audit trail,
   so nothing is shown that was hidden. A change of the API: one DTO, one query, `docs/api.md` section 8.15, the OpenAPI
   snapshot and the frontend's types; the dashboard's panel, which reads the same report, answers them too.
3. **Decisions are made in the inbox** (`/approvals`, the policy `Decide`): the requests that wait, the oldest first, with
   what each asks, its document (linked; an allocation or an application of credit to its payment or credit note), payer,
   amount, maker and reason, filtered by kind in the address. **Review** opens the request in place: its document read as
   it is now (its state among the facts shown), then **Approve** with an optional comment or **Reject** with a required
   one, sent with that document's version and the key of the operation (ADR 0040 point 2; a prefix change takes no key,
   ADR 0033). A request the user made shows "Yours: waits for another checker" and no review: the maker never decides
   (BR-603), and the API refuses it too. A document that waits says so on its own page and, to a checker, links to the
   inbox filtered to its kind.
4. **A credit note's lines come from its bill.** Each line of the bill can be credited by a quantity, at the bill line's
   price and tax rate, which the API takes from the line (`billLineId`); the quantity is not more than was billed and the
   line not more than the bill line came to; free-text lines are typed in full, as on a bill; the total is not more than
   the bill. The server checks again against the credit notes posted by then (BR-210, BR-211). The estimate is the exact
   arithmetic of ADR 0040 point 3.
5. **A credit note's credit is applied with the allocation form of 7.4**, made shared (`ApplyToBills`): the payer's bills
   that owe something, oldest first, "Apply oldest first", the caps checked in exact paise before sending.
6. **Credit notes and adjustments are drafted from the posted bill they correct**, as refunds from their source (ADR 0041
   point 2): `/credit-notes/new?billId=` and `/adjustments/new?billId=`; the lists offer no new one. An adjustment is
   offered for a bill that still owes; a rounding is less than one rupee; the amount is not more than the balance due.

## Alternatives

- **One prompt** (point 1): every screen of 7.5 in one change, about twice the largest so far.
- **The inbox built on the checkers' list** (`/approval-requests`, point 2): it answers the maker's comment but neither
  payer nor amount, and no parent either; the inbox would read every document of the page to show what it asks.
- **Decisions on each document's page** (point 3): eight pages each reading its pending request to know the maker; one
  place to decide is simpler and is where 7.6 links.
- **The bill's lines only** (point 4): no credit for something not on a line, such as goodwill.

## Consequences

- The dashboard of 7.6 links its approvals panel to `/approvals`, and follows its rule: no review of one's own request.
- A prefix change has no page of its own (no prompt builds the administration screens); the inbox shows its type and the
  two prefixes.

## Built (2026-11-23)

Built as decided, with no package and no migration. What the build found:

- **The report's two fields** are filled from rows the query already joined: the allocation's payment and the
  application's credit note. `ReportsApiTests` asserts both for a waiting allocation reversal and a waiting reversal of an
  application, and the reason and null parent of the requests it had.
- **`docs/api.md` section 8.6 said `documentType` is `Bill` "so far"**; it now names the eight kinds and three actions.
- **`ReasonForm` gained `optional` and `tone`**, for the approval's optional comment and its primary button.
- **`useBill` takes `enabled`**, so a form that does not know its bill yet reads nothing; the open bills of a payer
  (`usePayerOpenBills`, `mostOpenBills`, `BillSummary`) moved from the payments module to the bills module, where both
  forms that apply money read them.
- **A conflict in the inbox** compares the facts the checker was shown, the state among them: "State: was Waiting for
  approval, now Posted" says that someone else decided first.
- **A test that opened `/approvals` as a page without requests** (moving between sections) now gives it an empty inbox to
  read; its expectations are the same.
- **The bundle passed 500 kB** (586 kB minified, 169 kB gzip), and Vite warns. Splitting the screens by route is left to
  the delivery phase.
- **Tests:** 34 new frontend tests: the inbox (what waits, the maker's own request without review, an approval of each
  of the eight kinds with its version and key, a rejection that asks for its comment, an approval's comment, a decision
  that came late, the maker refused by the API, the filter in the address, closed to a clerk and a cashier); credit
  notes (lines from the bill and free lines with their estimate and body, a line above its bill line and a total above
  the bill refused, the API's refusal of a line at that line, a correction by `PATCH` then `PUT`, submission, a cashier
  who only reads, the actions of a posted one, applying credit oldest first, the reversal of a later application only;
  the bill's corrections and actions by role); adjustments (a rounding of a rupee refused, an amount above the balance due
  refused to the paisa, a correction by `PUT`, submission, the API's refusal, void, closed to a cashier, the link to the
  inbox). With the review shown to the maker, a decision sent without its version, a rounding of a rupee accepted, a
  quantity above the billed one accepted, a line above its bill line accepted and an empty comment sent as text, one at
  a time, each is caught. The whole `Database` collection passes on LocalDB (491).
- **In the browser**, the Phase 4 scenario by three users against the API in Development on a throwaway LocalDB database:
  a clerk posted a bill of 716.00; a cashier recorded 600.00, allocated 400.00 to it, asked for that allocation's reversal
  and submitted a refund of 200.00; the clerk drafted a credit note of the dressing line (a quantity of 3 refused against
  the 2 billed) and a discount of 90.00 (a rounding of 1.00 refused), submitted both, and found the inbox closed; a
  supervisor approved the four requests from the inbox, which named both makers and showed the reversal's reason and its
  payment. The bill then owed 500.00, the payment held 400.00, the credit note, the adjustment and the refund were
  numbered, and the reconciliation found no drift.

## Amended 2026-11-27 (Prompt 12.2b): the lists of credit notes and adjustments on the shared table

The lists of credit notes and adjustments are drawn by the shared table of ADR 0063, with their filters in the bar
(the search, status, for adjustments the kind, the date with its shortcuts, total or amount from and to; "Made by" for
an administrator) and sortable headers (number, date, total or amount, and for credit notes the available amount). A
list opened for one bill or one payer keeps it through "Clear filters". Drafting from the bill's page is unchanged. The
checker's inbox takes the shared table, with the requester as a filter, in 12.2c.
