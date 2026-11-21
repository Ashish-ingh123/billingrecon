# Requirements

- **Status:** R1 written and approved 2026-09-29; R2–R5 are outlines to be expanded when each release starts.
- **Product:** BillingRecon.
- **Domain (confirmed 2026-09-29):** patient billing for hospitals and clinics in India. A **bill** is
  raised for a **patient** and owed by a **payer**. In R1 every payer is self-pay (the patient or a
  guarantor); insurers, TPAs and corporate accounts arrive in R4.
- **Vocabulary:** *Bill* (not invoice), *Payment* (not receipt), *Credit note*, *Adjustment*, *Refund*,
  *Allocation* (applying a payment or credit to a bill), *Posting* (making a document financially
  effective), *Maker* / *Checker*.

Every business rule has a stable id **BR-nnn** and is cited from `domain-model.md` (invariants),
`database.md` (constraints) and the tests. A rule is changed by editing it here and recording the
change in the ADR that governs it; ids are never reused.

---

## 1. Actors and roles

A user belongs to exactly one tenant (one hospital or clinic) and holds one or more roles.

| Role | Purpose |
|---|---|
| **TenantAdmin** | Manages users, roles, tenant settings and number series. |
| **Supervisor** | Approves maker-checker requests; can do everything a clerk and cashier can. |
| **BillingClerk** | Registers patients and payers; drafts and posts bills; drafts credit notes and adjustments. |
| **Cashier** | Records payments, allocates them, drafts refunds. |
| **Viewer** | Read-only access to documents and reports (auditors, management). |

Permission matrix (✓ = allowed, R = read only):

| Capability | TenantAdmin | Supervisor | BillingClerk | Cashier | Viewer |
|---|---|---|---|---|---|
| Users, roles, tenant settings, number-series prefixes | ✓ | | | | |
| Patients, payers, service items | ✓ | ✓ | ✓ | R | R |
| Bills: draft, edit, void, post | ✓ | ✓ | ✓ | R | R |
| Credit notes, adjustments: draft, submit (maker) | ✓ | ✓ | ✓ | | R |
| Payments: record, allocate | ✓ | ✓ | ✓ | ✓ | R |
| Refunds: draft, submit (maker) | ✓ | ✓ | | ✓ | R |
| Reversals: request (maker) | ✓ | ✓ | ✓ | ✓ | |
| Approve or reject (checker) | ✓ | ✓ | | | |
| Reports | ✓ | ✓ | ✓ | ✓ | ✓ |

The checker must always be a different user from the maker (BR-603), whatever the roles.

*How the matrix is applied (ADR 0032, 2026-11-21):* a ✓ includes reading, and a blank cell is no access, not even
reading, except that a Cashier reads credit notes (a refund may come from one). Applying a credit note's credit to
another bill is in the credit notes' row; the checker's inbox of pending requests is in the checker's row; the audit
trail is read by everyone (US-43), but the trail of a user by a TenantAdmin only.

---

## 2. R1 scope (MVP)

### In scope

| Module | R1 content |
|---|---|
| Identity | Tenants, local user accounts, roles, JWT issued by the API, tenant settings. |
| Payers | Patients (demographics, MRN), self-pay payers, activation and deactivation. |
| Billing | Service item catalogue, bills with lines, posting with gapless numbers, void of drafts, credit notes against a bill. |
| Payments | Payments by cash, card, UPI, bank transfer or cheque; allocation to bills; allocation reversal; payment reversal; refunds. |
| Receivables | Double-entry receivables subledger, cached balances (bill, payment, credit note, payer), reconciliation query. |
| Adjustments | Discount, write-off and rounding adjustments against a bill. |
| Reporting | Bill register, payment register and daily collection, outstanding by payer, unapplied cash and credit, pending approvals, ledger reconciliation. |
| Audit | Append-only audit trail for every financial and security-relevant change; correlation ids. |
| Common | Money and rounding, fiscal year, clock, gapless numbering, idempotency, optimistic concurrency, tenancy. |

### Out of scope for R1 (and where it lands)

