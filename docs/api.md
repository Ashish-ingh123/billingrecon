# API

The REST conventions of the BillingRecon API, every endpoint, and the catalogue of its error codes (section 9). Written
with the first resources, patients and payers (Prompt 2.2, ADR 0018), and extended slice by slice; completed with
Prompt 6.2, since when `ApiDocumentationTests` fails for an endpoint or an error code that is not here.

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
- **HTTPS only** (ADR 0035). TLS ends at the proxy in front of the API. Outside Development a request that did not
  arrive over HTTPS gets 400 `https_required`, before anything of it is read and with no redirect: a client is
  configured with the `https` address. An answer over HTTPS carries `Strict-Transport-Security: max-age=31536000`. The
  two health endpoints also answer over plain HTTP, for probes.
- **Hosts.** A request for a host name the deployment does not list (`AllowedHosts`) gets 400 `host_not_allowed`; the
  health endpoints answer for any host.
- **Browsers on another origin** (CORS) may call the API only from the origins the deployment lists (`Cors:Origins`;
  none by default, `http://localhost:5173` in Development): the methods `GET`, `POST`, `PUT` and `PATCH`; the request
  headers `Authorization`, `Content-Type`, `If-Match`, `Idempotency-Key` and `X-Correlation-Id`; and they may read the
  answer headers `ETag`, `Location`, `Retry-After`, `X-Correlation-Id` and `Idempotent-Replayed`. No credentials: the
  token travels in `Authorization`, never in a cookie. A preflight needs no token.
- **What every answer of `/api` carries:** `Cache-Control: no-store` (nothing is kept by a browser or a cache) unless
  the endpoint sets its own, `X-Content-Type-Options: nosniff` and `Content-Security-Policy: default-src 'none';
  frame-ancestors 'none'`. No answer has a `Server` header.

## 2. Authentication and tenancy

**Every request carries a bearer token** (`Authorization: Bearer <token>`) that the client got by signing in
(section 8.12; ADR 0031). The token is a JWT the API issues and checks itself: signed with HS256, valid for 60 minutes,
carrying the user (`sub`), the tenant (`tenant_id`) and the roles (`role`) (BR-602). **The tenant comes from the token
only**, never from a body, a query string, a route or a header (BR-002). There is no refresh token: when the token
expires, the client signs in again. A token stays valid until it expires, also for a user disabled after signing in.

**Deny by default.** A request without a valid token gets 401 `unauthorized` and a bare `WWW-Authenticate: Bearer`
(why a token was refused is not told), on every endpoint, also on a path that does not exist. The only exceptions are
the sign-in, `/health/live`, `/health/ready` and, in Development, the OpenAPI document and the Scalar page; they read
no token. In the **Development** environment a request without an `Authorization` header acts as the development
user of `appsettings.Development.json` (a TenantAdmin), so the Scalar page works without signing in; a request with a
token is checked as everywhere else.

**Roles.** Every endpoint names one permission of the matrix in `docs/requirements.md` section 1 (ADR 0032). A
signed-in user whose roles the permission does not admit gets 403 `forbidden`, before anything of the request is read;
a user with several roles may do what any of them may. The roles are those of the token, so a change of roles applies
at the next sign-in. In short:

| Endpoints | TenantAdmin | Supervisor | BillingClerk | Cashier | Viewer |
|---|---|---|---|---|---|
| Users (section 8.11), settings (8.13), asking for a prefix change (8.14) | ✓ | | | | |
| Reading the prefix changes (8.14) | ✓ | ✓ | | | |
| Patients, payers, service items: read / change | ✓ / ✓ | ✓ / ✓ | ✓ / ✓ | ✓ / | ✓ / |
| Bills: read / draft, edit, void, post | ✓ / ✓ | ✓ / ✓ | ✓ / ✓ | ✓ / | ✓ / |
| Credit notes: read / draft, edit, void, submit, apply | ✓ / ✓ | ✓ / ✓ | ✓ / ✓ | ✓ / | ✓ / |
| Adjustments: read / draft, edit, void, submit | ✓ / ✓ | ✓ / ✓ | ✓ / ✓ | | ✓ / |
| Payments and allocations: read / record, allocate | ✓ / ✓ | ✓ / ✓ | ✓ / ✓ | ✓ / ✓ | ✓ / |
| Refunds: read / draft, edit, void, submit | ✓ / ✓ | ✓ / ✓ | | ✓ / ✓ | ✓ / |
| Ask for a reversal (payment, allocation, application of credit) | ✓ | ✓ | ✓ | ✓ | |
| Approve, reject, and the inbox `GET /approval-requests` | ✓ | ✓ | | | |
| Audit trail (the trails of a user, the tenant, its prefix changes and its number series: TenantAdmin only) | ✓ | ✓ | ✓ | ✓ | ✓ |
| Reports (section 8.15) | ✓ | ✓ | ✓ | ✓ | ✓ |
| Dashboard (section 8.16) | ✓ | ✓ | | | ✓ |
| `GET /api/v1/system/info` | ✓ | ✓ | ✓ | ✓ | ✓ |

A checker also has to be another user than the maker (BR-603): a decision by the maker is 403
`approval_checker_is_maker`, and the decision itself checks the role again (403 `approval_checker_role`), whatever the
endpoint.

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
`pageSize`) or, for a header, by its name (`If-Match`). The table below gives the rules; section 9 lists every code.

