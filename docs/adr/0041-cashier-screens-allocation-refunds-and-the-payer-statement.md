# ADR 0041: The cashier screens: allocating oldest first, refunds from their source, and the payer statement

- **Status:** Accepted. The owner approved the plan of Prompt 7.4 on 2026-11-23 "as recommended", with its three
  answers (points 1 to 3 below). The built note at the end records what the build found.
- **Date:** 2026-11-23
- **Deciders:** project owner, Claude Code
- **Builds on:** ADR 0039 (screens), ADR 0040 (the key of an operation, exact amounts), ADR 0024 (allocation), ADR 0027
  (reversals), ADR 0029 (refunds), ADR 0036 (reports).

## Context

Prompt 7.4 builds the cashier's screens: record a payment, allocate it to bills (the payer's outstanding bills, the caps
enforced in the form and by the server), request a reversal, draft a refund, and the payer statement (outstanding,
unapplied cash, available credit). Every one of these requests is financial (`Idempotency-Key`), every change of an
existing document carries `If-Match`, and the approvals of reversals and refunds belong to the checker's inbox of Prompt
7.5.

## Decision

1. **Allocating lists the payer's bills that still owe something, oldest first**, each with an amount box, the bill's
   balance due beside it and the payment's unapplied cash left above them. An **Apply oldest first** button fills the
   amounts from the unapplied cash, oldest bill first, and the cashier may change any of them. The form refuses an
   amount above a bill's balance due, a total above the unapplied cash, and an empty allocation, before anything is
   sent; the server checks again under its lock (BR-303). One request sends the bills with an amount, in the order shown,
   with the payment's version and a key.
2. **A refund starts from its source**: a payment's page, or the payer statement's lists of payments with unapplied cash
   and credit notes with available credit (`/refunds/new?paymentId=` or `?creditNoteId=`). There is no free-standing new
   refund; the source is fixed, shown with what it still holds, and the amount is checked against it in the form and
   again by the server at submission and at approval (BR-307).
3. **The payer statement is a page of its own**, `/payers/:id/statement`, linked from the payer's page (US-40): what the
   payer owes, their unapplied cash and available credit and the net outstanding, from the balance report; their
   outstanding bills; the payments holding unapplied cash, each with Allocate and Refund; the credit notes holding
   available credit, each with Refund. Every figure is the server's.
4. **Who may**, from the API's policies (ADR 0039 point 3): `RecordPayments` records and allocates (TenantAdmin,
   Supervisor, BillingClerk, Cashier); `RequestReversals` asks to reverse a payment or one of its allocations (the same
   four); `DraftRefunds` drafts, corrects, voids and submits a refund (TenantAdmin, Supervisor, Cashier); payments are
   read by every role and refunds by those of `ReadRefunds`. Approving and rejecting a reversal or a refund is Prompt
   7.5's; a document waiting for a decision says so and offers nothing else.
5. **Every financial request has the key of its operation** (ADR 0040 point 2): recording, allocating, the two reversal
   requests, a refund's draft, void and submission. A reversal request asks for a reason first, in place, and answers 202:
   the payment or the allocation waits.
6. **Amounts compared in the browser are exact paise** (`src/money.ts`), as the totals of 7.3; a figure of the server is
   read into paise the same way.

## Alternatives

- **Amounts typed by hand only** (the alternative to point 1): no suggestion, more typing at the counter.
- **A "New refund" page with a source picker** (the alternative to point 2): one more search, and a refund whose source
  the cashier must find again.
- **The statement on the payer's page** (the alternative to point 3): one page fewer, a long one.

## Consequences

- The screens of 7.5 (the inbox, credit notes) link to these: a credit note's page offers its refund the same way.
- A payer with more than 200 outstanding bills shows the 200 oldest in the allocation form, which says so; an allocation
  names at most 50 bills (the API's limit), which the form also says.

## Built (2026-11-23)

Built as decided, with no package. What the build found:

- **One hook for every financial action on a document**, `useDocumentAction`: the key of the operation, the retry of the
  same request after no answer or "still running", the conflict that reads the document again and says what changed, the
  outcome. The payment's reversal requests and the refund's void and submission use it; the bill's actions of 7.3 keep
  their own hook, which does the same.
- **The API's message for one line of an allocation** (`allocations[1].amount`) is shown at the bill of that line and
  not again in the error component; the first build showed it twice, which a test caught.
- **A source that is not needed is not read**: a refund drawn from a payment does not ask for a credit note, nor the other
  way round (`enabled` on the two queries).
- **A test that began on `/payments` as a page without requests** (the token's expiry) begins on `/reports`, still a
  placeholder: the payments list now reads the API.
- **Tests:** 34 new frontend tests: recording (the payer chosen from their page, the reference rule, the body and its
  key); a payment's page by role (a clerk drafts no refund, a viewer does nothing); the reversal of a payment and of one
  allocation, with a reason, their version and a key, and `payment_refunded` shown; allocation (oldest first, "Apply
  oldest first", the caps refused before sending, the API's refusal at its bill, a conflict); the statement; refunds from
  a payment and from a credit note, their submission (202), void and correction, and a billing clerk refused them;
  amounts in exact paise. With no cap per bill, no cap on the total, the reference never required, the refund's cap a
  paisa loose and an allocation without `If-Match`, one at a time, each is caught.
- **In the browser**, against the API in Development on a throwaway LocalDB database: a Cashier recorded a UPI payment
  of 3,000 from the payer's page (the form asked for the reference first), was refused 2,511.08 against a bill owing
  2,511.07, applied the payment oldest first (1,049.48 and 1,950.52, nothing left), asked for one allocation's reversal
  (202; the other reversals withheld while it waits), read the statement (owed 560.55, unapplied 500.00 from a second
  payment, net 60.55), drew a refund from it (500.01 refused, 500.00 drafted) and submitted it (202); a Viewer saw the
  payment with no action, read the refunds, and had no access to recording.