- Bank and payment-gateway statement import, matching, suspense cash: **R2**.
- Ageing buckets, dunning, reminders, promises to pay, disputes, bulk write-off workflow: **R3**.
- Insurers, TPAs, corporate payers, claims, pre-authorisation, payer-specific pricing and credit limits: **R4**.
- Period close, backup/DR drills, observability, compliance reporting: **R5**.
- GST return filing and the CGST/SGST/IGST split (R1 records one tax rate and amount per line).
- Multi-currency (R1 is INR only, BR-001).
- HIS/EMR integration, pharmacy or inventory, printable PDF templates, e-mail/SMS notifications.
- Standalone credit notes (a credit note is always against a bill, BR-210), reversal of refunds, adjustments that increase a bill (BR-503), write-off of tiny unapplied cash.
- External identity providers and SSO; self-service password reset (a TenantAdmin resets passwords in R1).
- Multi-facility tenants (one tenant is one billing entity).

---

## 3. Business rules (BR catalogue)

### Common (BR-001 – BR-019)

| Id | Rule |
|---|---|
| **BR-001** | All money is INR, held as `decimal` with exactly two decimals. Rounding is `MidpointRounding.AwayFromZero` to 2 places, applied at line level (bill line, tax line, allocation). Totals are sums of rounded lines and are never rounded again. |
| **BR-002** | The tenant of every request is taken from the validated JWT and from nowhere else. Every tenant-scoped record carries the TenantId; a request can never read or write another tenant's data. |
| **BR-003** | Instants (created, posted, approved, reversed) are UTC and come from `IClock.UtcNow`. Business dates (bill date, payment date, due date) are calendar dates in IST (Asia/Kolkata, UTC+05:30, no DST). "Today" means IST today as given by `IClock`. |
| **BR-004** | The fiscal year runs 1 April to 31 March and is labelled `YYYY-YY` (1 April 2026 – 31 March 2027 is `2026-27`). A document's fiscal year is derived from its business date. |
| **BR-005** | Financial documents (bills, credit notes, payments, adjustments, refunds), ledger entries and audit records are never deleted. Drafts are voided; posted documents are corrected only by new documents. |
| **BR-006** | Once posted, a document's financial fields (lines, amounts, dates, payer, number) never change. Only status and status-related fields (reversed at, void reason) change afterwards. |
| **BR-007** | A financial use case is one database transaction: document, ledger entries, cached balances, number allocation, approval decision and audit record commit together or not at all. |

### Patients and payers (BR-101 – BR-119)

