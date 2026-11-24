# ADR 0049: The money screens: money, statuses, totals and the history of a record

- **Status:** Accepted. The owner approved the plan of Prompt 9.3 on 2026-11-24 as one prompt, then, while it was being
  built, split it in two: 9.3 the shared components with bills, credit notes and adjustments; 9.3b payments, refunds,
  the payer statement and the checker's inbox. 9.3b amends this ADR rather than taking a number of its own, so 9.4 keeps
  ADR 0050.
- **Date:** 2026-11-24
- **Deciders:** project owner, Claude Code
- **Builds on:** ADR 0047 (tokens, icons, motion), ADR 0048 (the shell and the shared components), ADRs 0040 to 0042
  (what the bill, credit note and adjustment screens do).

## Context

After 9.2 every screen wore the new shell, but the money itself was still R1's: about 95 places wrote
`` `₹${formatDecimal(x)}` `` by hand, five modules each kept their own table of status words and colours (the bill a
badge of its own with no icon), a document's totals were a plain list at the foot of its lines, a bill waiting for
approval said so in a line of grey text, and the audit trail was a table of six columns. Prompt 9.3 gives money,
statuses, totals and the history one shape each, and applies them to bills, credit notes and adjustments, with no
change of what any screen sends or computes.

## Decision

1. **Money** is `formatMoney` (text) and `<Money>` (an element): the rupee sign, Indian grouping, two decimals, tabular
   figures. Its text is exactly what the screens wrote before (`₹1,049.48`), so every test that reads an amount reads the
   same characters. A negative amount reads `−₹12.50 negative`, a credit `−₹500.00 credit`: a sign and a word, never a
   colour alone. Nothing is computed: the value is the server's, or the editor's own estimate from `lineMath.ts`, which is
   not touched.
2. **One mapping of statuses**, `components/documentStatuses.ts`: every status of the bill, the credit note and its
   applications, the adjustment, the payment and its allocations, the refund, and Active and Inactive, each with its word,
   its tone and its own icon (a pencil for a draft, a clock for what waits, a stamp for posted, a half disc for partly
   paid, a tick for settled or applied, a barred circle for voided, a turning arrow for reversed). Each module's label
   table is this mapping (a test holds them the same object); the types make a status of the API without an entry a
   build error. `BillStatusBadge` and `StatusBadge` draw from it; the bill's own stylesheet is gone.
3. **The totals card**, `TotalsCard`: the parts quiet, a total larger and bolder, the balance due under a rule; an
   estimate says "Estimate" in its head and why underneath until the server's figures replace it. In the editors it stays
   in view while the lines scroll (`position: sticky`). A figure that changes is marked for a moment by a soft ground
   behind it (opacity only); the figure never moves, and the first figures are not marked.
4. **The banner**, `Banner`: a document's state across its page, with a word, an icon and a soft ground of its tone (new
   tokens `--accent-soft`, `--success-soft`, `--warning-soft`, `--danger-soft`, held by the contrast test for text and
   for the tone's own colour). A bill, credit note or adjustment waiting for approval shows "Waiting for approval" with
   the same sentence as before; a voided one "Voided" with its reason.
5. **The history as a timeline**: the audit trail is an ordered list, a rail with a marker per change, its icon by the
   kind of action, the action, the document number, when and by whom, the reason on its own ground, and "Before and
   after" folded as before. The words are the ones the tests read.
6. **Fields**: a unit drawn inside the field (`leading="₹"`, `trailing="%"`), hidden from assistive technology because
   the label already says it; a required mark ("Required" beside the label, `aria-required` on the control), shown on the
   dates and the amount of the money forms. The form still checks; no native `required` is added, so nothing a form sends
   or refuses changes.
7. **Pages**: the bill's page has its parties in a card, its lines, then the totals card in the lines' section; credit
   notes and adjustments show their facts beside a card of their figures (one column on a phone, the figures first). The
   lists of bills, credit notes and adjustments show an empty state of their own. The no-access, not-found and
   placeholder pages are centred cards with a drawing and a way back.
8. **Motion**: a line added to an editor eases in; a changed total is marked; an action button that is being sent says
   so by its words and a turning mark (`aria-busy`); the first field the API refused takes the focus, and so comes into
   view. With reduced motion nothing moves.

## Alternatives

- **The figures repeated at the top of the bill.** Not done: the same amount twice on the page makes every test and
  every screen reader read it twice; the totals card is in the lines' section, where it was.
- **The inbox as cards in 9.3.** Moved to 9.3b with the split. Its test finds the requests as table rows; 9.3b decides
  between a list of cards (the test then finds list items, with the same assertions) and rows drawn as cards.
- **A line that eases out when it is removed.** Not done: React removes it at once, and keeping it would delay what the
  form holds. A line that arrives eases in.

## Consequences