| Status | When | `errorCode` |
|---|---|---|
| 400 | A field is missing or invalid, the body cannot be read, a query value is out of range | `validation_failed` |
| 400 | A financial POST without a usable `Idempotency-Key` (section 7) | `idempotency_key_missing` |
| 400 | Outside Development, a request that did not arrive over HTTPS (section 1) | `https_required` |
| 400 | A host name the API does not answer for (section 1) | `host_not_allowed` |
| 401 | No token, or one that is not valid: expired, not signed by this API, without a tenant or a user (section 2) | `unauthorized` |
| 401 | A sign-in that is refused, whatever the reason (section 8.12) | `invalid_credentials` |
| 403 | The user's roles do not allow the endpoint (section 2), or a trail that is the TenantAdmin's (section 8.5) for anyone else | `forbidden` |
| 403 | A decision by the maker, or by a user who is neither Supervisor nor TenantAdmin (BR-603) | `approval_checker_is_maker`, `approval_checker_role` |
| 404 | The record does not exist for the tenant (another tenant's included), also when a request only refers to it | `patient_not_found`, `payer_not_found`, `service_item_not_found`, `bill_not_found`, `payment_not_found`, `credit_note_not_found`, `adjustment_not_found`; `not_found` for an unknown route |
| 409 | The record changed since it was read (BR-804) | `concurrency_conflict` |
| 409 | The first request with this `Idempotency-Key` is still running (section 7) | `idempotency_in_progress` |
| 409 | A uniqueness rule (BR-101, BR-104, the code of a service item, a prefix of one document type, one waiting prefix change per type) | `patient_mrn_duplicate`, `patient_already_linked`, `service_item_code_duplicate`, `prefix_in_use`, `prefix_change_pending`; `duplicate_key` where no rule applies |
| 422 | A business rule refuses the request | see the endpoints |
| 422 | The `Idempotency-Key` was used for a different request (section 7) | `idempotency_key_reuse` |
| 428 | A change of an existing record without `If-Match` | `precondition_required` |
| 429 | Too many sign-ins from one address, or too many refused for one account (section 8.12); `Retry-After` gives the seconds to wait | `too_many_sign_ins` |
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
`createdAtUtc`, `updatedAtUtc`, `version`. **A line**: `id` (since Prompt 4.3; what a credit note line names), `lineNo`, `serviceItemId`, `description`, `quantity`,
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

Read-only: the trail of any record, a document included. Every state change of a patient, payer, service item, bill,
credit note, payment, adjustment, refund, approval request, user, the tenant, a prefix change or a number series wrote
one record, in the transaction of the change. Nothing here can be created, changed or deleted: any other verb answers
405.

| Method and path | Success | Failures |
|---|---|---|
| `GET /api/v1/audit-records?entityType=&entityId=&page=&pageSize=` | 200, a page of records, oldest first | 400 when `entityType` or `entityId` is missing, or the page is outside the limits; 403 for the trail of a user, unless the caller is a TenantAdmin |

`entityType` is `Patient`, `Payer`, `ServiceItem`, `Bill`, `ApprovalRequest` or the type of any later record (`User`,
`Payment`, ...); `entityId` is the id of that record. Both are required. A record of another tenant, like an id nobody
has, has an empty trail: 200 with no items, never 404 or 403. Every role reads the trail (US-43), except the trails of a
user (`entityType=User`), of the tenant (`Tenant`), of its prefix changes (`PrefixChange`) and of its number series
(`NumberSeries`), in any case, which only a TenantAdmin reads: the users and the settings are the TenantAdmin's alone
(ADR 0032, ADR 0033).

A record:

| Member | Meaning |
|---|---|
| `id` | A number; records of one trail are in the order of their numbers |
| `occurredAtUtc` | The instant of the change, the same one the record's `updatedAtUtc` shows |
| `userId` | Who made the change; null for system work |
| `correlationId` | The `X-Correlation-Id` of the request that made the change (BR-703) |
| `entityType`, `entityId` | The record that changed |
| `action` | What happened: the name of the domain event without the name of its record (the list below) |
| `before`, `after` | The stored record before and after, as JSON objects; `before` is null for a creation. A bill carries its `Lines` |
| `reason` | The reason given, for a void; otherwise null |
| `documentNumber` | The document's number once it has one (a bill, credit note, payment, adjustment, refund); null before, and for records without numbers |

The actions, by `entityType`, each one state change (every state change raises one, ADR 0020):

| `entityType` | Actions |
|---|---|
| `Patient` | `Registered`, `Updated`, `Deactivated`, `Reactivated` |
| `Payer` | `Created`, `Updated`, `Deactivated`, `Reactivated` |
| `ServiceItem` | `Created`, `Updated`, `Deactivated`, `Reactivated` |
| `Bill` | `Drafted`, `HeaderUpdated`, `LinesReplaced`, `Voided`, `SubmittedForApproval`, `ReturnedToDraft`, `Posted`, `CreditApplied` (a payment, credit or adjustment lowered what is owed), `CreditReversed` (one of those was taken back) |
| `CreditNote` | `Drafted`, `HeaderUpdated`, `LinesReplaced`, `Voided`, `SubmittedForApproval`, `ReturnedToDraft`, `Posted`, `Applied`, `ApplicationReversalRequested`, `ApplicationReversalRejected`, `ApplicationReversed`, `Refunded` |
| `Payment` | `Recorded`, `Allocated`, `AllocationReversalRequested`, `AllocationReversalRejected`, `AllocationReversed`, `ReversalRequested`, `ReversalRejected`, `Reversed`, `Refunded` |
| `Adjustment` | `Drafted`, `Updated`, `Voided`, `SubmittedForApproval`, `ReturnedToDraft`, `Posted` |
| `Refund` | `Drafted`, `Updated`, `Voided`, `SubmittedForApproval`, `ReturnedToDraft`, `Posted` |
| `ApprovalRequest` | `Opened`, `Approved`, `Rejected` |
| `User` | `Created`, `Updated`, `RolesReplaced`, `Disabled`, `Enabled`, `PasswordReset`, `SignedIn` |
| `Tenant` | `SettingsChanged`, `PrefixChanged` |
| `PrefixChange` | `Requested`, `Approved`, `Rejected` |
| `NumberSeries` | `PrefixChanged` |

The members of `before` and `after` are the stored columns, named in PascalCase as the database names them, with
ids as GUIDs, money as numbers and enums by name; they are not the API's DTOs. A correction that sends exactly what
is stored changes nothing and writes no record, and the version of the record stays as it was.

### 8.6 Approval requests (BR-605, ADR 0023)

Read-only: the checker's inbox, for Supervisors and TenantAdmins (ADR 0032). A request is opened when a document that
needs a second person is posted, and decided through the document (`.../approve`, `.../reject`); nothing here can be
created, changed or deleted.

| Method and path | Success | Failures |
|---|---|---|
| `GET /api/v1/approval-requests?status=&documentType=&page=&pageSize=` | 200, a page of requests, oldest first | 400 |

`status` is `Pending` (the default), `Approved` or `Rejected`; `documentType` is `Bill` so far. A request:
`id`, `documentType`, `documentId`, `action` (`Post`), `status`, `makerUserId`, `makerComment`, `checkerUserId`,
`checkerComment`, `requestedAtUtc`, `decidedAtUtc`. The last three of the checker are null while it is pending.

### 8.7 Payments (US-30; BR-205, BR-301, BR-302, BR-309)

Money received from a payer. Recording a payment posts it: the gapless number, the ledger entry
(Dr Cash / Cr UnappliedCash), the payer's unapplied cash and the audit record are written by one transaction or
not at all (BR-007). A payment is never edited or deleted (BR-006). Allocating it to bills is an action of its own
(below) and the one request here that takes `If-Match`; reversals are below, and refunds are documents of their own
(section 8.10).

| Method and path | Success | Failures |
|---|---|---|
| `POST /api/v1/payments` (needs `Idempotency-Key`) | 201, the payment, `Location`, `ETag` | 400; 404 `payer_not_found`; 409 `idempotency_in_progress`; 422 `payer_inactive`, `payment_date_in_future`, `payment_date_too_old`, `idempotency_key_reuse` |
| `GET /api/v1/payments/{id}` | 200, the payment, `ETag` | 404 `payment_not_found` |
| `POST /api/v1/payments/{id}/allocations` (needs `Idempotency-Key` and `If-Match`) | 200, the payment with its allocations, `ETag` | 400; 404 `payment_not_found`, `bill_not_found`; 409 `concurrency_conflict`, `idempotency_in_progress`; 422 `allocation_exceeds_unapplied`, `allocation_exceeds_balance_due`, `allocation_payer_mismatch`, `bill_not_posted`, `idempotency_key_reuse`; 428 |
| `GET /api/v1/payments/{id}/allocations` | 200, the payment with its allocations, `ETag` | 404 `payment_not_found` |
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

**Allocating a payment (US-31; BR-303, BR-304, BR-208; ADR 0024).** One request applies the payment to one or more
posted bills of the payer it came from, all of them or none. For each bill the payment's `unappliedAmount` and the
bill's `balanceDue` fall by the same amount, the ledger gets Dr UnappliedCash(payment) / Cr Receivable(bill), and
the bill becomes `PartiallyPaid` or, when nothing is owed any more, `Settled`. `If-Match` carries the version of the
**payment**. The body (unknown members are refused):

| Field | Required | Rule |
|---|---|---|
| `allocations` | yes | Between 1 and 50 lines; a bill is named once |
| `allocations[].billId` | yes | A posted bill of the tenant, owed by the payer of the payment |
| `allocations[].amount` | yes | Above zero, at most two decimals; not more than the bill is owed, and the sum not more than the payment has unapplied |

The answer, and what the `GET` returns: `payment` (as above, with its new `unappliedAmount` and `version`) and
`allocations`, every allocation made from the payment, the oldest first: `id`, `billId`, `amount`, `status`
(`Active` so far), `allocatedAtUtc`, `allocatedByUserId`.

When two cashiers apply money to the same bill at the same moment and only one amount fits, one succeeds and the
other gets 409 `concurrency_conflict` and nothing of its request is written: read the payment and the bill again. A
request that arrives after the first has finished gets 422 `allocation_exceeds_balance_due`. A payment can be
allocated after its payer was deactivated. Each allocation writes an audit record on the payment and one on the bill.

**Reversing an allocation or a payment (US-32, US-33; BR-305, BR-306; ADR 0027).** A cashier asks with a reason;
a second person approves, which is the reversal, or rejects, which leaves everything as it was. Nothing is deleted:
the rows stay, marked `Reversed`, and the ledger gets mirror postings. Every request needs `If-Match` with the
version of the **payment** and an `Idempotency-Key`, and answers the payment with its allocations.

| Method and path | Success | Failures |
|---|---|---|
| `POST /api/v1/payments/{id}/allocations/{allocationId}/reverse` | **202**, the allocation in `PendingReversal` | 400; 403 `approval_needs_user`; 404 `payment_not_found`, `allocation_not_found`; 409 `concurrency_conflict`, `allocation_pending_reversal`, `allocation_reversed`, `payment_pending_reversal`, `payment_reversed`; 428 |
| `POST /api/v1/payments/{id}/allocations/{allocationId}/approve-reversal` (body optional) | 200, the allocation `Reversed` | 403 `approval_checker_is_maker`, `approval_needs_user`; 404; 409 `concurrency_conflict`, `allocation_reversed`; 422 `allocation_not_pending_reversal`; 428 |
| `POST /api/v1/payments/{id}/allocations/{allocationId}/reject-reversal` | 200, the allocation `Active` again | 400 without a comment; otherwise as `approve-reversal` |
| `POST /api/v1/payments/{id}/reverse` | **202**, the payment in `PendingReversal` | 400; 403 `approval_needs_user`; 404 `payment_not_found`; 409 `concurrency_conflict`, `payment_pending_reversal`, `payment_reversed`, `allocation_pending_reversal`; 422 `payment_refunded` (a refund was posted from it, BR-306); 428 |
| `POST /api/v1/payments/{id}/approve-reversal` (body optional) | 200, the payment `Reversed` | 403 `approval_checker_is_maker`, `approval_needs_user`; 404; 409 `concurrency_conflict`, `payment_reversed`; 422 `payment_not_pending_reversal`; 428 |
| `POST /api/v1/payments/{id}/reject-reversal` | 200, the payment `Posted` again | 400 without a comment; otherwise as `approve-reversal` |

`reverse` takes `reason` (required, at most 500 characters); `reject-reversal` takes `comment` (required);
`approve-reversal` takes an optional `comment`.

Approving the reversal of an allocation gives its amount back to the payment (`unappliedAmount` rises) and to the
bill (`balanceDue` rises, and the status moves back to `PartiallyPaid` or `Posted`), and enters
Dr Receivable(bill) / Cr UnappliedCash(payment). Approving the reversal of a payment first reverses every
allocation that is still applied, then enters Dr UnappliedCash(payment) / Cr Cash for the whole amount: the payment
ends `Reversed` with `unappliedAmount` 0, keeps its number, and is never used again. While a payment waits for its
reversal nothing can be allocated from it (409 `payment_pending_reversal`). Only one decision waits at a time: a
payment cannot be asked for reversal while one of its allocations waits, nor the other way round.

A payment now also has `reversedAtUtc` and `reversalReason`, and its `status` is `Posted`, `PendingReversal` or
`Reversed`. An allocation has `reversedAtUtc` and `reversalReason` too, and its `status` is `Active`,
`PendingReversal` or `Reversed`. The audit trail of the payment shows `AllocationReversalRequested`,
`AllocationReversalRejected`, `AllocationReversed`, `ReversalRequested`, `ReversalRejected` and `Reversed`, each
with the reason; the bill shows `CreditReversed`.

**Two checkers at the same moment (every `approve` and `reject` of this API).** One decision stands. The other gets
409: `concurrency_conflict` when it had read the document before the first was committed, or the code of the new
state when after. Until Prompt 4.5 a narrow ordering of the two answered 500 instead (ADR 0027, point 9).

### 8.8 Credit notes (US-24; BR-209 to BR-214, BR-604; ADR 0025)

A credit note corrects a posted bill: it lowers what was billed, and with it what the payer owes. It is drafted
against exactly one posted bill and belongs to that bill's payer. Every credit note needs a second person: the
maker's `post` hands it over, and the checker's `approve` is the posting, which takes the gapless number, enters
Dr Revenue / Cr CreditAvailable for the total, and applies the credit to the bill as far as the bill is owed. What
is left stays as `availableAmount` and can be applied to other posted bills of the same payer.

| Method and path | Success | Failures |
|---|---|---|
| `POST /api/v1/credit-notes` (needs `Idempotency-Key`) | 201, the credit note, `Location`, `ETag` | 400; 404 `bill_not_found`; 422 `bill_not_posted`, `credit_note_date_in_future`, `credit_note_date_too_old`, `credit_note_date_before_bill_date`, `credit_note_bill_line_not_found`, `credit_note_line_discount_exceeds_amount`, `credit_note_line_amount_too_large` |
| `GET /api/v1/credit-notes/{id}` | 200, the credit note, `ETag` | 404 `credit_note_not_found` |
| `GET /api/v1/credit-notes?billId=&payerId=&status=&page=&pageSize=` | 200, a page of credit notes without lines | 400 |
| `PATCH /api/v1/credit-notes/{id}` (needs `If-Match`) | 200 | 400; 404; 409 `concurrency_conflict`, `credit_note_pending_approval`, `credit_note_already_posted`; 422 `credit_note_not_draft` and the date codes; 428 |
| `PUT /api/v1/credit-notes/{id}/lines` (needs `If-Match`) | 200 | as `PATCH`, and the line codes of `POST` |
| `POST /api/v1/credit-notes/{id}/void` (needs `If-Match`, `Idempotency-Key`) | 200 | as `PATCH` |
| `POST /api/v1/credit-notes/{id}/post` (needs `If-Match`, `Idempotency-Key`; no body) | **202**, the credit note in `PendingApproval` | 403 `approval_needs_user`; 404; 409 as `PATCH`; 422 `credit_note_not_draft`, `credit_note_has_no_lines`, `credit_note_total_zero`, `credit_note_exceeds_bill_total`, `credit_note_line_exceeds_bill_line` and the date codes; 428 |
| `POST /api/v1/credit-notes/{id}/approve` (needs `If-Match`, `Idempotency-Key`; body optional) | 200, the posted credit note | 403 `approval_checker_is_maker`, `approval_needs_user`; 404; 409 `concurrency_conflict`, `credit_note_already_posted`; 422 `credit_note_not_pending_approval`, `credit_note_not_draft`, the two cap codes and the date codes; 428 |
| `POST /api/v1/credit-notes/{id}/reject` (needs `If-Match`, `Idempotency-Key`) | 200, the credit note as a draft again | 400 without a comment; 403, 404, 409, 422 as `approve`; 428 |
| `POST /api/v1/credit-notes/{id}/applications` (needs `If-Match`, `Idempotency-Key`) | 200, the credit note with its applications | 400; 404 `credit_note_not_found`, `bill_not_found`; 409 `concurrency_conflict`; 422 `credit_note_not_posted`, `credit_exceeds_available`, `allocation_exceeds_balance_due`, `allocation_payer_mismatch`, `bill_not_posted`; 428 |

The body of a draft (unknown members are refused):

| Field | Required | Rule |
|---|---|---|
| `billId` | yes | A posted bill of the tenant. Cannot be changed later |
| `creditNoteDate` | no | `yyyy-MM-dd`; today in India when left out. Not in the future, not older than the back-dating limit (BR-205), not before the bill date (BR-214) |
| `reason` | yes | At most 500 characters |
| `lines` | no | At most 200; replaced as a whole by `PUT .../lines` |
| `lines[].billLineId` | no | The `id` of a line of the bill. With it, `description`, `unitPrice` and `taxRatePercent` may be left out and are taken from that line |
| `lines[].quantity` | yes | More than 0, at most three decimals |
| `lines[].description`, `unitPrice`, `taxRatePercent` | without `billLineId` | As on a bill line |
| `lines[].discountAmount` | no | 0 or more, not more than the line amount; 0 when left out |

`PATCH` takes `creditNoteDate` and `reason`, both required. `void` takes `reason`; `reject` takes `comment`, both
required; `approve` takes an optional `comment`. `applications` takes `applications`: 1 to 50 lines of `billId` and
`amount`, each bill once.

A credit note: `id`, `billId`, `payerId`, `patientId`, `creditNoteDate`, `reason`, `status` (`Draft`,
`PendingApproval`, `Posted`, `Voided`), `number` (`CN/26-27/000001`), `fiscalYear` and `postedAtUtc` (all three null
until it is posted), `subTotal`, `discountTotal`, `taxTotal`, `total`, `availableAmount`, `voidedAtUtc`,
`voidReason`, `lines`, `applications`, `createdAtUtc`, `updatedAtUtc`, `version`. **A line** is a bill line with
`billLineId` in the place of `serviceItemId` and without an `id`. **An application**: `id`, `billId`, `amount`,
`status` (`Active`, `PendingReversal` or `Reversed`), `allocatedAtUtc`, `allocatedByUserId`, `appliedAtPosting`
(true for the application to its own bill made by the approval), `reversedAtUtc` and `reversalReason` (null unless
reversed).

**The caps (BR-210, BR-211).** The posted credit notes of a bill never come to more than the bill, and those that
name one line of it never to more than that line. Both are checked when the maker asks and again when the checker
approves, against the credit notes posted by then. Two credit notes approved at the same moment that do not both
fit: one is posted, the other gets 409 `concurrency_conflict`, takes no number, and still waits; approving it
again answers 422.

**What each state answers:** a draft can be edited, voided and handed over; one that waits answers 409
`credit_note_pending_approval` to all three; a posted one answers 409 `credit_note_already_posted`; a voided draft
answers 422 `credit_note_not_draft`. The audit trail is read with
`GET /api/v1/audit-records?entityType=CreditNote&entityId=...`: `Drafted`, `LinesReplaced`, `HeaderUpdated`,
`SubmittedForApproval`, `ReturnedToDraft`, `Posted`, `Applied`, `Voided`, and for reversals
`ApplicationReversalRequested`, `ApplicationReversalRejected` and `ApplicationReversed`.

**Reversing an application of credit (ADR 0028).** Shaped like the reversal of a payment allocation (section 8.7): a
clerk asks with a reason; a second person approves, which is the reversal, or rejects, which leaves everything as it
was. Only an application made later through `applications` can be reversed: the one the approval made to the credit
note's own bill (`appliedAtPosting` true) answers 422 `credit_application_made_at_posting`. Every request needs
`If-Match` with the version of the **credit note** and an `Idempotency-Key`, and answers the credit note with its
applications.

| Method and path | Success | Failures |
|---|---|---|
| `POST /api/v1/credit-notes/{id}/applications/{applicationId}/reverse` | **202**, the application in `PendingReversal` | 400; 403 `approval_needs_user`; 404 `credit_note_not_found`, `credit_application_not_found`; 409 `concurrency_conflict`, `credit_application_pending_reversal`, `credit_application_reversed`; 422 `credit_application_made_at_posting`; 428 |
| `POST /api/v1/credit-notes/{id}/applications/{applicationId}/approve-reversal` (body optional) | 200, the application `Reversed` | 403 `approval_checker_is_maker`, `approval_needs_user`; 404; 409 `concurrency_conflict`, `credit_application_reversed`; 422 `credit_application_not_pending_reversal`; 428 |
| `POST /api/v1/credit-notes/{id}/applications/{applicationId}/reject-reversal` | 200, the application `Active` again | 400 without a comment; otherwise as `approve-reversal` |

`reverse` takes `reason` (required, at most 500 characters); `reject-reversal` takes `comment` (required);
`approve-reversal` takes an optional `comment`. The reason is kept by the approval request and reaches the
application's `reversalReason` when the checker approves.

Approving gives the amount back to the credit note (`availableAmount` rises) and to the bill (`balanceDue` rises,
and the status moves back to `PartiallyPaid` or `Posted`), enters Dr Receivable(bill) / Cr CreditAvailable(credit
note), and takes no document number. The amount can then be applied to another bill. One decision waits at a time
for each application; while it waits, its amount is still applied and the credit note can still apply the rest of
its credit, so a decision needs the version that followed. The bill's audit trail shows `CreditReversed`.

### 8.9 Adjustments (BR-501 to BR-503, BR-604; ADR 0026)

An adjustment lowers a posted bill by a discount, a write-off or a rounding difference of less than one rupee; no
money and no credit note is involved. Every adjustment needs a second person, a rounding included: the maker's
`post` hands it over, and the checker's `approve` is the posting, which takes the gapless number, lowers the bill
and enters Dr Adjustments / Cr Receivable(bill). A posted adjustment is not reversed.

| Method and path | Success | Failures |
|---|---|---|
| `POST /api/v1/adjustments` (needs `Idempotency-Key`) | 201, the adjustment, `Location`, `ETag` | 400; 404 `bill_not_found`; 422 `bill_not_posted`, `adjustment_rounding_too_large`, `adjustment_date_in_future`, `adjustment_date_too_old`, `adjustment_date_before_bill_date` |
| `GET /api/v1/adjustments/{id}` | 200, the adjustment, `ETag` | 404 `adjustment_not_found` |
| `GET /api/v1/adjustments?billId=&payerId=&status=&kind=&page=&pageSize=` | 200, a page of adjustments | 400 |
| `PUT /api/v1/adjustments/{id}` (needs `If-Match`) | 200 | 400; 404; 409 `concurrency_conflict`, `adjustment_pending_approval`, `adjustment_already_posted`; 422 `adjustment_not_draft` and the codes of `POST`; 428 |
| `POST /api/v1/adjustments/{id}/void` (needs `If-Match`, `Idempotency-Key`) | 200 | as `PUT` |
| `POST /api/v1/adjustments/{id}/post` (needs `If-Match`, `Idempotency-Key`; no body) | **202**, the adjustment in `PendingApproval` | 403 `approval_needs_user`; 404; 409 as `PUT`; 422 `adjustment_not_draft`, `adjustment_exceeds_balance_due` and the date codes; 428 |
| `POST /api/v1/adjustments/{id}/approve` (needs `If-Match`, `Idempotency-Key`; body optional) | 200, the posted adjustment | 403 `approval_checker_is_maker`, `approval_needs_user`; 404; 409 `concurrency_conflict`, `adjustment_already_posted`; 422 `adjustment_not_pending_approval`, `adjustment_not_draft`, `adjustment_exceeds_balance_due` and the date codes; 428 |
| `POST /api/v1/adjustments/{id}/reject` (needs `If-Match`, `Idempotency-Key`) | 200, the adjustment as a draft again | 400 without a comment; 403, 404, 409, 422 as `approve`; 428 |

The body of a draft, and of `PUT`, which replaces all of it (unknown members are refused):

| Field | Required | Rule |
|---|---|---|
| `billId` | yes, on `POST` only | A posted bill of the tenant. Cannot be changed later |
| `adjustmentDate` | no on `POST`, yes on `PUT` | `yyyy-MM-dd`; today in India when left out. Not in the future, not older than the back-dating limit (BR-205), not before the bill date (BR-501) |
| `kind` | yes | `Discount`, `WriteOff` or `Rounding` |
| `amount` | yes | Above zero, at most two decimals, at most 9,999,999,999.99; less than 1.00 for a `Rounding` |
| `reason` | yes | At most 500 characters |

`void` takes `reason`; `reject` takes `comment`, both required; `approve` takes an optional `comment`.

An adjustment: `id`, `billId`, `payerId`, `adjustmentDate`, `kind`, `amount`, `reason`, `status` (`Draft`,
`PendingApproval`, `Posted`, `Voided`), `number` (`ADJ/26-27/000001`), `fiscalYear` and `postedAtUtc` (all three null
until it is posted), `voidedAtUtc`, `voidReason`, `createdAtUtc`, `updatedAtUtc`, `version`.

**The cap (BR-501).** The amount is not more than the bill is still owed. It is asked when the maker submits and
again when the checker approves: an adjustment that fitted and no longer does, because the bill was paid or
credited meanwhile, answers 422 `adjustment_exceeds_balance_due`, takes no number and still waits; the checker
rejects it and the maker corrects the amount. An approval and a payment for the same last rupees at the same
moment: one succeeds, the other gets 409 or 422.

The audit trail is read with `GET /api/v1/audit-records?entityType=Adjustment&entityId=...`: `Drafted`, `Updated`,
`SubmittedForApproval`, `ReturnedToDraft`, `Posted` (with the reason), `Voided`. The bill shows `CreditApplied`.

### 8.10 Refunds (US-34; BR-307 to BR-309, BR-604; ADR 0029)

A refund pays money back to a payer from exactly one source: what a payment has unapplied, or what a posted credit
note has available. It belongs to the payer of its source. Every refund needs a second person: the maker's `post`
hands it over, and the checker's `approve` is the posting, which takes the gapless number, lowers the source and
enters Dr UnappliedCash(payment) or CreditAvailable(credit note) / Cr Cash, dated the refund date. A posted refund
is never changed or reversed (BR-308): a refund made in error is corrected by recording a new payment.

| Method and path | Success | Failures |
|---|---|---|
| `POST /api/v1/refunds` (needs `Idempotency-Key`) | 201, the refund, `Location`, `ETag` | 400; 404 `payment_not_found`, `credit_note_not_found`; 409 `payment_pending_reversal`, `payment_reversed`; 422 `credit_note_not_posted`, `payer_inactive`, `refund_date_in_future`, `refund_date_too_old`, `refund_date_before_source_date` |
| `GET /api/v1/refunds/{id}` | 200, the refund, `ETag` | 404 `refund_not_found` |
| `GET /api/v1/refunds?payerId=&status=&paymentId=&creditNoteId=&page=&pageSize=` | 200, a page of refunds | 400 |
| `PUT /api/v1/refunds/{id}` (needs `If-Match`) | 200 | 400; 404; 409 `concurrency_conflict`, `refund_pending_approval`, `refund_already_posted` and the source codes of `POST`; 422 `refund_not_draft` and the date codes; 428 |
| `POST /api/v1/refunds/{id}/void` (needs `If-Match`, `Idempotency-Key`) | 200 | 400; 404; 409 and 422 as `PUT`; 428 |
| `POST /api/v1/refunds/{id}/post` (needs `If-Match`, `Idempotency-Key`; no body) | **202**, the refund in `PendingApproval` | 403 `approval_needs_user`; 404; 409 as `PUT`; 422 `refund_not_draft`, `refund_exceeds_available`, `credit_note_not_posted` and the date codes; 428 |
| `POST /api/v1/refunds/{id}/approve` (needs `If-Match`, `Idempotency-Key`; body optional) | 200, the posted refund | 403 `approval_checker_is_maker`, `approval_needs_user`; 404; 409 `concurrency_conflict`, `refund_already_posted`, `payment_pending_reversal`, `payment_reversed`; 422 `refund_not_pending_approval`, `refund_not_draft`, `refund_exceeds_available`, `payer_inactive` and the date codes; 428 |
| `POST /api/v1/refunds/{id}/reject` (needs `If-Match`, `Idempotency-Key`) | 200, the refund as a draft again | 400 without a comment; 403, 404, 409, 422 as `approve`; 428 |

The body of a draft (unknown members are refused); `PUT` takes the same fields except the source, all required
except `reference`:

| Field | Required | Rule |
|---|---|---|
| `paymentId` or `creditNoteId` | exactly one, on `POST` only | A payment of the tenant that is posted (neither waiting for its reversal nor reversed), or a posted credit note of the tenant. Cannot be changed later |
| `refundDate` | no on `POST`, yes on `PUT` | `yyyy-MM-dd`; today in India when left out. Not in the future, not older than the back-dating limit (BR-205), not before the date of the source (BR-307) |
| `amount` | yes | Above zero, at most two decimals, at most 9,999,999,999.99 |
| `method` | yes | `Cash`, `Card`, `UPI`, `BankTransfer` or `Cheque` |
| `reference` | unless `Cash` | The UPI id, card authorisation, UTR or cheque number of the payout; at most 100 characters (BR-301) |
| `reason` | yes | At most 500 characters |

`void` takes `reason`; `reject` takes `comment`, both required; `approve` takes an optional `comment`.

A refund: `id`, `payerId`, `sourceKind` (`Payment` or `CreditNote`), `paymentId`, `creditNoteId` (one of them null),
`refundDate`, `amount`, `method`, `reference`, `reason`, `status` (`Draft`, `PendingApproval`, `Posted`, `Voided`),
`number` (`REF/26-27/000001`), `fiscalYear` and `postedAtUtc` (all three null until it is posted), `voidedAtUtc`,
`voidReason`, `createdAtUtc`, `updatedAtUtc`, `version`.

**The cap and the payer (BR-307, BR-309).** The amount is not more than the source has left. It is asked when the
maker submits and again when the checker approves: a refund that fitted and no longer does, because the payment was
allocated or the credit applied meanwhile, answers 422 `refund_exceeds_available`, takes no number and still waits.
The payer must be active when the refund is drafted and when it is approved. Two refunds of one payment approved at
the same moment that do not both fit, or a refund approved while the same payment is allocated: one succeeds, the
other gets 409 or 422 and writes nothing.

**What a refund does to its source.** A refund that waits does not lock its source: the payment can still be
allocated and the credit note applied. A posted refund lowers the payment's `unappliedAmount` or the credit note's
`availableAmount`, and a payment with a posted refund can no longer be reversed (422 `payment_refunded`, BR-306).

The audit trail is read with `GET /api/v1/audit-records?entityType=Refund&entityId=...`: `Drafted`, `Updated`,
`SubmittedForApproval`, `ReturnedToDraft`, `Posted` (with the number and the reason), `Voided`. The payment or the
credit note shows `Refunded`, with its own number.

### 8.11 Users (US-01; BR-601; ADR 0030)

The users of the tenant, each with an e-mail address unique in the tenant without regard to case, a display name, a
password, a status and one or more roles. Users are reference data like patients and payers: the POSTs take **no**
`Idempotency-Key` (a stored fingerprint of a body that holds a password is not allowed), and the unique address and
`If-Match` protect them. No answer ever carries the password or its hash. Only a TenantAdmin may call them, reading
included (section 2; ADR 0032).

| Method and path | Success | Failures |
|---|---|---|
| `POST /api/v1/users` | 201, the user, `Location`, `ETag` | 400; 409 `user_email_taken` |
| `GET /api/v1/users/{id}` | 200, the user, `ETag` | 404 `user_not_found` |
| `GET /api/v1/users?status=&role=&email=&page=&pageSize=` | 200, a page of users by address | 400 |
| `PATCH /api/v1/users/{id}` (needs `If-Match`) | 200 | 400; 404; 409 `concurrency_conflict`, `user_email_taken`; 428 |
| `PUT /api/v1/users/{id}/roles` (needs `If-Match`) | 200 | 400; 404; 409 `concurrency_conflict`; 422 `user_last_tenant_admin`; 428 |
| `POST /api/v1/users/{id}/disable` (needs `If-Match`; no body) | 200, the user `Disabled` | 404; 409 `concurrency_conflict`; 422 `user_cannot_disable_self`, `user_last_tenant_admin`, `user_already_disabled`; 428 |
| `POST /api/v1/users/{id}/enable` (needs `If-Match`; no body) | 200, the user `Active` | 404; 409 `concurrency_conflict`; 422 `user_already_active`; 428 |
| `POST /api/v1/users/{id}/password` (needs `If-Match`) | 200, the user | 400; 404; 409 `concurrency_conflict`; 428 |

The body of a creation (unknown members are refused):

| Field | Required | Rule |
|---|---|---|
| `email` | yes | One `@`, a domain with a dot, no white space; at most 256 characters; unique in the tenant without regard to case |
| `displayName` | yes | At most 200 characters |
| `password` | yes | 12 to 128 characters, any characters (no rules of composition) |
| `roles` | yes | At least one of `TenantAdmin`, `Supervisor`, `BillingClerk`, `Cashier`, `Viewer` |

`PATCH` takes `email` and `displayName`, both required. `PUT .../roles` takes `roles`, every role the user is to hold.
`password` takes `password`, which an administrator sets; nothing forces a change at the next sign-in.

A user: `id`, `email` (as entered, trimmed), `displayName`, `status` (`Active` or `Disabled`), `roles` (in the order
above), `lastSignInAtUtc` (null until the first sign-in), `createdAtUtc`, `updatedAtUtc`, `version`. A sign-in
writes `lastSignInAtUtc`, so it changes the version: an administrator who read the user before reads it again.

**Guards (ADR 0030).** The tenant always keeps an active TenantAdmin: the last one can neither lose the role nor be
disabled (422 `user_last_tenant_admin`), and nobody disables themselves (422 `user_cannot_disable_self`). A user is
never deleted; a disabled user cannot sign in (BR-601) and everything that names it keeps its meaning. Every column
that names a user is a foreign key to one.

The audit trail is read with `GET /api/v1/audit-records?entityType=User&entityId=...`: `Created`, `Updated`,
`RolesReplaced`, `Disabled`, `Enabled`, `PasswordReset`, `SignedIn`. The images hold every column but the password
hash.

### 8.12 Sign-in (US-02; BR-601, BR-602; ADR 0031)

The one endpoint that needs no token: it is how a client gets one. It takes no `Idempotency-Key` (its body holds a
password, and the key store keeps a fingerprint of the body), and no answer of it may be cached
(`Cache-Control: no-store`).

| Method and path | Success | Failures |
|---|---|---|
| `POST /api/v1/auth/login` | 200, the token | 400; 401 `invalid_credentials`; 429 `too_many_sign_ins` |

The body (unknown members are refused): `tenant`, the code of the hospital; `email`; `password`. The code and the
address are compared without regard to case or the spaces around them. The answer: `accessToken`, `tokenType`
(`Bearer`), `expiresAtUtc`, `expiresInSeconds` (3600).

**Every refusal is the same 401 `invalid_credentials`**, with the same message: an unknown or suspended hospital, an
unknown address, a wrong password, a disabled user (BR-601). So the answer never tells which hospitals or addresses
exist, and an unknown address costs as much time as a wrong password. Why a sign-in was refused is written to the log
(a warning with the hospital's code and, when the address was found, the user's id); the address and the password
never are. A missing or over-long value is a 400 `validation_failed` like any other. A sign-in is recorded on the user
(`lastSignInAtUtc`) and in the audit trail (`SignedIn`, by the user).

**Repeated attempts are limited** (ADR 0035), in two ways that answer the same 429 `too_many_sign_ins`, with
`Retry-After` in seconds and `Cache-Control: no-store`:

- **per client address:** 30 sign-ins a minute, however they end (`Retry-After: 60`);
- **per account**, the hospital's code and the address as typed: after 10 refused sign-ins in 15 minutes, every
  sign-in for that account is refused until the 15 minutes have passed, the right password included
  (`Retry-After: 900`). A sign-in that succeeds counts nothing. An address nobody has is held in the same way, so the
  answer still tells nothing about which accounts exist.

### 8.13 Settings (US-03; BR-205, BR-207, BR-607; ADR 0033)

The tenant's settings, a TenantAdmin's alone. A change applies from the next request, since every use case reads the
settings when it runs; a bill that already waits for approval keeps waiting.

| Method and path | Success | Failures |
|---|---|---|
| `GET /api/v1/settings` | 200, the settings, `ETag` (the tenant's version) | |
| `PATCH /api/v1/settings` (needs `If-Match`) | 200, the settings | 400; 409 `concurrency_conflict`; 428 |

The body of a `PATCH` is the new state of the three settings (section 5; unknown members are refused):
`postingApprovalThreshold`, an amount above zero, or left out or null for none (no bill needs approval, BR-207);
`maxBackdateDays`, required, 0 to 365 (BR-205); `defaultDueDays`, required, 0 to 365 (BR-205). The answer:
those three, `prefixes` (`bill`, `creditNote`, `payment`, `adjustment`, `refund`: the default prefix of each type) and
`version`. The prefixes change only through a prefix change (section 8.14). A change is audited on the tenant
(`entityType=Tenant`, the tenant's id: `SettingsChanged`), a trail only a TenantAdmin reads.

### 8.14 Prefix changes (BR-802, BR-604; ADR 0033)

A new prefix for one document type, asked for by a TenantAdmin and decided by a second person: a Supervisor or a
TenantAdmin who is not the maker (BR-603). The approval gives the type the new prefix as its default and on its series
of the current fiscal year (by today's date in India), so the next number carries it; numbers already issued keep
theirs, and the series of earlier years keep their prefix. Not financial: no `Idempotency-Key`; one change waits per
type, and `If-Match` guards the decisions. The requests are listed in the checkers' inbox (section 8.6) as
`PrefixChange`.

| Method and path | Who | Success | Failures |
|---|---|---|---|
| `POST /api/v1/settings/prefix-changes` | TenantAdmin | 201, the change (`Pending`), `Location`, `ETag` | 400; 409 `prefix_change_pending`, `prefix_in_use`; 422 `prefix_unchanged` |
| `GET /api/v1/settings/prefix-changes/{id}` | TenantAdmin, Supervisor | 200, the change, `ETag` | 404 `prefix_change_not_found` |
| `GET /api/v1/settings/prefix-changes?status=&documentType=&page=&pageSize=` | TenantAdmin, Supervisor | 200, a page, the newest first | 400 |
| `POST /api/v1/settings/prefix-changes/{id}/approve` (needs `If-Match`; optional body `comment`) | TenantAdmin, Supervisor | 200, the change `Approved` | 403 `approval_checker_is_maker`, `approval_checker_role`; 404; 409 `concurrency_conflict`, `prefix_in_use`; 422 `prefix_change_not_pending`; 428 |
| `POST /api/v1/settings/prefix-changes/{id}/reject` (needs `If-Match`; body `comment`, required) | TenantAdmin, Supervisor | 200, the change `Rejected` | 400; 403; 404; 409; 422; 428 |

The body of a request: `documentType` (`Bill`, `CreditNote`, `Payment`, `Adjustment`, `Refund`), `prefix` (1 to 3
letters or digits, trimmed and upper-cased: `inv` is `INV`), and an optional `comment` for the checker. A change:
`id`, `documentType`, `fromPrefix`, `toPrefix`, `status` (`Pending`, `Approved`, `Rejected`), `requestedAtUtc`,
`requestedByUserId`, `decidedAtUtc`, `version`.

**A prefix belongs to one kind of document for good** (409 `prefix_in_use`): a new prefix may not be the default of
another type, nor a prefix a series of another type ever had, nor one another type's change came from, went to, or
waits to go to; so two documents of different kinds never carry the same number. A type's own earlier prefix may come
back. This is asked when the change is requested and again when it is approved. A number taken in the very instant
of an approval waits for it, or the approval waits for the number: the approval holds the type's series of the
current fiscal year, whether it exists yet or not, until it commits, so that year's series always ends with the new
prefix, the first number of the year included (ADR 0033, "Amended 2026-11-23"). Such a wait lasts milliseconds and
does not change the answer.

The trails of a change (`entityType=PrefixChange`: `Requested`, `Approved`, `Rejected`), of the tenant
(`PrefixChanged`) and of the series (`entityType=NumberSeries`: `PrefixChanged`) are the TenantAdmin's alone.

### 8.15 Reports (BR-901, US-40, US-42; ADR 0036)

Read-only, for every role. Each figure ties to the ledger: the totals of the registers to the postings of their
documents, the balances to the accounts they cache (BR-404), the collection of a day to the Cash entries of that business
date. A report reads committed rows and takes no lock.

| Method and path | Answer | Query |
|---|---|---|
| `GET /api/v1/reports/bill-register` | page of posted bills, by bill date | `from`, `to`, `payerId`, `status` (`Posted`, `PartiallyPaid`, `Settled`) |
| `GET /api/v1/reports/payment-register` | page of payments, by payment date, reversed ones included | `from`, `to`, `payerId`, `method`, `status` (`Posted`, `PendingReversal`, `Reversed`), `order` (`OldestFirst` by default, `NewestFirst`) |
| `GET /api/v1/reports/daily-collection` | the collection of one day by method | `date` |
| `GET /api/v1/reports/outstanding-by-payer` | page of payers with a balance, the largest net outstanding first | `search` |
| `GET /api/v1/reports/payers/{payerId}/balance` | one payer's balances (US-40); 404 `payer_not_found` | |
| `GET /api/v1/reports/unapplied-cash` | page of payments that hold unapplied cash, the oldest first | `payerId` |
| `GET /api/v1/reports/available-credit` | page of posted credit notes that hold available credit, the oldest first | `payerId` |
| `GET /api/v1/reports/pending-approvals` | page of the requests that wait for a checker, the oldest first | `documentType` |
| `GET /api/v1/reports/reconciliation` | page of drift rows (US-41), by identity and reference | |

**Periods.** A register covers `from` to `to` (`yyyy-MM-dd`, business dates in India), both included: both given or
neither, the end not before the start, at most 366 days. Neither means the current month in India. A period it cannot
cover is a 400 `validation_failed` on `to`. `daily-collection` without a `date` is today's in India.

**A paged report** takes `page` and `pageSize` like any list (section 6) and answers
`{ period, items, page, pageSize, totalCount, totals }`: `totals` are those of every page together, not of the page;
`period` is the one a register covers, null for the other reports. Items of the registers are chronological, by date
and then by number, the oldest first; the payment register can be asked for the newest first (`order=NewestFirst`).
Money is a decimal with two places, as everywhere.

- **Bill register** items: `billId`, `number`, `billDate`, `dueDate`, `patientId`, `patientName`, `payerId`,
  `payerName`, `total`, `balanceDue`, `status`, `postedAtUtc`. Totals: `bills`, `total` (the debits to Receivable of
  their postings), `balanceDue`. A draft, a voided bill and one that waits for approval are in no register.
- **Payment register** items: `paymentId`, `number`, `paymentDate`, `payerId`, `payerName`, `method`, `reference`,
  `amount`, `unappliedAmount`, `status`, `reversedAtUtc`. Totals: `payments`, `amount` (the debits to Cash of their
  postings), `unappliedAmount`.
- **Daily collection** (US-42): `date`, `methods` (one row per method, all five, zeros included) and `totals`, each with
  `payments` (recorded that day), `received`, `reversed` (payments whose reversal was approved that day, under their
  method), `refunded` (refunds of that day, under the refund's method) and `net` (received less reversed less refunded).
  The totals' `net` is the movement of the Cash account on that date. A payment dated the day before is in that day's
  collection, whenever it was recorded.
- **Outstanding by payer** and **a payer's balance** (US-40): `payerId`, `payerName`, `receivable`, `unappliedCash`,
  `creditAvailable`, `netOutstanding` (the first less the other two). The list leaves out payers whose three balances
  are zero; the single balance answers zeros for such a payer. Totals: `payers` and the four sums.
- **Unapplied cash** items: `paymentId`, `number`, `paymentDate`, `payerId`, `payerName`, `method`, `amount`,
  `unappliedAmount`, `status` (a payment waiting for its reversal still holds its money). Totals: `payments`,
  `unappliedAmount` (the balance of UnappliedCash).
- **Available credit** items: `creditNoteId`, `number`, `creditNoteDate`, `billId`, `billNumber`, `payerId`,
  `payerName`, `total`, `availableAmount`. Totals: `creditNotes`, `availableAmount` (the balance of CreditAvailable).
- **Pending approvals** items: `approvalRequestId`, `documentType`, `action`, `documentId`, `makerUserId`,
  `requestedAtUtc`, `waitingDays` (in India, from the day of the request to today), and the document's `payerId`,
  `payerName` and `amount` where it has them (a prefix change has none). Totals: `requests`, `oldestRequestedAtUtc`.
  This report is everyone's; the checker's inbox (section 8.6) stays the checkers'.
- **Reconciliation** (US-41, BR-404; Prompt 6.2): every place where a cached balance differs from the ledger, or the
  ledger or a number series breaks one of its identities (`docs/database.md` sections 5.5 and 5.6). Items: `identity`
  (for example `BillBalance`, `PayerReceivable`, `UnbalancedPosting`, `NumberSeriesGap`), `subjectId` (the bill,
  payment, credit note, payer, posting or series), `reference` (its number, or `Payment 2026-27` for a series), `cached`
  and `ledger` (the two figures that disagree; for a series, the documents numbered and the numbers given). Totals:
  `checkedAtUtc`, `rows`, and `byIdentity` (`identity`, `rows`). An empty report means none: the caches equal the
  ledger and every series is gapless. A drift row is a defect of the highest severity: the report also writes it to the
  log as an error, and never corrects anything (`docs/database.md` section 5.6 says what to do).

### 8.16 Dashboard (US-44, BR-902; ADR 0037)

Read-only, for a TenantAdmin, a Supervisor or a Viewer; a BillingClerk or a Cashier gets 403 `forbidden` (they keep
every report of section 8.15). One endpoint per panel, so that each loads, and fails, on its own. Every figure is
derived from the ledger by business date, or comes from one of the reports of section 8.15, never from a copy of their
queries. Amounts are exact decimals with two places; nothing is rounded to lakh or crore on the server.

| Method and path | Answer | Query |
|---|---|---|
| `GET /api/v1/dashboard/summary` | the six cards, each against the previous period | `from`, `to` |
| `GET /api/v1/dashboard/trend` | billed and collected per day or per week, cumulative, and the daily figures of the sparklines | `from`, `to`, `interval` (`Day`, `Week`) |
| `GET /api/v1/dashboard/ageing` | the balance due of posted bills by days past due, as of today in India | |
| `GET /api/v1/dashboard/top-payers` | the first 5 rows of the outstanding-by-payer report, with its totals | |
| `GET /api/v1/dashboard/recent-payments` | the 5 newest payments of the period, from the payment register, with its totals | `from`, `to` |
| `GET /api/v1/dashboard/pending-approvals` | the 3 oldest rows of the pending-approvals report, with its totals | |
| `GET /api/v1/dashboard/drift` | the first 3 rows of the reconciliation report, with its totals | |

**The period** is `from` to `to`, business dates in India, both included: both given or neither, the end not before the
start, at most 366 days, and **ending today in India at the latest**. Neither means the last 30 days ending today. A
period it cannot cover is a 400 `validation_failed` on `to`. **The previous period** is the one of the same length that
ends the day before the period starts (for 1 to 30 October, 1 to 30 September; for the last 30 days, the 30 before).

- **Summary:** `period`, `previousPeriod`, and six cards, `totalBilled`, `amountCollected`, `outstandingDues`,
  `unappliedCash`, `pendingApprovals` and `reconciliationDrift`, each `{ value, previous, changePercent }`.
  `changePercent` is (value − previous) / previous × 100, two places, half away from zero, and null when `previous` is
  zero.
  - *Billed*: the debits to Receivable of the bills' postings dated in the period (the bills posted with a bill date in
    it).
  - *Collected*: the debits to Cash of the payments' postings dated in the period, **gross**: a later reversal or refund
    does not lower it (the daily collection of section 8.15 shows them).
  - *Outstanding dues* and *unapplied cash*: the balances of the Receivable and UnappliedCash accounts at the end of the
    period's last day, from the ledger entries dated up to it.
  - *Pending approvals*: the requests asked for on or before the period's last day (in India) and not decided by its
    end.
  - *Reconciliation drift*: the rows of the reconciliation now; `previous` and `changePercent` are always null, since
    drift is not kept by day.
- **Trend:** `period`, `interval`, `points` and `daily`. Without `interval`, a period of up to 31 days is answered per
  day and a longer one per week. A point is `{ from, to, billed, collected, cumulativeBilled, cumulativeCollected }`; a
  week runs Monday to Sunday, and the first and last weeks are cut by the period. `daily` has one row for every date of
  the period, zeros included: `{ date, billed, collected, outstandingDues, unappliedCash, pendingApprovals }`, the last
  three at the end of that day.
- **Ageing:** `asOf` (today in India), `buckets`, `bills` and `total`. Always the four buckets `0-30` (a bill not yet
  due, or due today, included), `31-60`, `61-90` and `90+`, by the days from the bill's due date to today, each with
  `bills`, `amount` (their balance due) and `share` (percent of `total`, two places; the four may add up to 99.99 or
  100.01). `total` is the outstanding dues of today. A draft, a voided bill, one that waits for approval and a settled
  one are in no bucket.
- **Top payers, recent payments, pending approvals, drift:** the answer of the report each comes from (section 8.15),
  page 1 at the size above, with the totals of every page, so that a panel can say "5 of 23".

## 9. Error-code catalogue

Every `errorCode` the API can answer, with its status and what the client is told (the `detail`). Section 3 gives the
rules; this is the whole list. A code is stable: a client branches on it, never on the message. An architecture test
(`ApiDocumentationTests`) fails when a code is missing here.

### 9.1 Codes the API makes itself

For a failure no use case reported: routing, model binding, content negotiation, transport, rate limits and faults
(`ErrorCodes`). A status this table does not name answers `http_<status>`.

| Code | Status | When |
|---|---|---|
| `validation_failed` | 400 | The body or the query is invalid; `errors` lists the messages per field |
| `bad_request` | 400 | The request cannot be read, and no field is to blame |
| `host_not_allowed` | 400 | The request names a host the API does not answer for (section 1) |
| `https_required` | 400 | Outside Development, the request did not arrive over HTTPS (section 1) |
| `unauthorized` | 401 | No token, or one that is not valid (section 2) |
| `forbidden` | 403 | The user's roles do not allow the endpoint (section 2) |
| `not_found` | 404 | No route has this path |
| `method_not_allowed` | 405 | The path exists, not with this method |
| `not_acceptable` | 406 | The `Accept` header rules out JSON |
| `request_timeout` | 408 | The server stopped waiting for the request's body |
| `conflict` | 409 | A conflict no use case named |
| `payload_too_large` | 413 | The body is larger than the server reads |
| `unsupported_media_type` | 415 | The body is not JSON |
| `unprocessable_entity` | 422 | A business rule refused the request, and no use case named it |
| `precondition_required` | 428 | A change of an existing record without `If-Match` (section 4) |
| `too_many_requests` | 429 | A rate limit other than the sign-in's (none exists yet) |
| `too_many_sign_ins` | 429 | Too many sign-ins from one address, or refused for one account; see `Retry-After` (section 8.12) |
| `internal_error` | 500 | A fault; nothing internal is told outside Development |
| `service_unavailable` | 503 | The service cannot answer now |

### 9.2 Common: concurrency, idempotency, approvals

| Code | Status | Message |
|---|---|---|
| `validation_failed` | 400 | The request is not valid. The errors are listed per field. |
| `concurrency_conflict` | 409 | The record was changed by someone else after you read it. Read it again, then retry (BR-804). |
| `duplicate_key` | 409 | The record conflicts with one that already exists. |
| `idempotency_key_missing` | 400 | This request needs an Idempotency-Key header: one UUID that you generate for the operation and send again unchanged when you retry it (BR-803). |
| `idempotency_in_progress` | 409 | A request with this Idempotency-Key is still being processed. Wait, then send the same request with the same key again (BR-803). |
| `idempotency_key_reuse` | 422 | This Idempotency-Key was already used for a different request. A key belongs to one operation: generate a new key for a new one, also when you try again after a refusal (BR-803). |
| `approval_checker_is_maker` | 403 | You asked for this yourself, so you cannot decide it: a second person has to approve or reject it (BR-603). |
| `approval_already_decided` | 409 | This request has already been decided. Read the document again. |
| `approval_needs_user` | 403 | This action needs a signed-in user: an approval is asked for and given by people (BR-603). |
| `approval_checker_role` | 403 | Only a Supervisor or a TenantAdmin may approve or reject (BR-603). |

### 9.3 Identity: tenants, users, sign-in, settings, prefixes

| Code | Status | Message |
|---|---|---|
| `invalid_credentials` | 401 | The hospital code, the e-mail address or the password is not right, or the user may not sign in. |
| `tenant_invalid` | 400 | The tenant is not valid. |
| `user_not_found` | 404 | The user does not exist. |
| `user_email_taken` | 409 | Another user of this hospital already has this e-mail address (BR-601). |
| `user_already_disabled` | 422 | The user is already disabled. |
| `user_already_active` | 422 | The user is already active. |
| `user_last_tenant_admin` | 422 | This is the last active tenant administrator; make another user a TenantAdmin first. |
| `user_cannot_disable_self` | 422 | You cannot disable your own user; another administrator can. |
| `prefix_change_not_found` | 404 | The prefix change does not exist. |
| `prefix_change_not_pending` | 422 | The prefix change has been decided already; a decision is final. |
| `prefix_change_pending` | 409 | A change of the prefix of this document type already waits for a decision. |
| `prefix_unchanged` | 422 | The document type has this prefix already. |
| `prefix_in_use` | 409 | This prefix belongs to another kind of document, now or in the past; choose another one (BR-802). |

### 9.4 Payers: patients and payers

| Code | Status | Message |
|---|---|---|
| `patient_not_found` | 404 | The patient does not exist. |
| `patient_mrn_duplicate` | 409 | A patient with this MRN is already registered (BR-101). |
| `patient_inactive` | 422 | The patient is inactive. Reactivate the patient first (BR-102, BR-104). |
| `patient_already_linked` | 409 | The patient already has a self-pay payer (BR-104). |
| `patient_already_inactive` | 422 | The patient is already inactive. |
| `patient_already_active` | 422 | The patient is already active. |
| `payer_not_found` | 404 | The payer does not exist. |
| `payer_inactive` | 422 | The payer is inactive. Reactivate the payer first (BR-105). |
| `payer_kind_not_supported` | 422 | Only self-pay payers exist in this release. Insurers and corporate accounts arrive in R4 (BR-103). |
| `payer_already_inactive` | 422 | The payer is already inactive. |
| `payer_already_active` | 422 | The payer is already active. |

### 9.5 Billing: service items, bills, credit notes

| Code | Status | Message |
|---|---|---|
| `service_item_not_found` | 404 | The service item does not exist. |
| `service_item_code_duplicate` | 409 | A service item with this code is already in the catalogue. |
| `service_item_already_inactive` | 422 | The service item is already inactive. |
| `service_item_already_active` | 422 | The service item is already active. |
| `service_item_inactive` | 422 | The service item is inactive, so it cannot be put on a bill line. Reactivate it, or enter the line as free text. |
| `bill_not_found` | 404 | The bill does not exist. |
| `bill_not_draft` | 422 | Only a draft bill can be edited, voided or posted (BR-204). |
| `bill_date_in_future` | 422 | The bill date cannot be in the future (BR-205). |
| `bill_date_too_old` | 422 | The bill date cannot be earlier than …, the earliest date this hospital allows (BR-205). |
| `bill_due_date_before_bill_date` | 422 | The due date cannot be before the bill date (BR-205). |
| `bill_line_discount_exceeds_amount` | 422 | The discount of line … is more than the amount of the line (BR-201). |
| `bill_line_amount_too_large` | 422 | The amount of line … is larger than a bill line can hold. |
| `bill_has_no_lines` | 422 | A bill needs at least one line to be posted. |
| `bill_already_posted` | 409 | The bill is posted already and can no longer be changed (BR-006). Correct it with a credit note or an adjustment. |
| `bill_pending_approval` | 409 | The bill is waiting for approval and cannot be changed until it is approved or rejected (BR-207). |
| `bill_not_pending_approval` | 422 | The bill is not waiting for approval, so there is nothing to approve or reject. |
| `bill_not_posted` | 422 | The bill is not posted, so nothing is owed on it and nothing can be applied to it (BR-303). |
| `allocation_exceeds_balance_due` | 422 | Bill … has a balance due of …; more than that cannot be applied to it (BR-303). |
| `allocation_payer_mismatch` | 422 | The bill is owed by a different payer than the payment came from (BR-303). |
| `credit_note_not_found` | 404 | The credit note does not exist. |
| `credit_note_not_draft` | 422 | Only a draft credit note can be edited, voided or submitted. |
| `credit_note_pending_approval` | 409 | The credit note is waiting for approval and cannot be changed until it is approved or rejected (BR-212). |
| `credit_note_not_pending_approval` | 422 | The credit note is not waiting for approval, so there is nothing to approve or reject. |
| `credit_note_already_posted` | 409 | The credit note is posted already and can no longer be changed (BR-006). |
| `credit_note_not_posted` | 422 | The credit note is not posted, so it has no credit to apply (BR-213). |
| `credit_note_has_no_lines` | 422 | A credit note needs at least one line to be submitted. |
| `credit_note_total_zero` | 422 | A credit note needs a total above zero to be submitted. |
| `credit_note_date_in_future` | 422 | The credit note date cannot be in the future (BR-205). |
| `credit_note_date_too_old` | 422 | The credit note date cannot be earlier than …, the earliest date this hospital allows (BR-205). |
| `credit_note_date_before_bill_date` | 422 | The credit note date cannot be before the bill date, … (BR-214). |
| `credit_note_line_discount_exceeds_amount` | 422 | The discount of line … is more than the amount of the line (BR-211). |
| `credit_note_line_amount_too_large` | 422 | The amount of line … is larger than a credit note line can hold. |
| `credit_note_bill_line_not_found` | 422 | Line … names a line the bill does not have (BR-211). |
| `credit_note_exceeds_bill_total` | 422 | The bill comes to … and … of it is credited already; this credit note would take the credits above the bill (BR-210). |
| `credit_note_line_exceeds_bill_line` | 422 | Line … credits a bill line of …, of which … is credited already; that is more than the line (BR-211). |
| `credit_exceeds_available` | 422 | Credit note … has … available; more than that cannot be applied (BR-213). |
| `credit_application_not_found` | 404 | The credit note has no such application. |
| `credit_application_made_at_posting` | 422 | This credit was applied to its own bill when the credit note was posted, and that application cannot be reversed (BR-212). |
| `credit_application_pending_reversal` | 409 | The application is waiting for a decision on its reversal already. |
| `credit_application_reversed` | 409 | The application was reversed already. |
| `credit_application_not_pending_reversal` | 422 | The application is not waiting for a reversal, so there is nothing to approve or reject. |

### 9.6 Payments: payments, allocations, reversals, refunds

| Code | Status | Message |
|---|---|---|
| `payment_not_found` | 404 | The payment does not exist. |
| `payment_date_in_future` | 422 | The payment date cannot be in the future (BR-205). |
| `payment_date_too_old` | 422 | The payment date cannot be earlier than …, the earliest date this hospital allows (BR-205). |
| `allocation_exceeds_unapplied` | 422 | Payment … has … unapplied; more than that cannot be allocated (BR-303). |
| `payment_pending_reversal` | 409 | The payment is waiting for a decision on its reversal and cannot be used until then (BR-306). |
| `payment_reversed` | 409 | The payment was reversed and can no longer be used (BR-306). |
| `payment_not_pending_reversal` | 422 | The payment is not waiting for a reversal, so there is nothing to approve or reject. |
| `allocation_not_found` | 404 | The payment has no such allocation. |
| `allocation_pending_reversal` | 409 | An allocation of the payment is waiting for a decision on its reversal; decide that first (BR-305). |
| `allocation_reversed` | 409 | The allocation was reversed already (BR-305). |
| `allocation_not_pending_reversal` | 422 | The allocation is not waiting for a reversal, so there is nothing to approve or reject. |
| `payment_refunded` | 422 | A refund was posted from the payment, so it cannot be reversed (BR-306). Correct it with a new payment. |
| `refund_not_found` | 404 | The refund does not exist. |
| `refund_not_draft` | 422 | Only a draft refund can be corrected, voided or submitted. |
| `refund_pending_approval` | 409 | The refund is waiting for approval and cannot be changed until it is approved or rejected (BR-604). |
| `refund_not_pending_approval` | 422 | The refund is not waiting for approval, so there is nothing to approve or reject. |
| `refund_already_posted` | 409 | The refund is posted already and can no longer be changed (BR-006, BR-308). |
| `refund_date_in_future` | 422 | The refund date cannot be in the future (BR-205). |
| `refund_date_too_old` | 422 | The refund date cannot be earlier than …, the earliest date this hospital allows (BR-205). |
| `refund_date_before_source_date` | 422 | The refund date cannot be before the date of the document it comes from, … (BR-307). |
| `refund_exceeds_available` | 422 | … has … left to refund; a refund cannot be for more (BR-307). |

### 9.7 Adjustments

| Code | Status | Message |
|---|---|---|
| `adjustment_not_found` | 404 | The adjustment does not exist. |
| `adjustment_not_draft` | 422 | Only a draft adjustment can be corrected, voided or submitted. |
| `adjustment_pending_approval` | 409 | The adjustment is waiting for approval and cannot be changed until it is approved or rejected (BR-502). |
| `adjustment_not_pending_approval` | 422 | The adjustment is not waiting for approval, so there is nothing to approve or reject. |
| `adjustment_already_posted` | 409 | The adjustment is posted already and can no longer be changed (BR-006). |
| `adjustment_date_in_future` | 422 | The adjustment date cannot be in the future (BR-205). |
| `adjustment_date_too_old` | 422 | The adjustment date cannot be earlier than …, the earliest date this hospital allows (BR-205). |
| `adjustment_date_before_bill_date` | 422 | The adjustment date cannot be before the bill date, … (BR-501). |
| `adjustment_rounding_too_large` | 422 | A rounding adjustment is less than 1.00; use a discount or a write-off for more (BR-501). |
| `adjustment_exceeds_balance_due` | 422 | Bill … has a balance due of …; an adjustment cannot be for more (BR-501). |

### 9.8 Receivables: the ledger

| Code | Status | Message |
|---|---|---|
| `ledger_balance_negative` | 422 | The posting would take a balance below zero (BR-405). Read the document again and retry. |

`ledger_balance_negative` is the ledger's own refusal under its lock (BR-405). No endpoint answers it as it is: every use
case whose posting can lower a balance answers it as 409 `concurrency_conflict`, since a request that committed first
changed what this one read; the others only raise balances and cannot meet it. `tenant_invalid` refuses a tenant row
that onboarding would make wrongly; no endpoint of R1 creates a tenant, so it is listed for completeness.