| Id | Rule |
|---|---|
| **BR-101** | A patient has a medical record number (MRN) that is required and unique within the tenant. The MRN is entered (the hospital's HIS remains the system of record); it is not generated by BillingRecon. |
| **BR-102** | A patient is deactivated, never deleted. An inactive patient cannot be billed; existing bills are unaffected. |
| **BR-103** | Payer kind is `SelfPay` in R1. `Insurer` and `Corporate` are reserved values for R4 and are rejected in R1. |
| **BR-104** | A patient has at most one self-pay payer linked to them. A self-pay payer may also stand alone as a guarantor (no patient link). A payer can be linked only to an active patient (added 2026-10-17 with the owner's approval of the Prompt 2.2 plan); the link never changes. |
| **BR-105** | A payer is deactivated, never deleted. An inactive payer cannot receive new bills, payments or refunds; existing balances remain and can still be allocated and reported. |
| **BR-106** | Every bill is for exactly one patient and owed by exactly one payer. Payments, credit notes, adjustments and refunds belong to exactly one payer. |

### Billing (BR-201 – BR-229)

| Id | Rule |
|---|---|
| **BR-201** | A bill line has quantity > 0 (3 decimals), unit price ≥ 0 (4 decimals), discount ≥ 0 and ≤ the line amount, tax rate between 0 and 100 (2 decimals), and a non-empty description. A line may reference a service item, which supplies the default price and tax rate. |
| **BR-202** | Line arithmetic, each step rounded per BR-001: `LineAmount = round(Quantity × UnitPrice)`; `TaxableAmount = LineAmount − DiscountAmount`; `TaxAmount = round(TaxableAmount × TaxRate / 100)`; `LineTotal = TaxableAmount + TaxAmount`. |
| **BR-203** | Bill totals are sums of lines: `SubTotal = Σ LineAmount`, `DiscountTotal = Σ DiscountAmount`, `TaxTotal = Σ TaxAmount`, `Total = SubTotal − DiscountTotal + TaxTotal`. A bill needs at least one line to post. `Total ≥ 0`. |
| **BR-204** | Only draft bills can be edited or voided. A voided bill keeps its content and is visible in the audit trail; it has no number and no ledger effect. |
| **BR-205** | Business dates on bills, credit notes, payments, adjustments and refunds are not in the future and are not earlier than IST today minus `Tenant.MaxBackdateDays` (default 7). `DueDate ≥ BillDate`; default `DueDate = BillDate + Tenant.DefaultDueDays` (default 0). |
| **BR-206** | Posting a bill assigns the next gapless number for (tenant, Bill, fiscal year of BillDate), posts `Dr Receivable(bill) / Cr Revenue` for `Total`, sets `BalanceDue = Total` and freezes the bill (BR-006). A bill with `Total = 0` posts directly as Settled. |
| **BR-207** | A bill whose `Total ≥ Tenant.PostingApprovalThreshold` (null = never) must be approved by a checker before posting. It waits in `PendingApproval`, is locked, and receives its number only when the checker approves (BR-606). |
| **BR-208** | A posted bill's status follows its balance: `Posted` when `BalanceDue = Total`, `PartiallyPaid` when `0 < BalanceDue < Total`, `Settled` when `BalanceDue = 0`. Reversals move it back. |
| **BR-209** | A posted bill is corrected only by a credit note (BR-210) or an adjustment (BR-501); never by editing. |
| **BR-210** | A credit note is against exactly one posted bill and belongs to that bill's payer. The sum of posted credit note totals for a bill never exceeds `Bill.Total`. |
| **BR-211** | Credit note lines follow BR-201 and BR-202. A line may reference a bill line; the credit for a referenced bill line never exceeds that line's `LineTotal` across all posted credit notes. |
| **BR-212** | Credit notes require maker-checker (BR-604). Posting assigns the next gapless credit-note number, posts `Dr Revenue / Cr CreditAvailable(creditNote)` for `Total`, then automatically applies `min(Total, Bill.BalanceDue)` to the bill (`Dr CreditAvailable / Cr Receivable(bill)`). |
| **BR-213** | A credit note's `AvailableAmount` (unapplied credit) may be applied to other posted bills of the same payer or refunded (BR-307). It is never negative and never exceeds `Total`. |
| **BR-214** | `CreditNoteDate ≥ BillDate` of the referenced bill, subject to BR-205. |

### Payments, allocations and refunds (BR-301 – BR-329)

| Id | Rule |
|---|---|
| **BR-301** | A payment has `Amount > 0`, a method in {Cash, Card, UPI, BankTransfer, Cheque}, a payment date per BR-205, and an external reference (UPI id, card auth, UTR, cheque number) that is required for every method except Cash. |
| **BR-302** | A payment is posted the moment it is recorded (there is no draft payment): it takes the next gapless payment number, posts `Dr Cash / Cr UnappliedCash(payment)` and sets `UnappliedAmount = Amount`. |
| **BR-303** | An allocation applies part of a payment to a posted bill of the same payer: `Amount > 0`, `Amount ≤ Payment.UnappliedAmount`, `Amount ≤ Bill.BalanceDue`. It posts the paired entry `Dr UnappliedCash(payment) / Cr Receivable(bill)` and decreases both `Payment.UnappliedAmount` and `Bill.BalanceDue` in the same transaction. This is the one use case that changes two aggregates at once (see `domain-model.md` §6). |
| **BR-304** | A payment may be allocated to many bills and a bill may receive many allocations. Partial allocation is normal; unapplied cash may remain indefinitely and is reported (BR-901). |
| **BR-305** | Reversing an allocation is a maker-checker action (BR-604). It posts the mirror pair `Dr Receivable(bill) / Cr UnappliedCash(payment)` and restores both cached balances. The original allocation row is marked Reversed; nothing is deleted. |
| **BR-306** | Reversing a payment (for example a dishonoured cheque) is a maker-checker action. Approval first reverses every active allocation of the payment (BR-305), then posts `Dr UnappliedCash(payment) / Cr Cash` for `Amount`. A payment from which a refund has been posted cannot be reversed. |
| **BR-307** | A refund returns money from one source: a payment's `UnappliedAmount` or a credit note's `AvailableAmount`. `Amount > 0` and `Amount ≤` the source's available amount at posting. Refunds require maker-checker, a method and reference (BR-301 rules), and post `Dr UnappliedCash(payment) or CreditAvailable(creditNote) / Cr Cash`. The refund date is not before the date of its payment or credit note; BR-205 applies as well (added 2026-11-21, ADR 0029). |
| **BR-308** | Refunds are not reversible in R1. A refund made in error is corrected by recording a new payment. |
| **BR-309** | Payments and refunds can be recorded only for an active payer (BR-105). |

### Receivables ledger (BR-401 – BR-419)

| Id | Rule |
|---|---|
| **BR-401** | Every financial posting writes exactly one ledger posting with two or more entries whose debits equal credits, in the same transaction as the document (BR-007). A posted document without ledger entries, or ledger entries without a document, is a defect. *Amended 2026-11-20 (ADR 0022):* a posted document whose total is zero has no ledger posting, because an entry has an amount above zero; a bill that comes to nothing is posted as Settled without one (BR-206). |
| **BR-402** | Ledger postings and entries are append-only. Corrections are reversing postings that reference the original posting. |
| **BR-403** | The subledger has six accounts: `Receivable` (subject: bill), `UnappliedCash` (subject: payment), `CreditAvailable` (subject: credit note), and the control accounts `Revenue`, `Cash` and `Adjustments`. Subject and payer are required on the first three and absent on the control accounts. |
| **BR-404** | The balance identities in `database.md` §5.5 hold after every transaction: bill balance due, payment unapplied amount, credit note available amount and payer balances each equal the corresponding ledger sum. A reconciliation query detects drift; drift is a Sev-1 defect, never silently corrected. |
| **BR-405** | No subject balance is ever negative: `Receivable(bill) ≥ 0`, `UnappliedCash(payment) ≥ 0`, `CreditAvailable(creditNote) ≥ 0`. |
| **BR-406** | Each ledger posting carries the document's business date (IST) and the posting instant (UTC). Period close is out of scope for R1; postings into any past date allowed by BR-205 are accepted. |

### Adjustments (BR-501 – BR-519)

| Id | Rule |
|---|---|
| **BR-501** | An adjustment is against exactly one posted bill, of kind `Discount`, `WriteOff` or `Rounding`, with `Amount > 0` and `Amount ≤ Bill.BalanceDue` at posting. A `Rounding` adjustment is less than ₹1.00. The adjustment date is not before the bill date, subject to BR-205 (added 2026-11-21 with Prompt 4.4, ADR 0026). |
| **BR-502** | Adjustments require maker-checker (BR-604) and a reason. Posting assigns the next gapless adjustment number and posts `Dr Adjustments / Cr Receivable(bill)`. |
| **BR-503** | Adjustments only reduce a bill's balance. To charge more, raise a new bill. |

### Identity and maker-checker (BR-601 – BR-619)

| Id | Rule |
|---|---|
| **BR-601** | A user belongs to one tenant. E-mail is unique within the tenant. Roles are those in §1; a user may hold several. Disabled users cannot sign in. |
| **BR-602** | The JWT carries the tenant id, user id and roles. It is validated on every request; the tenant context is resolved once per request from it (BR-002). |
| **BR-603** | The checker of a maker-checker request must be a different user from the maker and must hold `Supervisor` or `TenantAdmin`. This is enforced server-side and cannot be bypassed by role. |
| **BR-604** | Actions that require maker-checker: posting a credit note, posting an adjustment, posting a refund, reversing an allocation, reversing a payment, posting a bill at or above `Tenant.PostingApprovalThreshold` (BR-207), and changing a number-series prefix (BR-802). |
| **BR-605** | A document has at most one pending approval request. Pending requests are listed for checkers. A rejection records the checker's comment and returns a draft document to `Draft`; a rejected reversal leaves the document as it was. |
| **BR-606** | The checker's approval is the posting: number allocation, ledger entries, cache updates, status change and audit happen in the approval transaction. The maker's submission has no financial effect. |
| **BR-607** | Tenant settings (`PostingApprovalThreshold`, `MaxBackdateDays`, `DefaultDueDays`, default number prefixes) are changed only by a TenantAdmin and every change is audited. |

### Audit (BR-701 – BR-719)

| Id | Rule |
|---|---|
| **BR-701** | Every state change to a financial or security-relevant entity (documents, allocations, approvals, payers, patients, users, roles, tenant settings, number series) writes an audit record in the same transaction: who, when (UTC), tenant, entity type and id, action, before and after JSON, correlation id. |
| **BR-702** | Audit records are insert-only. The application's database login has no UPDATE or DELETE permission on the audit table. |
| **BR-703** | Every request has a correlation id (accepted from `X-Correlation-Id` or generated), returned in the response and present in every log line and audit record for that request. |

### Numbering, idempotency and concurrency (BR-801 – BR-819)

| Id | Rule |
|---|---|
| **BR-801** | Bills, credit notes, payments, adjustments and refunds are numbered gaplessly and sequentially per (tenant, document type, fiscal year) in the format `PREFIX/YY-YY/NNNNNN` (for example `BIL/26-27/000123`), at most 16 characters as CGST rule 46(b) requires for tax invoices (ADR 0008). The number is taken inside the posting transaction and rolls back with it; a failed posting cannot leave a gap. |
| **BR-802** | A number series row is created on first use for each (tenant, document type, fiscal year) with the tenant's default prefix. Changing a prefix is a maker-checker action and affects only future numbers. `NextNumber` never decreases. |
| **BR-803** | Every POST that creates or posts a financial document requires an `Idempotency-Key` header holding a UUID, scoped to the tenant. A replay with the same key and payload returns the original response; the same key with a different payload returns 422 `idempotency_key_reuse`; a key whose first request is still running returns 409 `idempotency_in_progress`; a missing or malformed key returns 400 `idempotency_key_missing`. Keys are retained for 30 days. |
| **BR-804** | Every mutable aggregate carries a `rowversion`. Reads return it as an ETag; writes send it as `If-Match`. A stale version returns 409 `concurrency_conflict`; the client re-reads and retries. |

### Reporting (BR-901 – BR-919)

| Id | Rule |
|---|---|
| **BR-901** | R1 reports: bill register, payment register with daily collection by method, outstanding by payer, unapplied cash and available credit, pending approvals, and ledger reconciliation (cache versus ledger). Reports read committed data and every figure ties back to ledger entries. |

---

## 4. R1 user stories and acceptance criteria

Acceptance criteria cite the rules they verify. "Given/when/then" is abbreviated to the observable outcome.

### Identity and tenancy

| Id | Story | Acceptance |
|---|---|---|
| US-01 | As a TenantAdmin I create users and assign roles so that staff can work within our tenant. | E-mail unique per tenant (BR-601); change audited (BR-701). |
| US-02 | As a user I sign in and receive a JWT so that the API knows my tenant and roles. | Token carries tenant, user, roles (BR-602); disabled user is refused (BR-601). |
| US-03 | As a TenantAdmin I set the posting approval threshold, back-date window and default due days. | Settings applied on next request; change audited (BR-607). |
| US-04 | As the platform, requests never cross tenants. | A token for tenant A cannot read or write tenant B's records, including by id (BR-002); tested as a defect. |

### Patients and payers

| Id | Story | Acceptance |
|---|---|---|
| US-10 | As a BillingClerk I register a patient with MRN and demographics. | Duplicate MRN rejected (BR-101). |
| US-11 | As a BillingClerk I create a self-pay payer for a patient or a guarantor. | Second self-pay payer for the same patient rejected (BR-104); `Insurer` kind rejected in R1 (BR-103). |
| US-12 | As a BillingClerk I deactivate a patient or payer. | No new bills or payments for them (BR-102, BR-105); balances still reported. |

### Billing

| Id | Story | Acceptance |
|---|---|---|
| US-20 | As a BillingClerk I draft a bill for a patient with lines from the service catalogue and free-text lines. | Line and total arithmetic per BR-201–203; draft has no number; draft editable (BR-204). |
| US-21 | As a BillingClerk I post a bill. | Gapless number assigned (BR-801), ledger `Dr Receivable / Cr Revenue` (BR-206), `BalanceDue = Total`, bill frozen (BR-006), audit written (BR-701); all in one transaction (BR-007). |
| US-22 | As a BillingClerk I post a bill at or above the threshold and a Supervisor approves it. | Bill waits in `PendingApproval` without a number (BR-207); approval by the same user rejected (BR-603); approval posts it (BR-606). |
| US-23 | As a BillingClerk I void a draft bill. | Posted bill cannot be voided (BR-204); void audited. |
| US-24 | As a BillingClerk I draft a credit note against a posted bill; a Supervisor approves it. | Total capped by BR-210/211; posting applies credit to the bill first and leaves the rest as available credit (BR-212, BR-213). |
| US-25 | As the system, posting fails atomically. | If any step fails, no number is consumed (BR-801), no ledger or audit rows exist (BR-401, BR-007). |

### Payments

| Id | Story | Acceptance |
|---|---|---|
| US-30 | As a Cashier I record a payment from a payer. | Number and ledger per BR-302; reference required for non-cash (BR-301). |
| US-31 | As a Cashier I allocate a payment to one or more bills. | Caps per BR-303; both caches and the ledger pair updated in one transaction; bill status follows BR-208. |
| US-32 | As a Cashier I request reversal of an allocation; a Supervisor approves it. | Mirror ledger pair (BR-305); caches restored; original row marked Reversed. |
| US-33 | As a Cashier I request reversal of a dishonoured cheque payment; a Supervisor approves it. | Allocations reversed first, then the payment (BR-306); refused if a refund exists. |
| US-34 | As a Cashier I draft a refund of unapplied cash or available credit; a Supervisor approves it. | Cap per BR-307; ledger and cache updated; number assigned. |
| US-35 | As a client I retry a financial POST with the same `Idempotency-Key`. | Original response returned unchanged; different payload → 422; missing → 400 (BR-803). |
| US-36 | As a client I send a stale `If-Match`. | 409 `concurrency_conflict`, no change made (BR-804). |

### Receivables and reporting

| Id | Story | Acceptance |
|---|---|---|
| US-40 | As a Supervisor I see a payer's outstanding, unapplied cash and available credit. | Figures equal the ledger sums (BR-404). |
| US-41 | As an auditor I run the reconciliation report. | Zero drift rows after every integration test scenario (BR-404); a seeded drift is reported. |
| US-42 | As a Cashier I see today's collection by method. | Ties to payment ledger entries for the IST business date (BR-003, BR-901). |
| US-43 | As a Viewer I read the audit trail of a bill. | Every state change present with before/after and correlation id (BR-701, BR-703). |

---

## 5. Non-functional requirements (R1)

| Area | Requirement |
|---|---|
| Correctness | The twelve rules in `CLAUDE.md` §4 and the BR catalogue above are enforced by tests; a failing invariant blocks release. |
| Performance | Posting a bill or recording a payment: p95 < 500 ms at 50 concurrent users, 10 000 bills per tenant per month, on a single SQL Server instance. Number allocation serialises per (tenant, type, FY) and must stay under 20 ms inside the transaction. |
| Availability | Single-region deployment; planned downtime windows acceptable in R1. Backup and restore drills are R5. |
| Security | TLS only; JWT lifetime 60 minutes; passwords hashed with a slow, salted algorithm from the BCL; secrets from environment or user secrets; no tenant data in logs beyond ids. |
| Retention | Financial documents, ledger and audit are retained for at least 8 years (Companies Act 2013 books of account). Idempotency keys 30 days. |
| Observability | Structured logs with TenantId, UserId, CorrelationId on every event; health endpoint. Metrics and tracing are R5. |
| Compatibility | API is versioned under `/api/v1`; breaking changes need a new version. |

---

## 6. Later releases (outline)

- **R2 Reconciliation:** bank and gateway statement import, auto and manual matching to payments, unapplied and suspense cash, reconciliation reports.
- **R3 Recovery:** ageing buckets (0–30, 31–60, 61–90, 90+ days from due date), dunning schedules and reminders, promises to pay, disputes, bulk write-off workflow under maker-checker.
- **R4 Insurance / Corporate:** payer kinds `Insurer` and `Corporate`, claims and pre-authorisation, split billing between patient and third-party payer, payer statements, payer-specific pricing and credit limits (credit-limit changes under maker-checker).
- **R5 Maturity:** period close, hardening, performance, observability, backup/DR drills, GST and compliance reporting, extension points.