- The journey of 8.2 waits for the payment's page after "Allocate" before it checks that the link is gone. On the
  allocate page the link is absent too, so the step used to go on while the allocation was on its way and could cut it
  off (seen once in this prompt's run: the API logged 499). A stricter step, not a weaker one.
- A bill's test reads the totals card and the timeline; the line-error test also checks that the refused field has the
  focus.

## Built (2026-11-24)

- **Verified on the office machine, installed Edge:** 422 component tests (12 new), twice in a row; lint, format, types,
  audit and the build clean. In the browser against the built app with nginx's policy applied in the test: the bill
  editor, a posted bill, a credit note and an adjustment in both themes at 1440 and 390 px (no sideways scroll, no
  console error, no policy refusal, no request the test did not expect, no layout shift), the totals card in view while
  the lines scroll, nothing animated with reduced motion; with the shell and sign-in tests, 31 browser tests. The three
  steps of the 8.2 journey passed through the new screens against the API on LocalDB 2019 (Vite's dev server).
- **Found and fixed while building:** the editor's line fields overflowed a phone (the grid's columns took the inputs'
  own width); the editor shifted when the patient arrived (the chosen state of a picker was shorter than its search).
- **Screenshots:** `docs/ui/bill-editor(-dark).png`, `bill-editor-phone(-dark).png`, `bill-posted(-dark).png`,
  `credit-note(-dark).png`, `adjustment(-dark).png`; the shell's were taken again (badges with their own icons).
- **Not verified:** these screens through nginx on the personal machine (runbook section 8, row 9.3). Once in the run,
  LocalDB answered an allocation with "a severe error occurred" while the disk was almost full; it did not recur.
- **For 9.3b:** the meter of what is left to apply, the payments, refunds, the statement's four figures and the inbox's
  cards, with `Money` and the status mapping, which those screens already read for their labels.

## Amended 2026-11-24 (Prompt 9.3b): the cashier's screens and the inbox

Approved by the owner with its one changed expectation (the inbox's test finds cards, not rows).

1. **The meter**, `ApplyMeter`, in the shared `ApplyToBills` (allocating a payment, applying a credit note's credit):
   a native `<meter>` of the applied amount against the cap, the same sentence of figures as before ("Unapplied cash:
   ₹1,500.00 · Allocated here: … · Left: …", read aloud as it changes), and, before anything is sent, a warning in words
   with the amount it is over by. It reads the paise the form already held; `money.ts` is not touched. It sits under the
   bills and stays in view while they scroll.
2. **The payment's and the refund's pages**: their facts beside a figures card (the amount, and the payment's unapplied
   cash under a rule); waiting for a reversal, waiting for approval, reversed and voided are banners with the sentences
   they had.
3. **The payer statement**: four tiles (owed on bills, unapplied cash, available credit, net outstanding, the net with
   an accent edge), two by two on a phone. The tiles stand from the first paint with a placeholder, and the three lists
   are drawn together once all three have arrived, so that nothing moves as they come (each list arriving on its own
   pushed the ones below it down: a layout shift of 0.03 to 0.14).
4. **The inbox**: each request a card in a list named "Requests that wait, oldest first": the kind with its icon and the
   document, when it was asked and how long it has waited (two days or more in the warning colour), the payer, the
   amount, the maker and the reason; Review opens inside the card. The maker's own request is a quieter card that says
   "Yours: waits for another checker" and offers no review. A decision is announced in a success banner.
5. **Fields and lists**: the amount of a payment and of a refund carries ₹ and the required mark, and so do their dates
   and methods; the payments and refunds lists and the statement's three sections have empty states of their own.

**Changed expectation:** `Approvals.test.tsx` finds the requests as the list's items instead of the table's rows; every
assertion on a request is the same, and two are added (the maker's card has no Review, the other's one).

**Built and verified (office machine, installed Edge):** 422 component tests, twice; the payment's figures, the
statement's four figures against the API's, and the meter's warning and value are asserted in the existing tests. In the
browser, `e2e/cashier.spec.ts`: the payment, allocating, a refund, the statement and the inbox, in both themes at 1440
and 390 px under nginx's policy, with nothing refused, no console error, no request the test did not expect and no layout
shift; the meter in view and warning; nothing animated with reduced motion (22 tests). With the other suites, 53 browser
tests. The shared helper of the stubbed suites is now `e2e/stubbed.ts`. The three steps of the 8.2 journey passed against
the API on LocalDB 2019. Screenshots: `docs/ui/allocating*.png`, `statement*.png`, `inbox*.png`.

**Seen and not fixed (backlog B35):** during the journey LocalDB 2019 answered two dashboard queries (summary and drift)
with "a severe error occurred on the current command" (500); the dashboard read them again and the step passed. The same
fault answered one allocation in 9.3's run. It is the database engine, not the screens; it is to be watched on SQL Server
2022.

**Left as it is:** on a phone the allocation table scrolls sideways to its amount column, as every table does since 9.2.
**Not verified:** through nginx on the personal machine (runbook section 8, row 9.3b).
