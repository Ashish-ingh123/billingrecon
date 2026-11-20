# API

The REST conventions of the BillingRecon API and its endpoints. Written with the first resources, patients and
payers (Prompt 2.2, ADR 0018), and extended with the service catalogue (Prompt 2.3); authentication, the `Idempotency-Key` contract and the financial endpoints are added
here with the slices that build them.

---

## 1. Basics

- **Base path** `/api/v1` (ADR 0013). A breaking change gets `/api/v2`; within `v1`, changes are additive only.
- **JSON** with camel-case property names. **Enums travel by name** (`"SelfPay"`, `"Inactive"`), in requests and
  responses; a number is refused. **Business dates** are `yyyy-MM-dd`, calendar dates in India (BR-003);
  **instants** are ISO 8601 in UTC (`2026-10-17T06:30:00+00:00`), and their names end in `AtUtc`.
- **A request body may hold only the fields the endpoint knows.** Any other field is refused with 400: a misspelt name
  cannot pass unnoticed, and a field that cannot be changed cannot be sent.
- **Operational endpoints** sit outside the prefix: `/health/live`, `/health/ready`, and in Development only the
  OpenAPI document (`/openapi/v1.json`) and the Scalar UI (`/scalar`).
- Every response carries `X-Correlation-Id` (taken from the request or generated, BR-703).

## 2. Authentication and tenancy

Phase 5 adds sign-in and JWT bearer tokens; the token carries the tenant, the user and the roles (BR-602), and the
tenant comes from the token only, never from a body, a query string, a route or a header (BR-002). Until then the
Development environment acts for one fixed tenant and user from `appsettings.Development.json`, and every other
environment acts for nobody (ADR 0015, section 6). Role checks arrive with authentication.

**Another tenant's record answers 404**, with exactly the code and the message an id nobody has gets
(`patient_not_found`, `payer_not_found`). The same holds for an id a request names in its body, such as the
`patientId` of a new payer. A 403 would say "this exists, and it is not yours": confirmation, for any id a client
cares to try, that another hospital has that patient. Every query is filtered by the tenant (ADR 0009), so for the
caller the record does not exist, and 404 is the truthful answer. 403 is kept for a known user who lacks a
permission inside their own tenant.

## 3. Errors

Every error is an RFC 9457 ProblemDetails (`application/problem+json`) with three extensions: `errorCode`, a stable
snake-case code clients branch on; `traceId`; and `correlationId`, equal to the response header (ADR 0011). A 400
with field errors lists the messages per field under `errors`, keyed by the JSON name of the field (`fullName`,
`pageSize`) or, for a header, by its name (`If-Match`).

