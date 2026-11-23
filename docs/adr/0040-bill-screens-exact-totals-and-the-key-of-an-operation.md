# ADR 0040: The bill screens: exact totals in the browser, the key of an operation, and one editor for a draft

- **Status:** Accepted. The owner approved the plan of Prompt 7.3 on 2026-11-23 "as recommended", with its four answers
  (points 1 to 4 below). The built note at the end records what the build found.
- **Date:** 2026-11-23
- **Deciders:** project owner, Claude Code
- **Builds on:** ADR 0039 (screens), ADR 0006 (idempotency), ADR 0004 (money and rounding), ADR 0022 and ADR 0023
  (posting, approval).

## Context

Prompt 7.3 builds the bill screens: the draft editor with lines from the catalogue or free text and live totals that
mirror BR-202 and BR-203 while the server's figures are always the ones shown after a save; void; post with an
`Idempotency-Key` reused on retry; and the bill's view with its status, balance and audit trail. The arithmetic of a line
is exact decimal arithmetic on the server, rounded once per product to the paisa, half away from zero; a JavaScript
number is a binary float and gets such sums wrong in the last place. A financial POST must carry one key for one
operation, sent again unchanged with exactly the same request when it is retried (`docs/api.md` section 7). A bill in a
list carries the ids of its patient and payer, not their names.

## Decision

1. **One editor for a draft**, its header and its lines on one page. A new draft is one request (`POST /bills` with its
   lines). Saving an existing draft sends what changed: the header (`PATCH`), then the lines (`PUT .../lines`) with the
   version the first answer gave, each with `If-Match`. When the header is saved and the lines are refused, the screen
   says so, keeps the saved version, and the next save sends the lines alone.
2. **The key of an operation lives in memory** (`useIdempotentRequest`): made when a create, a void or a post is first
   sent; sent again with the same request, byte for byte, when the user retries after no answer, a fault (5xx) or 409
   `idempotency_in_progress`; dropped after a success or a refusal; and a different request (a changed form, a new
   version) is a new operation with a new key. A reload in the middle loses the key: a second post is then refused by
   `If-Match` (the bill moved on), and a second create makes a second draft, which is voided.
3. **Live totals by exact decimal arithmetic written here, on `BigInt`** (`src/bills/lineMath.ts`, no package): the
   amounts are scaled integers; `lineAmount = round(quantity x unitPrice)`, `taxAmount = round((lineAmount -
   discountAmount) x taxRatePercent / 100)`, each rounded once to two places, half away from zero, as the server's
   `LineCalculator` does; sums are never rounded again. The editor labels them an estimate until saved; after a save the
   screen shows the server's figures.
4. **The names in a list are read per patient and payer**, each once and kept in the query cache, rather than added to
   the API's bill summary.
5. **Who may:** the API's `WriteBills` (TenantAdmin, Supervisor, BillingClerk) drafts, edits, voids and posts; everyone
   reads (`ReadBills`) and reads the audit trail (`ReadAudit`). Approve and reject are Prompt 7.5's; a bill waiting for
   approval says so.
6. **Posting** answers 200 with the number, or 202 when the total reaches the hospital's threshold: the screen says
   "posted as BIL/..." or "sent for approval". A void needs a reason and asks once more.
7. **The audit trail** of a record is one component for every screen: its records oldest first, what happened, when, by
   whom ("you" for the signed-in user, otherwise the user's id: only a TenantAdmin may read users), the reason, the
   document number, the correlation id, and the stored record before and after, folded.

## Alternatives

- **Two forms for a draft** (the header, the lines), each saved alone: simpler saves, a worse editor.
- **The key in `sessionStorage`**: survives a reload, but a stale key would replay an old answer for a request the user
  no longer means; the API's other guards cover the reload.
- **`decimal.js`**: a well-known package for the same arithmetic, one more dependency for a dozen lines.
- **Names in the bill summary**: one request per page, but a change of the API for a screen.
- **Live totals in floating point**: wrong in the last paisa at a midpoint; the user would see one figure and the
  server keep another.

## Consequences

- The client never sends a computed amount: it sends quantities, prices, discounts and rates, and shows the server's
  amounts once saved. A disagreement between the estimate and the server is a defect of `lineMath.ts`, which its tests
  hold to the documented example and the midpoints.
- The pickers, the audit trail, the bill status badge and `useIdempotentRequest` serve 7.4 and 7.5.
- A list of 20 bills may make up to 40 small reads the first time; the cache keeps them for the session's later pages.

## Built (2026-11-23)

Built as decided, with no package. What the build found:

- **One set of cases holds both arithmetics.** `frontend/src/bills/lineCases.json` lists eighteen lines (the documented
  example, midpoints a binary float rounds down, thirds, a discount of the whole line, the largest line) with what each
  comes to. The frontend's `lineMath.test.ts` and the Domain's `LineCalculatorSharedCasesTests` both read it, so a case
  that the browser and the server compute differently fails on the side that is wrong. With the midpoint rounded down
  in `lineMath.ts`, thirteen browser tests fail.
- **Seen against the API**: a draft of a catalogue line (3 x 333.3333 less 0.50 at 5%) and a free-text line (7 x
  142.8571 less 0.99 at 28%) showed 1,049.48 and 1,278.73, 2,328.21 in all, as the estimate; the server stored the same
  to the paisa; after a correction of the quantity, 2,511.07 on both sides.
- **The parties are asked for with the fields**: a draft without a patient or a payer says so together with the lines'
  messages, not after them (React Hook Form calls the save only when its own fields are valid, so the parties are
  checked before it).
- **A bill without a number is named by its state**: "Draft bill", "Bill waiting for approval", "Voided draft"; in the
  list its number reads "No number" and the status says which.
- **The trail names the development user by its id**, as any other user who is not the reader: a TenantAdmin's user
  list is the only place a name could come from.
- **The shell tests that used `/bills` as a page without requests** moved to `/reports`, still a placeholder: the bill
  list now asks the API for its bills, which took the answer those tests had prepared for the sign-in.
- **Tests:** 42 new frontend tests (the shared cases and the totals; the list's filters; a bill's view by role; posting
  with its key, 200 and 202, the same key after no answer and while running, a new key after a refusal, a conflict;
  voiding with its reason; a new draft with its parties, a catalogue and a free-text line, its estimate and its body;
  the same key on an unchanged retry and a new one after a change; a line's message at its field; the edit's `PATCH`,
  `PUT`, both with chained versions, and the header saved when the lines are refused), and 19 in the Domain. With a new
  key every time, "still running" not retryable, midpoints rounded down, the lines always sent and a 202 taken as
  posted, one at a time, each is caught.
- **In the browser**, against the API in Development on a throwaway LocalDB database with an approval threshold of
  5,000: a BillingClerk drafted the bill above with the patient's own payer chosen for them, corrected its notes and a
  quantity (a `PATCH` and then a `PUT`), posted it as BIL/26-27/000001 with the trail Drafted, Header updated, Lines
  replaced, Posted; sent a bill of 9,000 for approval (202), and voided a draft with its reason; a Viewer saw the list
  with names and statuses, a bill with no action, and no access to a new bill.