| Status | When | `errorCode` |
|---|---|---|
| 400 | A field is missing or invalid, the body cannot be read, a query value is out of range | `validation_failed` |
| 400 | A financial POST without a usable `Idempotency-Key` (section 7) | `idempotency_key_missing` |
| 404 | The record does not exist for the tenant (another tenant's included), also when a request only refers to it | `patient_not_found`, `payer_not_found`, `service_item_not_found`, `bill_not_found`, `payment_not_found`; `not_found` for an unknown route |
| 409 | The record changed since it was read (BR-804) | `concurrency_conflict` |
| 409 | The first request with this `Idempotency-Key` is still running (section 7) | `idempotency_in_progress` |
| 409 | A uniqueness rule (BR-101, BR-104, the code of a service item) | `patient_mrn_duplicate`, `patient_already_linked`, `service_item_code_duplicate`; `duplicate_key` where no rule applies |
| 422 | A business rule refuses the request | see the endpoints |
| 422 | The `Idempotency-Key` was used for a different request (section 7) | `idempotency_key_reuse` |
| 428 | A change of an existing record without `If-Match` | `precondition_required` |
| 500 | A fault; no internal detail is returned outside Development | `internal_error` |

## 4. Versions: ETag and If-Match (BR-804, ADR 0007)

- A response that returns one record (a `GET`, the `201` of a create, the `200` of a change) carries the record's
  version in the `ETag` header, a strong ETag: the row version in base64, in quotes (`"AAAAAAAAB9E="`). The body
  carries the same value, unquoted, as `version`.
- A request that changes an existing record (`PATCH`, and the `POST` actions on `/{id}/...`) sends that ETag back,
  exactly as received, in `If-Match`:
  - without `If-Match`: **428** `precondition_required`, nothing changed;
  - with anything but one strong ETag of this API (a weak tag, `*`, a list, something else): **400**
    `validation_failed`, field `If-Match`;
  - with the ETag of an older version: **409** `concurrency_conflict`, nothing changed. The client reads the record
    again, shows what changed, and decides; the API never merges.
- A create needs no `If-Match`.

## 5. Changing a record

- **Create**: `POST` on the collection; **201** with the record, its address in `Location` and its ETag.
- **Correct**: `PATCH` on the record, with `If-Match`. The body holds **every editable field**, and an optional field
  left out is **cleared**: the body is the new state of the editable fields, not a list of changes. Fields that are
  not editable (an MRN, a status, a payer's kind or patient link) are not part of the body and are refused if sent.
  A body that changes nothing answers 200 with the same version.
- **Change of state**: a `POST` action on the record (`/deactivate`, `/reactivate`), with `If-Match` and no body. A
  status is never written through a `PATCH`.
- **Nothing is deleted** (BR-005): there is no `DELETE`. Patients and payers are deactivated.

## 6. Lists

- `page` (from 1, at most 1,000,000; default 1) and `pageSize` (1 to 100; default 20).
- The answer is an envelope: `{ "items": [...], "page": 2, "pageSize": 20, "totalCount": 57 }`, the items in a stable
  order (by name, then by id), so that pages neither repeat nor skip a record while nothing changes.
- Filters are explicit query parameters; a value out of range answers 400.
- `search` compares without regard to case. A `%` or `_` in it matches itself.

## 7. Idempotency (BR-803, ADR 0006)

Every `POST` that creates or posts a **financial document** requires an `Idempotency-Key` header. So far these are
`POST /api/v1/bills` and `POST /api/v1/bills/{id}/void`; every later financial POST joins them, and its row in
section 8 says so. With the header, such a request has its effect at most once, however often it is sent.

**The key** is one UUID in the usual hyphenated form (`3f2504e0-4f89-11d3-9a0c-0305e82c3301`), which the client
generates **for the operation** and sends again unchanged when it retries that operation. It is scoped to the
tenant: it names the operation, not the user who sends it. The empty UUID is not a key.

| What the client sends | Answer |
|---|---|
| No header, two headers, a list, or text that is not one such UUID | 400 `idempotency_key_missing`; nothing runs |
| A key for the first time | The request runs. Its answer is stored with the key |
| The same key with the same request, after the first was answered | The stored answer again: the same status, body, `Location` and `ETag`, plus `Idempotent-Replayed: true`. Nothing runs |
| The same key with the same request, while the first is still running | 409 `idempotency_in_progress`. Wait, then send the same request again |
| The same key with a different request | 422 `idempotency_key_reuse`. The stored answer is never overwritten |

**The same request** means the same method, the same path and query string, the same `If-Match` header and the same
bytes of the body. A client retries by sending again exactly what it sent; a body serialised afresh with other
white space is a different request.

**What is stored.** A success (2xx) is stored in the same transaction as the work it reports: either both are
there or neither. A refusal (4xx) is stored too, after everything the request did has been undone, so a retry gets
the same refusal, with the `traceId` and `correlationId` of the first. A fault (5xx) stores nothing and frees the
key: retry with the same key.

**After a refusal, a new attempt needs a new key.** The key belongs to the request that was refused. A client that
is answered 409 `concurrency_conflict`, reads the record again and sends the new version in `If-Match` is making a
new attempt: under the old key it is answered 422 `idempotency_key_reuse`, under a new key it runs.

**How long.** A key is remembered for 30 days. A retry after that would run as a first request.

**Not financial documents:** patients, payers and service items. They take no key and ignore the header. A retried
`POST /patients` meets its own MRN and answers 409; a retried `POST /payers` for a patient answers 409
`patient_already_linked`; a retried `POST /service-items` answers 409 `service_item_code_duplicate`; a retried
`POST /payers` for a guarantor can create a second guarantor, which is then deactivated. `PUT` and `PATCH` take no
key either: `If-Match` already refuses a repeat as stale.

---

## 8. Endpoints

### 8.1 Patients (US-10, US-12; BR-101, BR-102)

| Method and path | Success | Failures |
|---|---|---|
| `POST /api/v1/patients` | 201, the patient | 400; 409 `patient_mrn_duplicate` |
| `GET /api/v1/patients/{id}` | 200, the patient | 404 `patient_not_found` |
| `GET /api/v1/patients?search=&status=&page=&pageSize=` | 200, a page of patients by name | 400 |
| `PATCH /api/v1/patients/{id}` | 200, the patient | 400; 404; 409 `concurrency_conflict`; 428 |
| `POST /api/v1/patients/{id}/deactivate` | 200, the patient | 404; 409; 422 `patient_already_inactive`; 428 |
| `POST /api/v1/patients/{id}/reactivate` | 200, the patient | 404; 409; 422 `patient_already_active`; 428 |

**A patient**: `id`, `mrn`, `fullName`, `dateOfBirth`, `sex`, `phone`, `email`, `addressLine1`, `addressLine2`,
`city`, `state`, `pinCode`, `status` (`Active`, `Inactive`), `createdAtUtc`, `updatedAtUtc`, `version`.

**The fields a client sends** (register: all of them; correct: all but `mrn`):

| Field | Rule |
|---|---|
| `mrn` | Required; 1 to 30 characters after trimming, no control characters. Unique in the tenant, compared without regard to case (`MRN-A1` and `mrn-a1` are one MRN). Never changes. |
| `fullName` | Required; 1 to 200 characters after trimming. |
| `dateOfBirth` | Optional; not in the future (India's calendar), not before 1900-01-01. |
| `sex` | Optional; `Male`, `Female`, `Other` or `Unknown`. |
| `phone` | Optional; 7 to 15 digits and at most 20 characters, written with digits, spaces, hyphens, parentheses and a leading `+`. |
| `email` | Optional; an address with one `@` and a domain with a dot, at most 256 characters. |
| `addressLine1`, `addressLine2` | Optional; at most 200 characters each. |
| `city`, `state` | Optional; at most 100 characters each. |
| `pinCode` | Optional; at most 10 characters, letters, digits, spaces and hyphens: an Indian PIN code or a foreign postal code. |

Text is stored without its surrounding white space, and a blank optional value as null.

**Search** (`search`): finds a patient by the whole MRN or by part of the name. **Status** (`status`): `Active` or
`Inactive`; both when left out. An inactive patient is still listed, read and corrected; no new bill may be raised for
them (BR-102).

### 8.2 Payers (US-11, US-12; BR-103 to BR-105)

| Method and path | Success | Failures |
|---|---|---|
| `POST /api/v1/payers` | 201, the payer | 400; 404 `patient_not_found`; 409 `patient_already_linked`; 422 `payer_kind_not_supported`, `patient_inactive` |
| `GET /api/v1/payers/{id}` | 200, the payer | 404 `payer_not_found` |
| `GET /api/v1/payers?search=&status=&patientId=&page=&pageSize=` | 200, a page of payers by name | 400 |
| `PATCH /api/v1/payers/{id}` | 200, the payer | 400; 404; 409 `concurrency_conflict`; 428 |
| `POST /api/v1/payers/{id}/deactivate` | 200, the payer | 404; 409; 422 `payer_already_inactive`; 428 |
| `POST /api/v1/payers/{id}/reactivate` | 200, the payer | 404; 409; 422 `payer_already_active`; 428 |

**A payer**: `id`, `kind`, `name`, `patientId` (null for a guarantor), `phone`, `email`, `status` (`Active`,
`Inactive`), `createdAtUtc`, `updatedAtUtc`, `version`.

**The fields a client sends** (create: all of them; correct: `name`, `phone`, `email`):

| Field | Rule |
|---|---|
| `kind` | Required. `SelfPay`; `Insurer` and `Corporate` are known names, refused with 422 `payer_kind_not_supported` until R4 (BR-103). |
| `patientId` | Optional: the patient the payer is. Left out for a guarantor, who pays for someone (BR-104). The patient must exist in the tenant (404 otherwise, another tenant's patient included), be active (422 `patient_inactive`) and have no payer yet (409 `patient_already_linked`). Never changes. |
| `name` | Required; 1 to 200 characters after trimming. |
| `phone`, `email` | Optional; the rules of a patient's. |

A patient has at most one payer linked to them; any number of guarantors may exist. **Search** finds a payer by part of
its name; `patientId` finds the payer linked to a patient. A deactivated payer receives no new bills, payments or
refunds; its balance remains and is still allocated and reported (BR-105).

### 8.3 Service items (the service catalogue; BR-201)

| Method and path | Success | Failures |
|---|---|---|
| `POST /api/v1/service-items` | 201, the service item | 400; 409 `service_item_code_duplicate` |
| `GET /api/v1/service-items/{id}` | 200, the service item | 404 `service_item_not_found` |
| `GET /api/v1/service-items?search=&status=&page=&pageSize=` | 200, a page of service items by code | 400 |
| `PATCH /api/v1/service-items/{id}` | 200, the service item | 400; 404; 409 `concurrency_conflict`; 428 |
| `POST /api/v1/service-items/{id}/deactivate` | 200, the service item | 404; 409; 422 `service_item_already_inactive`; 428 |
| `POST /api/v1/service-items/{id}/reactivate` | 200, the service item | 404; 409; 422 `service_item_already_active`; 428 |

There is no `DELETE`: it answers 405.

**A service item**: `id`, `code`, `name`, `category`, `defaultUnitPrice`, `taxRatePercent`, `status` (`Active`,
`Inactive`), `createdAtUtc`, `updatedAtUtc`, `version`. The two numbers are JSON numbers, never strings.

**The fields a client sends** (create: all of them; correct: all but `code`):

| Field | Rule |
|---|---|
| `code` | Required; 1 to 30 characters after trimming, each an ASCII letter, a digit or one of `.` `-` `_` `/`. Unique in the tenant, compared without regard to case (`LAB/CBC` and `lab/cbc` are one code), and still taken while its item is inactive. Never changes. |
| `name` | Required; 1 to 200 characters after trimming. |
| `category` | Optional; at most 50 characters. Free text, for grouping in lists and reports. |
| `defaultUnitPrice` | Required; 0 or more, at most 4 decimals and 15 digits before them. Send `0` for a service that is free. |
| `taxRatePercent` | Required; 0 to 100, at most 2 decimals. Send `0` for a service that is not taxed. |

Neither number has a default: a request without one is refused, so that a forgotten field never becomes a free or
untaxed service. **Search** (`search`) finds an item by part of its code or part of its name, without regard to
case; the text is taken literally, `%` and `_` included. **Status** (`status`): `Active` or `Inactive`; both when
left out. Correcting the price or the tax rate changes what new bill lines start from; a line already written
keeps its own copy. An inactive item is still listed, read and corrected.

### 8.4 Bills (US-20, US-21, US-23; BR-201 to BR-206)

A bill is a draft until it is posted. A draft has no number and no ledger effect.

| Method and path | Success | Failures |
|---|---|---|
| `POST /api/v1/bills` (needs `Idempotency-Key`, section 7) | 201, the bill | 400, also `idempotency_key_missing`; 404 `patient_not_found`, `payer_not_found`, `service_item_not_found`; 422 (see below) |
| `GET /api/v1/bills/{id}` | 200, the bill with its lines | 404 `bill_not_found` |
| `GET /api/v1/bills?patientId=&payerId=&status=&billDateFrom=&billDateTo=&page=&pageSize=` | 200, a page of bill summaries, newest bill date first | 400 |
| `PATCH /api/v1/bills/{id}` | 200, the bill | 400; 404; 409 `concurrency_conflict`, `bill_already_posted`; 422 `bill_not_draft` and the date rules; 428 |
| `PUT /api/v1/bills/{id}/lines` | 200, the bill | 400; 404 `bill_not_found`, `service_item_not_found`; 409 `concurrency_conflict`, `bill_already_posted`; 422 `bill_not_draft`, `service_item_inactive` and the line rules; 428 |
| `POST /api/v1/bills/{id}/void` (needs `Idempotency-Key`, section 7) | 200, the bill | 400, also `idempotency_key_missing`; 404; 409 `concurrency_conflict`, `bill_already_posted`; 422 `bill_not_draft`; 428 |
| `POST /api/v1/bills/{id}/post` (needs `Idempotency-Key`, section 7; no body) | 200, the bill with its number; **202**, the bill waiting for approval | 400 `idempotency_key_missing`; 403 `approval_needs_user`; 404 `bill_not_found`; 409 `concurrency_conflict`, `bill_already_posted`, `bill_pending_approval`, `idempotency_in_progress`; 422 `bill_not_draft`, `bill_has_no_lines`, `bill_date_too_old`, `bill_date_in_future`, `payer_inactive`; 428 |
| `POST /api/v1/bills/{id}/approve` (needs `Idempotency-Key`; body optional) | 200, the bill, posted | 400; 403 `approval_checker_is_maker`, `approval_needs_user`; 404; 409 `concurrency_conflict`, `bill_already_posted`; 422 `bill_not_pending_approval`, `bill_not_draft`, `bill_date_too_old`, `payer_inactive`; 428 |
| `POST /api/v1/bills/{id}/reject` (needs `Idempotency-Key`) | 200, the bill, a draft again | 400 (the comment is required); 403 `approval_checker_is_maker`, `approval_needs_user`; 404; 409 `concurrency_conflict`, `bill_already_posted`; 422 `bill_not_pending_approval`, `bill_not_draft`; 428 |

There is no `DELETE`: it answers 405. A draft that is not wanted is voided.

**A bill**: `id`, `patientId`, `payerId`, `billDate`, `dueDate`, `encounterReference`, `notes`, `status` (`Draft`,
`Voided`, `Posted`, `PartiallyPaid`, `Settled`, `PendingApproval`), `number`, `fiscalYear` and `postedAtUtc` (all three null until the
bill is posted), `subTotal`, `discountTotal`, `taxTotal`, `total`, `balanceDue`, `voidedAtUtc`, `voidReason`, `lines`,
`createdAtUtc`, `updatedAtUtc`, `version`. **A line**: `lineNo`, `serviceItemId`, `description`, `quantity`,
`unitPrice`, `lineAmount`, `discountAmount`, `taxRatePercent`, `taxAmount`, `lineTotal`. **A bill in a list** has
no lines and no void details. Every amount is a JSON number, never a string. The `version` and the ETag change when
the bill changes and when any of its lines does.

**Creating a draft** (`POST`):

| Field | Rule |
|---|---|
| `patientId` | Required. The patient must exist in the tenant (404 `patient_not_found`) and be active (422 `patient_inactive`, BR-102). Never changes. |
| `payerId` | Required. The payer must exist in the tenant (404 `payer_not_found`) and be active (422 `payer_inactive`, BR-105). It need not be linked to the patient: a guarantor, or another patient's payer, may owe the bill (BR-106). Never changes. |
| `billDate` | Optional; today in India when left out. Not in the future (422 `bill_date_in_future`) and not earlier than today less the hospital's back-dating limit, 7 days unless changed (422 `bill_date_too_old`) (BR-205). |
| `dueDate` | Optional; the bill date plus the hospital's default due days when left out. Not before the bill date (422 `bill_due_date_before_bill_date`). |
| `encounterReference` | Optional; at most 50 characters. The visit or admission in the hospital's own system. |
| `notes` | Optional; at most 1000 characters. |
| `lines` | Optional; at most 200. A draft may have none. |

**A line** (in `POST` and in `PUT .../lines`):

| Field | Rule |
|---|---|
| `serviceItemId` | Optional. With it, the line is priced from the catalogue: the item must exist in the tenant (404 `service_item_not_found`) and be active (422 `service_item_inactive`), unless the draft already has a line for it. Without it, the line is free text. |
| `description` | Required for a free-text line; with a service item, its name when left out. At most 300 characters. |
| `quantity` | Required; more than 0, at most 999999.999, at most 3 decimals. |
| `unitPrice` | Required for a free-text line; with a service item, its default price when left out. 0 or more, at most 4 decimals. |
| `discountAmount` | Optional, 0 when left out. 0 or more, at most 2 decimals, and not more than the line amount (422 `bill_line_discount_exceeds_amount`). |
| `taxRatePercent` | Required for a free-text line; with a service item, its tax rate when left out. 0 to 100, at most 2 decimals. |

A field error of a line is keyed by its position: `lines[1].quantity` is the second line. Every line is checked,
not only the ones before the first error. A line whose quantity times unit price exceeds 9,999,999,999.99 answers
422 `bill_line_amount_too_large`.

**The arithmetic** is the server's, and a client displays what the server returns (BR-202, BR-203). Each product is
rounded to the paisa, away from zero; sums are never rounded again:

```
lineAmount = round(quantity x unitPrice)
taxAmount  = round((lineAmount - discountAmount) x taxRatePercent / 100)
lineTotal  = lineAmount - discountAmount + taxAmount
subTotal, discountTotal, taxTotal = the sums of the lines;  total = subTotal - discountTotal + taxTotal
```

Example: 3 x 333.3333 is 1000.00; less 0.50 leaves 999.50; 5% of that is 49.98 (49.975 rounds up); the line comes
to 1049.48. A line keeps the description, price and tax rate it was written with: a later change of the catalogue
does not alter a bill. `balanceDue` equals `total` on a draft.

**Replacing the lines** (`PUT .../lines`, body `{ "lines": [...] }`) sets every line of the draft, in the order
sent; an empty list removes them all. **Correcting the header** (`PATCH`) sends `billDate` and `dueDate` (both
required), `encounterReference` and `notes`; a text left out is cleared, and a body that names the patient, the
payer, the status or the lines is refused with 400. A bill date that is not changed is not checked against the
back-dating limit again, so a draft that has grown old can still be corrected; a changed one must be allowed today.
**Voiding** (`POST .../void`, body `{ "reason": "..." }`): the reason is required, at most 500 characters. A voided
bill keeps its lines and totals, shows `voidedAtUtc` and `voidReason`, and answers 422 `bill_not_draft` to every
further change.

**Filters of the list:** `patientId`, `payerId`, `status`, and `billDateFrom` and `billDateTo` (`yyyy-MM-dd`, both
ends included; 400 when the end is before the start). They combine.

**Posting** (`POST .../post`, US-21; BR-206, ADR 0022) gives the bill the next number of the fiscal year of its
bill date (`BIL/26-27/000123`), enters `Dr Receivable / Cr Revenue` for the total, raises what the payer owes, and
freezes the bill: one transaction, with the audit record (BR-007). The bill comes back with `number`, `fiscalYear`
(`2026-27`), `postedAtUtc` and the status `Posted`, or `Settled` when it comes to nothing. A draft must have at
least one line, a bill date that is still allowed today (BR-205) and an active payer; the patient is not asked
about again. A posted bill answers **409 `bill_already_posted`** to every edit, void or second posting, and is
corrected only by a credit note or an adjustment.

What a client that posts twice receives:

| It sends | Answer |
|---|---|
| the same `Idempotency-Key` while the first request is still running | 409 `idempotency_in_progress` |
| the same key after the first request finished | the first answer again, with `Idempotent-Replayed: true` |
| another key at the same moment | one succeeds, the other 409 `concurrency_conflict` |
| another key later, with the version from before the posting | 409 `concurrency_conflict` |
| another key later, with the version of the posted bill | 409 `bill_already_posted` |

**Approval (maker-checker; US-22, BR-207, BR-603 to BR-606, ADR 0023).** When the hospital has set a posting approval
threshold and the total of the bill is at or above it, `POST .../post` does not post. It answers **202** with the
bill in the status `PendingApproval`: no number, no ledger entry, nothing owed yet. A hospital without a threshold
posts any bill directly. A bill that waits is locked: an edit, a void or another posting answers
409 `bill_pending_approval`.

A second user then decides, with the version of the waiting bill in `If-Match`:

- `POST .../approve`, body `{ "comment": "..." }` or none: the approval is the posting. The bill comes back
  `Posted` with its number, and the checker is recorded as the one who posted. The date of the bill and its payer
  are checked again at that moment.
- `POST .../reject`, body `{ "comment": "..." }`, the comment required, at most 500 characters: the bill is a
  draft again and can be corrected, voided or posted once more.

The user who posted the bill can do neither: 403 `approval_checker_is_maker`, whatever their role. A draft that
nobody submitted answers 422 `bill_not_pending_approval`. Until users and roles exist (Phase 5), any other user of
the hospital can decide; the rule that the checker is a Supervisor or a TenantAdmin arrives with roles.

### 8.5 Audit trail (US-43; BR-701 to BR-703, ADR 0020)

Read-only. Every change of a patient, payer, service item or bill wrote one record, in the transaction of the
change. Nothing here can be created, changed or deleted: any other verb answers 405.

| Method and path | Success | Failures |
|---|---|---|
| `GET /api/v1/audit-records?entityType=&entityId=&page=&pageSize=` | 200, a page of records, oldest first | 400 when `entityType` or `entityId` is missing, or the page is outside the limits |

`entityType` is `Patient`, `Payer`, `ServiceItem`, `Bill` or `ApprovalRequest`; `entityId` is the id of that record. Both are required.
A record of another tenant, like an id nobody has, has an empty trail: 200 with no items, never 404 or 403.

A record:

| Member | Meaning |
|---|---|
| `id` | A number; records of one trail are in the order of their numbers |
| `occurredAtUtc` | The instant of the change, the same one the record's `updatedAtUtc` shows |
| `userId` | Who made the change; null for system work |
| `correlationId` | The `X-Correlation-Id` of the request that made the change (BR-703) |
| `entityType`, `entityId` | The record that changed |
| `action` | `Registered`, `Created`, `Drafted`, `Updated`, `HeaderUpdated`, `LinesReplaced`, `Deactivated`, `Reactivated`, `Voided`, `Posted`, `SubmittedForApproval`, `ReturnedToDraft`; for an approval request `Opened`, `Approved`, `Rejected` |
| `before`, `after` | The stored record before and after, as JSON objects; `before` is null for a creation. A bill carries its `Lines` |
| `reason` | The reason given, for a void; otherwise null |
| `documentNumber` | Null until documents have numbers |

The members of `before` and `after` are the stored columns, named in PascalCase as the database names them, with
ids as GUIDs, money as numbers and enums by name; they are not the API's DTOs. A correction that sends exactly what
is stored changes nothing and writes no record, and the version of the record stays as it was.

### 8.6 Approval requests (BR-605, ADR 0023)

Read-only: the checker's inbox. A request is opened when a document that needs a second person is posted, and
decided through the document (`.../approve`, `.../reject`); nothing here can be created, changed or deleted.

| Method and path | Success | Failures |
|---|---|---|
| `GET /api/v1/approval-requests?status=&documentType=&page=&pageSize=` | 200, a page of requests, oldest first | 400 |

`status` is `Pending` (the default), `Approved` or `Rejected`; `documentType` is `Bill` so far. A request:
`id`, `documentType`, `documentId`, `action` (`Post`), `status`, `makerUserId`, `makerComment`, `checkerUserId`,
`checkerComment`, `requestedAtUtc`, `decidedAtUtc`. The last three of the checker are null while it is pending.

### 8.7 Payments (US-30; BR-205, BR-301, BR-302, BR-309)

Money received from a payer. Recording a payment posts it: the gapless number, the ledger entry
(Dr Cash / Cr UnappliedCash), the payer's unapplied cash and the audit record are written by one transaction or
not at all (BR-007). A payment is never edited or deleted (BR-006), so no endpoint takes `If-Match`; allocations,
reversals and refunds are separate actions that arrive with their own prompts.

| Method and path | Success | Failures |
|---|---|---|
| `POST /api/v1/payments` (needs `Idempotency-Key`) | 201, the payment, `Location`, `ETag` | 400; 404 `payer_not_found`; 409 `idempotency_in_progress`; 422 `payer_inactive`, `payment_date_in_future`, `payment_date_too_old`, `idempotency_key_reuse` |
| `GET /api/v1/payments/{id}` | 200, the payment, `ETag` | 404 `payment_not_found` |
| `GET /api/v1/payments?payerId=&method=&status=&paymentDateFrom=&paymentDateTo=&page=&pageSize=` | 200, a page of payments | 400 |

The body of a recording (unknown members are refused):

| Field | Required | Rule |
|---|---|---|
| `payerId` | yes | An active payer of the tenant (BR-309) |
| `paymentDate` | no | `yyyy-MM-dd`; today in India when left out. Not in the future and not older than the tenant's back-dating limit (BR-205) |
| `method` | yes | `Cash`, `Card`, `UPI`, `BankTransfer` or `Cheque` |
| `reference` | unless `Cash` | The UPI id, card authorisation, UTR or cheque number; at most 100 characters (BR-301) |
| `amount` | yes | Above zero, at most two decimals, at most 9,999,999,999.99 |
| `notes` | no | At most 500 characters |

A payment: `id`, `number` (`PAY/26-27/000001`), `fiscalYear` (that of the payment date, BR-004), `payerId`,
`paymentDate`, `method`, `reference`, `amount`, `unappliedAmount` (equal to `amount` until something is allocated),
`status` (`Posted` so far), `notes`, `createdAtUtc`, `updatedAtUtc`, `version`.

The list is ordered by payment date, newest first, then by number, newest first; the filters combine, and
`paymentDateTo` may not be before `paymentDateFrom`. The audit trail of a payment is read with
`GET /api/v1/audit-records?entityType=Payment&entityId=...`; its first record has the action `Recorded` and the
number of the payment.
