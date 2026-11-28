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
- **`Server-Timing`, for a load test only** (ADR 0046). When the deployment sets `Diagnostics:ServerTiming=true`, the
  answer to a request that took a document number carries `Server-Timing: number-wait;dur=1.25, number-hold;dur=6.8`
  (milliseconds): how long the request waited for the number series of its document, and how long it then held it,
  until its transaction ended (`docs/requirements.md` section 5). A request that took no number has none. Off by
  default; never on in a deployment (`docs/security.md` section 10).

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

| Endpoints | TenantAdmin | Supervisor | BillingClerk | Cashier | Viewer | Accountant |
|---|---|---|---|---|---|---|
| Users (section 8.11), settings (8.13), asking for a prefix change (8.14) | ✓ | | | | | |
| Reading the prefix changes (8.14) | ✓ | ✓ | | | | |
| Patients, payers, service items: read / change | ✓ / ✓ | ✓ / ✓ | ✓ / ✓ | ✓ / | ✓ / | ✓ / |
| Bills: read / draft, edit, void, post | ✓ / ✓ | ✓ / ✓ | ✓ / ✓ | ✓ / | ✓ / | ✓ / |
| Credit notes: read / draft, edit, void, submit, apply | ✓ / ✓ | ✓ / ✓ | ✓ / ✓ | ✓ / | ✓ / | ✓ / |
| Adjustments: read / draft, edit, void, submit | ✓ / ✓ | ✓ / ✓ | ✓ / ✓ | | ✓ / | ✓ / |
| Payments and allocations: read / record, allocate | ✓ / ✓ | ✓ / ✓ | ✓ / ✓ | ✓ / ✓ | ✓ / | ✓ / |
| Refunds: read / draft, edit, void, submit | ✓ / ✓ | ✓ / ✓ | | ✓ / ✓ | ✓ / | ✓ / |
| Ask for a reversal (payment, allocation, application of credit) | ✓ | ✓ | ✓ | ✓ | | |
| Approve, reject, and the inbox `GET /approval-requests` | ✓ | ✓ | | | | |
| Audit trail (the trails of a user, the tenant, its prefix changes and its number series: TenantAdmin only) | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Reports (section 8.15) | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Dashboard (section 8.16) | ✓ | ✓ | | | ✓ | ✓ |
| Settlement channels (section 8.22): read / set up | ✓ / ✓ | ✓ / | | | ✓ / | ✓ / |
| Statements (section 8.22): read / dry run and import | ✓ / ✓ | ✓ / ✓ | | | ✓ / | ✓ / ✓ |
| `GET /api/v1/system/info` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| `GET /api/v1/system/business-date` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |

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
| 401 | A token of a session that has ended: a sign-out, a changed or reset password, changed roles, a disabled user (section 8.12) | `session_ended` |
| 403 | Any endpoint but the change of one's own password, while a password change is due (section 8.12) | `password_change_required` |
| 403 | Any endpoint that needs a user, when the user's hospital is suspended by the operator, also with a token issued before (ADR 0052) | `tenant_suspended` |
| 403 | The user's roles do not allow the endpoint (section 2), or a trail that is the TenantAdmin's (section 8.5) for anyone else | `forbidden` |
| 403 | A decision by the maker, or by a user who is neither Supervisor nor TenantAdmin (BR-603). Since 2026-11-26 (review finding R9) also the approval of a bill, a credit note, an adjustment or a refund by the user who created it or changed it last, though another user asked: the same code, with a message that says so. Such a user may reject. | `approval_checker_is_maker`, `approval_checker_role` |
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
- `search` finds a part of the text, as `LIKE '%text%'`, without regard to case. A `%`, `_` or `[` in it matches itself.
  The API accepts a search of one character; the screens send one from two characters, while it is typed (ADR 0018 and
  ADR 0039, amended 2026-11-25).
- **The search of a list of documents** (Prompt 11.1; ADR 0036, amended 2026-11-26): `search` on the lists of bills,
  payments, credit notes, adjustments and refunds is one box for four things. A document is found when the text is a
  part of its number (for a bill or a payment brought forward, of its old number too), a part of its patient's name or
  MRN, a part of its payer's name, or, when the text reads as an amount (`590`, `590.00`, `1,250.50`), exactly its
  amount (the total of a bill or a credit note, the amount of the others). A bill and a credit note have their patient;
  a payment, an adjustment and a refund are found by the patient of their payer. The search combines with every other
  filter, and `totalCount` is of what it finds. At most 100 characters (400 beyond that); **a text of fewer than two
  characters filters nothing**, where the lists of patients, payers and items search from one: one character is in
  almost every number. The order of the list is unchanged.
- **Filters and orders of Prompt 12.2** (ADR 0063). The lists below take, beside their earlier filters, the ones in the
  table, and `sort`: one of the list's named columns, ascending, or with `-` in front the largest or latest first
  (`sort=total`, `sort=-total`), whatever its case. A column that is not named, or anything else, is **400
  `sort_not_supported`**, whose message lists the columns. Every order ends on a key that is one of a kind (the
  document's id, or for a number its fiscal year and sequence and then its id), so that pages never repeat or skip a
  row. With no `sort`, a list keeps its own order as before.
- **An amount** is a number of rupees from 0 with at most two decimals (`1500`, `1500.50`), compared with the column
  as `>=` for `...From` and `<=` for `...To`; **a date** is `yyyy-MM-dd`, a business date in India, both ends included;
  **`createdBy`** and **`requestedBy`** are the id of a user; **`origin`** is `Standard` or `Opening` (brought in at the
  cut-over, ADR 0053). One that cannot be read, or a range whose end is before its start, is **400
  `filter_invalid`**, with each such parameter named in `errors`. **A filter left empty filters nothing.** Every filter
  combines with the others, with `search`, with the order and with the paging; `totalCount`, and the totals of a
  report, are of the rows they leave.

| List | New filters | `sort` |
|---|---|---|
| `GET /api/v1/bills` | `totalFrom`, `totalTo`, `balanceDueFrom`, `balanceDueTo`, `createdBy`, `origin` | `number`, `billDate`, `total`, `balanceDue` |
| `GET /api/v1/payments` | `amountFrom`, `amountTo`, `createdBy`, `origin` | `number`, `paymentDate`, `amount`, `unappliedAmount` |
| `GET /api/v1/credit-notes` | `creditNoteDateFrom`, `creditNoteDateTo`, `totalFrom`, `totalTo`, `createdBy` | `number`, `creditNoteDate`, `total`, `availableAmount` |
| `GET /api/v1/adjustments` | `adjustmentDateFrom`, `adjustmentDateTo`, `amountFrom`, `amountTo`, `createdBy` | `number`, `adjustmentDate`, `amount` |
| `GET /api/v1/refunds` | `refundDateFrom`, `refundDateTo`, `method`, `amountFrom`, `amountTo`, `createdBy` | `number`, `refundDate`, `amount` |
| `GET /api/v1/patients` | | `mrn`, `fullName`, `dateOfBirth` |
| `GET /api/v1/payers` | `kind` | `name`, `kind` |
| `GET /api/v1/service-items` | `category` (the whole category, as text) | `code`, `name`, `defaultUnitPrice` |
| `GET /api/v1/users` | | `displayName`, `email`, `lastSignInAtUtc` |
| `GET /api/v1/cashier-shifts` | `dateFrom`, `dateTo` | `businessDate`, `difference` |
| `GET /api/v1/approval-requests` | `requestedBy` | `requestedAtUtc` |
| `GET /api/v1/reports/bill-register` | `totalFrom`, `totalTo` | `number`, `billDate`, `total`, `balanceDue` |
| `GET /api/v1/reports/payment-register` | `amountFrom`, `amountTo` | `number`, `paymentDate`, `amount`, `unappliedAmount` (`order` stays for the dashboard and applies when no `sort` is given) |
| `GET /api/v1/reports/outstanding-by-payer` | | `name`, `receivable`, `netOutstanding` |
| `GET /api/v1/reports/unapplied-cash` | `dateFrom`, `dateTo` | `number`, `paymentDate`, `unappliedAmount` |
| `GET /api/v1/reports/available-credit` | `dateFrom`, `dateTo` | `number`, `creditNoteDate`, `availableAmount` |
| `GET /api/v1/reports/pending-approvals` | | `requestedAtUtc` |

  The cost was measured on 100,000 bills of one hospital on LocalDB 2019: every filter and order of the bills answers
  within 200 ms, most within 25 ms (ADR 0063). Migration `0034_ListIndexes` adds the indexes the new orders need.
- **`GET /api/v1/system/business-date`** (anyone signed in) answers `{ "today": "2026-11-27" }`: today in India by the
  server's clock, never cached. The screens count the shortcuts of a date filter (today, this week, this month, the
  last 30 days) from it, never from the browser's clock.
- **What the screens send** (Prompt 12.2b): the lists keep their filters, `sort` and `page` in the address and send
  them as they are; an amount after the pause of typing, and nothing for one that cannot be read. The screens never
  filter or sort the page they receive.

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

**Search** (`search`): finds a patient by a part of the MRN or a part of the name. **Status** (`status`): `Active` or
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
| `GET /api/v1/bills?search=&patientId=&payerId=&status=&billDateFrom=&billDateTo=&page=&pageSize=` | 200, a page of bill summaries, newest bill date first | 400 |
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
| `GET /api/v1/audit-records?entityType=&entityId=&page=&pageSize=` | 200, a page of records, oldest first | 400 when `entityType` or `entityId` is missing, or the page is outside the limits; 403 for the trail of a kind of record the caller's role may not read, and for a kind that is not audited |

`entityType` is `Patient`, `Payer`, `ServiceItem`, `Bill`, `ApprovalRequest` or the type of any later record (`User`,
`Payment`, ...); `entityId` is the id of that record. Both are required. A record of another tenant, like an id nobody
has, has an empty trail: 200 with no items, never 404. **Since 2026-11-26 (review finding R7; ADR 0032, amended) a
trail is read by whoever may read its record, and by no one else:** a trail holds the record before and after each
change, whole, so it is the record. In any case of the letters: `Patient`, `Payer` and `ServiceItem` by who reads the
reference data (every role); `Bill`, `CreditNote`, `Adjustment`, `Payment` and `Refund` by who reads that document (a
Cashier reads no adjustment's trail, a BillingClerk no refund's); `ApprovalRequest` and `OpeningBatch` by who may decide
(TenantAdmin, Supervisor), so not by the maker of a request; `User`, `Tenant`, `PrefixChange`, `NumberSeries` and
`PrintSettings` by a TenantAdmin alone, as the users and the settings are (ADR 0032, ADR 0033). Anything else is 403: a
kind that is not audited has no reader. Until that day every role read every trail but the four of the TenantAdmin.

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
| `Bill` | `Drafted`, `HeaderUpdated`, `LinesReplaced`, `Voided`, `SubmittedForApproval`, `ReturnedToDraft`, `Posted`, `BroughtForward` (an opening receivable, ADR 0053), `CreditApplied` (a payment, credit or adjustment lowered what is owed), `CreditReversed` (one of those was taken back) |
| `CreditNote` | `Drafted`, `HeaderUpdated`, `LinesReplaced`, `Voided`, `SubmittedForApproval`, `ReturnedToDraft`, `Posted`, `Applied`, `ApplicationReversalRequested`, `ApplicationReversalRejected`, `ApplicationReversed`, `Refunded` |
| `Payment` | `Recorded`, `BroughtForward` (an opening advance, ADR 0053), `Allocated`, `AllocationReversalRequested`, `AllocationReversalRejected`, `AllocationReversed`, `ReversalRequested`, `ReversalRejected`, `Reversed`, `Refunded` |
| `Adjustment` | `Drafted`, `Updated`, `Voided`, `SubmittedForApproval`, `ReturnedToDraft`, `Posted` |
| `Refund` | `Drafted`, `Updated`, `Voided`, `SubmittedForApproval`, `ReturnedToDraft`, `Posted` |
| `ApprovalRequest` | `Opened`, `Approved`, `Rejected` |
| `User` | `Created`, `Updated`, `RolesReplaced`, `Disabled`, `Enabled`, `PasswordReset`, `PasswordChanged`, `SignedIn`, `SignedOut` |
| `Tenant` | `Onboarded`, `SettingsChanged`, `LetterheadChanged` (ADR 0054), `LetterheadImageChanged` (ADR 0059), `PrefixChanged`, `Suspended`, `Resumed` (the last three of the operator's command, ADR 0052, with no user) |
| `PrefixChange` | `Requested`, `Approved`, `Rejected` |
| `PrintSettings` | `Changed` (ADR 0059), for the first save of a kind of document and for every later one |
| `OpeningBatch` | `Submitted`, `Approved`, `Rejected` (ADR 0053; the images never hold the files) |
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

`status` is `Pending` (the default), `Approved` or `Rejected`; `documentType` is `Bill`, `CreditNote`, `Adjustment`,
`Refund`, `Payment`, `PaymentAllocation`, `CreditNoteAllocation` or `PrefixChange`; `action` is `Post`, `Reverse` or
`ChangePrefix`. A request: `id`, `documentType`, `documentId`, `action`, `status`, `makerUserId`, `makerComment`, `checkerUserId`,
`checkerComment`, `requestedAtUtc`, `decidedAtUtc`. The last three of the checker are null while it is pending.

### 8.7 Payments (US-30; BR-205, BR-301, BR-302, BR-309)

Money received from a payer. Recording a payment posts it: the gapless number, the ledger entry
(Dr Cash / Cr UnappliedCash), the payer's unapplied cash and the audit record are written by one transaction or
not at all (BR-007). A payment is never edited or deleted (BR-006). Allocating it to bills is an action of its own
(below) and the one request here that takes `If-Match`; reversals are below, and refunds are documents of their own
(section 8.10).

| Method and path | Success | Failures |
|---|---|---|
| `POST /api/v1/payments` (needs `Idempotency-Key`) | 201, the payment, `Location`, `ETag` | 400; 404 `payer_not_found`; 409 `idempotency_in_progress`; 422 `payer_inactive`, `payment_date_in_future`, `payment_date_too_old`, `payment_reference_already_recorded`, `shift_not_open`, `shift_date_mismatch` (a cash payment, section 8.20), `idempotency_key_reuse` |
| `GET /api/v1/payments/{id}` | 200, the payment, `ETag` | 404 `payment_not_found` |
| `POST /api/v1/payments/{id}/allocations` (needs `Idempotency-Key` and `If-Match`) | 200, the payment with its allocations, `ETag` | 400; 404 `payment_not_found`, `bill_not_found`; 409 `concurrency_conflict`, `idempotency_in_progress`; 422 `allocation_exceeds_unapplied`, `allocation_exceeds_balance_due`, `allocation_payer_mismatch`, `bill_not_posted`, `idempotency_key_reuse`; 428 |
| `GET /api/v1/payments/{id}/allocations` | 200, the payment with its allocations, `ETag` | 404 `payment_not_found` |
| `GET /api/v1/payments?search=&payerId=&method=&status=&paymentDateFrom=&paymentDateTo=&page=&pageSize=` | 200, a page of payments | 400 |

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
| `GET /api/v1/credit-notes?search=&billId=&payerId=&status=&page=&pageSize=` | 200, a page of credit notes without lines | 400 |
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
| `GET /api/v1/adjustments?search=&billId=&payerId=&status=&kind=&page=&pageSize=` | 200, a page of adjustments | 400 |
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
| `GET /api/v1/refunds?search=&payerId=&status=&paymentId=&creditNoteId=&page=&pageSize=` | 200, a page of refunds | 400 |
| `PUT /api/v1/refunds/{id}` (needs `If-Match`) | 200 | 400; 404; 409 `concurrency_conflict`, `refund_pending_approval`, `refund_already_posted` and the source codes of `POST`; 422 `refund_not_draft` and the date codes; 428 |
| `POST /api/v1/refunds/{id}/void` (needs `If-Match`, `Idempotency-Key`) | 200 | 400; 404; 409 and 422 as `PUT`; 428 |
| `POST /api/v1/refunds/{id}/post` (needs `If-Match`, `Idempotency-Key`; no body) | **202**, the refund in `PendingApproval` | 403 `approval_needs_user`; 404; 409 as `PUT`; 422 `refund_not_draft`, `refund_exceeds_available`, `credit_note_not_posted` and the date codes; 428 |
| `POST /api/v1/refunds/{id}/approve` (needs `If-Match`, `Idempotency-Key`; body optional) | 200, the posted refund | 403 `approval_checker_is_maker`, `approval_needs_user`; 404; 409 `concurrency_conflict`, `refund_already_posted`, `payment_pending_reversal`, `payment_reversed`; 422 `refund_not_pending_approval`, `refund_not_draft`, `refund_exceeds_available`, `payer_inactive`, `shift_not_open` and `shift_date_mismatch` (a cash refund, section 8.20) and the date codes; 428 |
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
| `GET /api/v1/users?status=&role=&email=&page=&pageSize=` | 200, a page of users by address; `email` finds a part of the address, without regard to case, a `%` or `_` in it matching itself | 400 |
| `PATCH /api/v1/users/{id}` (needs `If-Match`) | 200 | 400; 404; 409 `concurrency_conflict`, `user_email_taken`; 428 |
| `PUT /api/v1/users/{id}/roles` (needs `If-Match`) | 200 | 400; 404; 409 `concurrency_conflict`; 422 `user_last_tenant_admin`; 428 |
| `POST /api/v1/users/{id}/disable` (needs `If-Match`; no body) | 200, the user `Disabled` | 404; 409 `concurrency_conflict`; 422 `user_cannot_disable_self`, `user_last_tenant_admin`, `user_already_disabled`; 428 |
| `POST /api/v1/users/{id}/enable` (needs `If-Match`; no body) | 200, the user `Active` | 404; 409 `concurrency_conflict`; 422 `user_already_active`; 428 |
| `POST /api/v1/users/{id}/password` (needs `If-Match`) | 200, the user | 400; 404; 409 `concurrency_conflict`; 428 |
| `POST /api/v1/users/{id}/second-step/reset` (needs `If-Match`; no body) | 200, the user, without a second step and with `approvalHoldUntilUtc` | 404; 409 `concurrency_conflict`; 428 |

A user answers `secondStepEnrolled` and `approvalHoldUntilUtc` too (ADR 0061). A reset of the password and a reset of
the second step both set the hold: for 24 hours that user approves and rejects nothing (section 8.12). The reset of a
second step is for a lost phone: the user's sessions end and they enrol again at the next sign-in. Audited as
`SecondStepReset`.

The body of a creation (unknown members are refused):

| Field | Required | Rule |
|---|---|---|
| `email` | yes | One `@`, a domain with a dot, no white space; at most 256 characters; unique in the tenant without regard to case |
| `displayName` | yes | At most 200 characters |
| `password` | yes | 12 to 128 characters, any characters (no rules of composition) |
| `roles` | yes | At least one of `TenantAdmin`, `Supervisor`, `BillingClerk`, `Cashier`, `Viewer`, `Accountant` (since R2, ADR 0069) |

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
`RolesReplaced`, `Disabled`, `Enabled`, `PasswordReset`, `PasswordChanged`, `SignedIn`, `SignedOut`. The images hold every column but the password
hash.

### 8.12 Sign-in (US-02; BR-601, BR-602; ADR 0031)

The one endpoint that needs no token: it is how a client gets one. It takes no `Idempotency-Key` (its body holds a
password, and the key store keeps a fingerprint of the body), and no answer of it may be cached
(`Cache-Control: no-store`).

| Method and path | Success | Failures |
|---|---|---|
| `POST /api/v1/auth/login` | 200, the token | 400; 401 `invalid_credentials`; 429 `too_many_sign_ins` |
| `POST /api/v1/auth/password` (any signed-in user, policy `SignedIn`) | 200, a new token | 400; 401; 422 `password_current_wrong` |
| `POST /api/v1/auth/second-step` (a token that waits for its second step; body `code`) | 200, a full token | 401 `session_ended`; 403; 422 `second_step_code_wrong`, `second_step_not_enrolled` |
| `POST /api/v1/auth/second-step/enrolment` (a token that must enrol, or any full token; no body) | 200, the secret and its address | 401; 403; 422 `second_step_already_enrolled` |
| `POST /api/v1/auth/second-step/enrolment/confirm` (the same tokens; body `code`) | 200, the recovery codes and a full token | 401; 403; 422 `second_step_code_wrong`, `second_step_enrolment_not_begun`, `second_step_already_enrolled` |
| `GET /api/v1/auth/options` (no token) | 200, `passwordResetByMail` | |
| `POST /api/v1/auth/forgot-password` (no token; body `tenant`, `email`) | 202, no body, whatever was found | 400; 429 `too_many_sign_ins` |
| `POST /api/v1/auth/reset-password` (no token; body `tenant`, `token`, `newPassword`) | 204 | 400; 422 `password_reset_link_invalid`; 429 `too_many_sign_ins` |
| `POST /api/v1/auth/logout` (any signed-in user, policy `SignedIn`) | 204 | 401 |

**A sign-out ends the user's sessions on the server** (ADR 0057). Every token carries the number of the session it was
issued in (the claim `ses`), and every request with a token is held against the user's row: a token whose number is no
longer the user's, or whose user is disabled or gone, answers 401 `session_ended`, from the next request on and on
every device. `POST /api/v1/auth/logout` takes no body and no `Idempotency-Key`, answers 204 with
`Cache-Control: no-store`, and gives the user a new number; so do a change of one's own password (whose answer is a
token of the new session), a password reset by an administrator, a disabling, and a change of the user's roles, the
user's own included. Signing in again gives a token that works. The sign-out is in the audit trail (`SignedOut`, by the
user). A user whose password change is due may sign out. In Development a request without a token acts as the
development user as before; it has no session to end.

The body (unknown members are refused): `tenant`, the code of the hospital; `email`; `password`. The code and the
address are compared without regard to case or the spaces around them. The answer: `accessToken`, `tokenType`
(`Bearer`), `expiresAtUtc`, `expiresInSeconds` (3600), and `credentialsChangeDue`.

**A password set by an administrator is changed at the first sign-in** (ADR 0051, amending ADR 0030). A user an
administrator created, or whose password an administrator reset, signs in with `credentialsChangeDue: true` and gets a token
that lives 15 minutes and carries the claim `pwd_change: required`. With that token every endpoint but
`POST /api/v1/auth/password` answers 403 `password_change_required`. That endpoint takes `currentPassword` and
`newPassword` (12 to 128 characters, not the current one; unknown members refused; `Cache-Control: no-store`), answers
422 `password_current_wrong` when the current one is not right, and on success answers a new token of the usual hour
without the claim, with `credentialsChangeDue: false`. Any signed-in user may change their own password the same way at
any time. The change is in the audit trail (`PasswordChanged`, by the user); neither password is written anywhere. A
user's answer (`GET /api/v1/users/{id}`) says `credentialsChangeDue` too.

**Every refusal is the same 401 `invalid_credentials`**, with the same message: an unknown or suspended hospital, an
unknown address, a wrong password, a disabled user (BR-601). So the answer never tells which hospitals or addresses
exist, and an unknown address costs as much time as a wrong password. Why a sign-in was refused is written to the log
(a warning with the hospital's code and, when the address was found, the user's id); the address and the password
never are. A missing or over-long value is a 400 `validation_failed` like any other. A sign-in is recorded on the user
(`lastSignInAtUtc`) and in the audit trail (`SignedIn`, by the user).

**The second step (ADR 0061).** The answer of a sign-in carries `secondStep`, the stage its token is at:

- `None`: a full token of a user who has no second step and whose roles need none;
- `Pending`: the user has a second step. The token lives 5 minutes and reaches `POST /api/v1/auth/second-step` and
  the sign-out, and everything else answers 403 `second_step_required`;
- `Enrolment`: the user is a TenantAdmin or a Supervisor and has none yet. The token lives 15 minutes and reaches the
  two enrolment actions and the sign-out, and everything else answers 403 `second_step_enrolment_required`;
- `Done`: a full token whose sign-in gave the second step.

A password that is due (`credentialsChangeDue`) is changed first; the answer of the change is then at one of these
stages, and so is the answer of any change of one's own password. `code` is the six digits an authenticator app
shows (TOTP, RFC 6238: HMAC-SHA-1, steps of 30 seconds), accepted for its own step and the one before and after,
and once: the step a code was accepted for and every earlier one are spent. In place of it a **recovery code**
(`XXXX-XXXX-XXXX-XXXX`, with or without its dashes, in any case) signs in once. A wrong code is 422
`second_step_code_wrong`; the fifth in a row ends the user's sessions, the one that gave them included (401
`session_ended`), and the user signs in again.

**Enrolment.** Begin answers `secret` (base32) and `provisioningUri` (the `otpauth://totp/...` address a QR code
holds); beginning again replaces a secret that was never confirmed. Confirm takes the first code of the app and
answers `recoveryCodes` (ten, shown this once: only their hashes are kept) and `signedIn`, the sign-in of a new
session at the stage `Done`; every other session of the user ends. A user who has a second step cannot begin
another (422 `second_step_already_enrolled`), and a token that waits for a code cannot enrol at all (403): an
administrator takes the old one away first (section 8.11). None of the three takes an `Idempotency-Key`, and no
answer of them may be cached. Outside the Development and Demo environments the requirement cannot be switched off.

**A decision needs the second step, and a checker who is not on hold.** Every approve and reject action (every POST
of the policy `Decide`) answers 403 `second_step_required` to a token whose stage is not `Done`, and 403
`approval_on_hold` to a user whose password or second step an administrator reset less than 24 hours ago (the
message says until when). Reading the inbox is no decision.

**A forgotten password (ADR 0061, amended).** Three actions need no token. `GET /api/v1/auth/options` answers
`passwordResetByMail`: whether this deployment has a mail server; it says nothing about any hospital or user.
`POST /api/v1/auth/forgot-password` takes the hospital's code and the address, as the sign-in does, and **always
answers 202 with no body**: for a user who is sent a link, for an unknown hospital or address, for a user who cannot
sign in, for the sixth request of a user within an hour, and where there is no mail server. A link goes to the
address the hospital keeps for the user: `{the app}/reset-password#token=…&hospital=…`, good once, for 30 minutes,
and replaced by a later one. `POST /api/v1/auth/reset-password` takes `tenant` and `token` from the link and
`newPassword` (12 to 128 characters); 204 sets the password and ends every session of the user (401
`session_ended` for their tokens), and answers no token: the user signs in, second step included. Every link that is
not good answers 422 `password_reset_link_invalid`, whatever the reason. Neither POST takes an `Idempotency-Key`; no
answer of them may be cached; both count against the limit per client address below. An unknown member in a body is
400.

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

Read-only, for every role, the day-end report excepted (section 8.20). Each figure ties to the ledger: the totals of the registers to the postings of their
documents, the balances to the accounts they cache (BR-404), the collection of a day to the Cash entries of that business
date. A report reads committed rows and takes no lock.

| Method and path | Answer | Query |
|---|---|---|
| `GET /api/v1/reports/bill-register` | page of posted bills, by bill date | `from`, `to`, `payerId`, `status` (`Posted`, `PartiallyPaid`, `Settled`) |
| `GET /api/v1/reports/payment-register` | page of payments, by payment date, reversed ones included | `from`, `to`, `payerId`, `method`, `status` (`Posted`, `PendingReversal`, `Reversed`), `order` (`OldestFirst` by default, `NewestFirst`) |
| `GET /api/v1/reports/daily-collection` | the collection of one day by method | `date` |
| `GET /api/v1/reports/day-end` | every shift of one day with its cash, and the cash outside the shifts (section 8.20); for a TenantAdmin, a Supervisor, a Viewer and an Accountant only | `date` |
| `GET /api/v1/reports/accounting-export`, `GET /api/v1/reports/accounting-export/summary` | the vouchers of a period for the accountant's package, as a file, and their control totals (section 8.21); for a TenantAdmin, a Supervisor, a Viewer and an Accountant only | `from`, `to` (both required) |
| `GET /api/v1/reports/outstanding-by-payer` | page of payers with a balance, the largest net outstanding first | `search` |
| `GET /api/v1/reports/payers/{payerId}/balance` | one payer's balances (US-40); 404 `payer_not_found` | |
| `GET /api/v1/reports/unapplied-cash` | page of payments that hold unapplied cash, the oldest first | `payerId` |
| `GET /api/v1/reports/available-credit` | page of posted credit notes that hold available credit, the oldest first | `payerId` |
| `GET /api/v1/reports/pending-approvals` | page of the requests that wait for a checker, the oldest first. Since 2026-11-26 (review finding R8) the rows, the count and the oldest are of the kinds of document the caller's role may read (a Cashier is shown no waiting adjustment, a BillingClerk no waiting refund; a prefix change and an opening batch are shown to who may decide), and of every kind for a Supervisor or TenantAdmin. A `documentType` the caller may not read answers an empty page, not 403 | `documentType` |
| `GET /api/v1/reports/reconciliation` | page of drift rows (US-41), by identity and reference | |

**Filters and orders of Prompt 12.2.** Beside the query above, the paged reports take the filters and the `sort` of
section 6 (ADR 0063, amending ADR 0036): the registers an amount from and to, the unapplied cash and the available
credit a period of dates. The totals of a report, over every page, are of the rows its filters leave. With no new
parameter a report answers exactly as before.

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
- **Pending approvals** items: `approvalRequestId`, `documentType`, `action`, `documentId`, `parentDocumentId` (for
  the reversal of an allocation, its payment; of an application of credit, its credit note; null for every other
  request: the decision is sent to that document, ADR 0042), `makerUserId`, `makerComment` (the maker's reason for a
  reversal or a prefix change, null for a posting), `requestedAtUtc`, `waitingDays` (in India, from the day of the
  request to today), and the document's `payerId`, `payerName` and `amount` where it has them (a prefix change has
  none). Totals: `requests`, `oldestRequestedAtUtc`. This report is everyone's, and the frontend's checker's inbox is
  built on it (ADR 0042); the list of section 8.6 stays the checkers'.
- **Reconciliation** (US-41, BR-404; Prompt 6.2): every place where a cached balance differs from the ledger, or the
  ledger or a number series breaks one of its identities (`docs/database.md` sections 5.5 and 5.6). Items: `identity`
  (for example `BillBalance`, `PayerReceivable`, `UnbalancedPosting`, `NumberSeriesGap`), `subjectId` (the bill,
  payment, credit note, payer, posting or series), `reference` (its number, or `Payment 2026-27` for a series), `cached`
  and `ledger` (the two figures that disagree; for a series, the documents numbered and the numbers given). Totals:
  `checkedAtUtc`, `rows`, and `byIdentity` (`identity`, `rows`). An empty report means none: the caches equal the
  ledger and every series is gapless. A drift row is a defect of the highest severity: the report also writes it to the
  log as an error, and never corrects anything (`docs/database.md` section 5.6 says what to do).

### 8.16 Dashboard (US-44, BR-902; ADR 0037)

Read-only, for a TenantAdmin, a Supervisor, a Viewer or an Accountant; a BillingClerk or a Cashier gets 403 `forbidden` (they keep
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
| `GET /api/v1/dashboard/pending-approvals` | the 3 oldest rows of the pending-approvals report, with its totals, filtered as the report is (the three roles of the dashboard read every kind today) | |
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

### 8.17 Bringing in a hospital's records and opening balances (ADR 0053; BR-101, BR-104, BR-603, BR-801, BR-401)

For the day a hospital starts here. Two steps, in this order: its patients and guarantors, then what they owed and
what they had paid in advance at the cut-over. Every file is the text of a CSV file in a JSON string: UTF-8, a comma
between fields, a header line, double quotes around a field that holds a comma. The layouts are in `docs/runbook.md`
section 18. Unknown members of a body are refused. A finding names its `file`, its `line` (1 is the header, 0 the file
as a whole) and its `reason`, and never repeats a patient's name, phone or address.

| Method and path | Who | Success | Failures |
|---|---|---|---|
| `POST /api/v1/imports/parties` | TenantAdmin (`ManageTenant`) | 200, what was done or would be | 400; 409 `duplicate_key` |
| `POST /api/v1/opening-batches/dry-run` | TenantAdmin (`ManageTenant`) | 200, what the files hold | 400 |
| `POST /api/v1/opening-batches` | TenantAdmin (`ManageTenant`) | 201, the batch waiting | 400; 403 `approval_needs_user`; 409 `opening_batch_duplicate`; 422 `opening_batch_has_rejections` |
| `GET /api/v1/opening-batches/{id}` | Supervisor, TenantAdmin (`Decide`) | 200 | 404 `opening_batch_not_found` |
| `GET /api/v1/opening-batches` | Supervisor, TenantAdmin (`Decide`) | 200, a page, newest first | 400 |
| `POST /api/v1/opening-batches/{id}/approve` | Supervisor, TenantAdmin (`Decide`) | 200, approved and posted | 400; 403 `approval_checker_is_maker`, `approval_checker_role`; 404; 409 `opening_batch_not_pending`, `concurrency_conflict`; 422 `opening_batch_has_rejections`; 428 |
| `POST /api/v1/opening-batches/{id}/reject` | Supervisor, TenantAdmin (`Decide`) | 200, rejected | 400; 403; 404; 409; 428 |

**Patients and guarantors.** The body: `patientsCsv` (required), `guarantorsCsv`, `dryRun`. A patient is found by its
MRN, without regard to case: one the hospital has under the same name is *matched* and left as it is; one it has under
another name is rejected and never merged; a new one is registered with the self-pay payer it is (BR-104). A guarantor
is found by `guarantor_ref`, the reference it had before, which the payer keeps. Each row meets the rules a user's own
registration meets. The answer: `dryRun`, `written`, `patientsCreated`, `patientsMatched`, `payersCreated`,
`guarantorsCreated`, `guarantorsMatched`, `rejected`. **One rejected row writes nothing** (`written: false`, still
200): correct the file and send it again. It takes no `Idempotency-Key`: sent twice, the second finds every row matched.
One file holds at most 5,000 rows, and one request at most 600,000 characters of patients and 200,000 of guarantors
(400 `validation_failed` beyond): the proxy in front of the API takes 1 MB a request.

**The opening batch.** The body of the dry run and of the submission: `cutOverDate` (the first day documents are
raised here; the balances are as at the day before), `billsCsv`, `advancesCsv`, `trialBalanceCsv`, and for the
submission `comment`. The dry run answers `accepted`, `receivables`, `receivableTotal`, `advances`, `advanceTotal`,
`creditBalanceTotal` and `rejected`, and writes nothing. A row is rejected when a value is missing or malformed, its
patient or guarantor is not here or is inactive, its date is on or after the cut-over, its old number is in the file
twice or was brought in before; and the three totals must equal the trial balance to the paisa. A batch holds at most
2,000 opening balances. Each file holds at most 250,000 characters; a larger one is a finding of the
file.

The submission needs an `Idempotency-Key`, checks the files the same way, and with one finding answers 422
`opening_batch_has_rejections` and writes nothing. Otherwise it keeps the files, opens an approval request and answers
the batch (`id`, `cutOverDate`, `status`, the counts and totals, `submittedAtUtc`, `submittedByUserId`,
`decidedAtUtc`, `version`); the files are never answered back. The same files for the same cut-over date, while a batch
of them waits or was approved, are 409 `opening_batch_duplicate`: opening balances are brought in once.

**Approval is the posting** (BR-603). A second user, a Supervisor or TenantAdmin, sends `approve` or `reject` (a
comment is required to reject) with `If-Match` and an `Idempotency-Key`. On approval the kept files are checked again
against the hospital as it is now, and then, in one transaction:

- each unpaid bill becomes an **opening receivable**: a posted bill with `origin: "Opening"`, one line for what was
  outstanding, its old number in `legacyNumber`, its old bill date and due date, and a number of its own series,
  `OBR/YY-YY/NNNNNN`, in the fiscal year of the cut-over;
- each advance or credit balance becomes an **opening advance**: a posted payment with `origin: "Opening"`, its old
  reference in `legacyNumber` and `reference`, its old date and method, and a number `OBA/YY-YY/NNNNNN`. A credit
  balance of the system before is brought in the same way and says so in its notes;
- each is posted to the ledger against the account `OpeningBalances`, dated the day before the cut-over, so an opening
  receivable is not revenue here and an opening advance is not cash received here.

From then on an opening receivable is allocated to, credited, adjusted and reported like any posted bill, and an
opening advance is allocated and refunded like any payment. An opening advance is **never reversed** (422
`opening_advance_not_reversible`): no money came in here; refund it instead. The prefixes `OBR` and `OBA` are the same
for every hospital and are not settings: a prefix change that names them, or asks for either type, is refused.
`GET /api/v1/bills/{id}` and `GET /api/v1/payments/{id}` answer `origin` (`Standard` or `Opening`) and `legacyNumber`.
In the audit trail: `OpeningBatch` `Submitted`, `Approved`, `Rejected`; `Bill` `BroughtForward`; `Payment`
`BroughtForward`. The reconciliation report gains identity 12, `OpeningBalancesAccount`.

### 8.18 Letterhead, SAC codes and the record of a print (ADR 0054)

What a printed document needs beyond the document itself. The printed pages are Prompt 10.4b; this is their data.

| Method and path | Who | Success | Failures |
|---|---|---|---|
| `GET /api/v1/settings/letterhead` | Anyone signed in (`SignedIn`) | 200 | |
| `PUT /api/v1/settings/letterhead` | TenantAdmin (`ManageTenant`) | 200, the settings | 400; 409 `concurrency_conflict`; 428 |
| `GET /api/v1/settings/letterhead/logo` | Anyone signed in (`SignedIn`) | 200, the image, `ETag` its SHA-256 | 404 `letterhead_image_not_found` |
| `GET /api/v1/settings/letterhead/signature` | Anyone signed in (`SignedIn`) | 200, the image, `ETag` its SHA-256 | 404 `letterhead_image_not_found` |
| `PUT /api/v1/settings/letterhead/logo` | TenantAdmin (`ManageTenant`) | 200, the settings | 400; 409 `concurrency_conflict`; 428 |
| `PUT /api/v1/settings/letterhead/signature` | TenantAdmin (`ManageTenant`) | 200, the settings | 400; 409 `concurrency_conflict`; 428 |
| `POST /api/v1/settings/letterhead/logo/remove` | TenantAdmin (`ManageTenant`) | 200, the settings | 409 `concurrency_conflict`; 428 |
| `POST /api/v1/settings/letterhead/signature/remove` | TenantAdmin (`ManageTenant`) | 200, the settings | 409 `concurrency_conflict`; 428 |
| `POST /api/v1/bills/{id}/prints` | Who reads bills (`ReadBills`) | 201 | 400; 404 `bill_not_found`; 409; 422 `document_not_printable` |
| `POST /api/v1/payments/{id}/prints` | Who reads payments (`ReadPayments`) | 201 | 400; 404 `payment_not_found`; 409; 422 `document_not_printable` |
| `POST /api/v1/refunds/{id}/prints` | Who reads refunds (`ReadRefunds`) | 201 | 400; 404 `refund_not_found`; 409; 422 `document_not_printable` |

**The letterhead** is what the hospital prints at the head and foot of its documents: `name` (the hospital's name,
read-only here), `legalName`, `addressLine1`, `addressLine2`, `city`, `state`, `pinCode` (six digits), `phone`, `email`,
`gstin` (fifteen letters and digits, kept in capitals) and `signatory` (the line under the place of a signature). Every
part but the name is optional and null until it is set. `PUT` sends every part, and one left out or blank is cleared;
it needs `If-Match` with the version of the settings, which the letterhead shares, and answers the settings
(`GET /api/v1/settings` now carries `letterhead` too). The state is the place of supply of every document. Whether a
GSTIN is a real registration the API cannot know. In the audit trail of the tenant: `LetterheadChanged`.

**The logo and the signature (ADR 0059, Prompt 10.4c).** The letterhead also answers `signatoryDesignation` (set with
the other parts by `PUT`), and `logoSha256` and `signatureSha256`: the SHA-256 of each image the hospital prints now,
null when it has none. The images themselves are read and written at their own paths. `PUT .../logo` and
`PUT .../signature` take **the file itself as the body**, not JSON, with its media type as the `Content-Type`: a PNG,
JPEG or WebP image, told by its own bytes (the signature at its start and the size its header states), at most 256 KB
for the logo and 128 KB for the signature, and at most 2000 pixels a side. An SVG, any other file, a file that is too
large and a `Content-Type` that is not what the bytes are get 400 `validation_failed` with the reason under `content`;
Both need `If-Match` with the version of the settings and answer
the settings. `POST .../remove` takes the image off the letterhead (there is no `DELETE` in this API); the stored bytes
stay, so that a hash in the audit trail still finds its image, and the same bytes uploaded again are the row there is.
`GET` answers the bytes with their media type, for anyone signed in, and only ever the caller's own hospital's: the
path names no hospital. In the audit trail of the tenant: `LetterheadImageChanged`, whose event and images carry the
hash before and after and never the image.

**The SAC code** of a service item (`sacCode`, six digits, optional) is sent with its other details on
`POST /api/v1/service-items` and `PATCH /api/v1/service-items/{id}` and answered with them. A bill line answers the
`sacCode` its service had when the line was written; it is taken from the catalogue, never from the request, and a
free-text line has none.

**A print is recorded** before a document is printed. The request has no body and needs an `Idempotency-Key`, so one
sent twice is one print. The answer: `copyNumber` (1, 2, 3 and so on for that document), `original` (true for the first
alone) and `printedAtUtc`; the printed page says "Original" or "Duplicate" from it. Only what took financial effect is
printed: a posted bill (an opening receivable included), a payment that was not reversed, a posted refund. Anything
else is 422 `document_not_printable`. The records are insert-only, like the audit trail; there is no endpoint that
lists, changes or removes them.

### 8.19 Print settings, and who recorded a document (ADR 0059)

How the hospital prints each kind of document. Settings change the layout of a sheet and nothing else: no figure, and
not the mark of the original or a duplicate.

| Method and path | Who | Success | Failures |
|---|---|---|---|
| `GET /api/v1/settings/print` | Anyone signed in (`SignedIn`) | 200, four answers | |
| `PUT /api/v1/settings/print/{documentType}` | TenantAdmin (`ManageTenant`) | 200, the settings, `ETag` | 400; 409 `concurrency_conflict`; 428 |

`documentType` is `Bill`, `Payment` (its receipt), `Refund` (its voucher) or, since Prompt 11.2b, `ShiftClosing` (the
closing slip of a shift, section 8.20, which may be printed on the roll and whose print is not recorded). `GET` always
answers the four, in that order; a kind the hospital never saved answers **the defaults**, which are the sheet as it was before there were
settings, with the version `AAAAAAAAAAA=` (eight zero bytes). `PUT` sends every part and needs `If-Match` with the
version read: the version of none for a first save, which makes the row, and the row's own after that. Each part:

| Part | Values | Default |
|---|---|---|
| `paper` | `A4`, `A5`; `Roll80` (a roll 80 mm wide) for a receipt or a refund voucher only | `A4` |
| `marginTopMm`, `marginRightMm`, `marginBottomMm`, `marginLeftMm` | 5 to 25 | 14, 12, 14, 12 |
| `logoPosition` | `Left`, `Centre`, `Right` | `Left` |
| `logoSize` | `Small`, `Medium`, `Large` | `Medium` |
| `prePrintedStationery` | true: the paper carries the hospital's head, and the sheet prints none of its own | false |
| `stationeryTopMm` | 0 to 80: the space left at the top for that head | 0 |
| `showPatientContact` | the patient's phone and address | false |
| `showPayer` | the payer | true |
| `showSacColumn`, `showTaxSplit` | a bill's SAC column, and its tax as CGST and SGST | true |
| `showAmountInWords` | the amount in words | true |
| `showSignatureBlock` | the signature block | true |
| `showCashierName` | who recorded a receipt or a refund voucher, by name | false |
| `footerLine`, `terms` | plain text, at most 200 and 1000 characters; trimmed, a blank one is null | null |

**What a bill may never switch off.** For `Bill`, `showSacColumn` and `showTaxSplit` are always true: a tax invoice must
show the SAC code of each service and its tax in its halves (ADR 0054). Sending false, or `Roll80`, for a bill is 400
`validation_failed` with the reason under the part's name; the table refuses the same by two CHECK constraints. For a
receipt and a voucher the two switches mean nothing and are stored as sent.

In the audit trail: entity `PrintSettings`, action `Changed`, with what the settings were and are.

**Who recorded a document.**

| Method and path | Who | Success | Failures |
|---|---|---|---|
| `GET /api/v1/payments/{id}/recorder` | Who reads payments (`ReadPayments`) | 200 | 404 `payment_not_found` |
| `GET /api/v1/refunds/{id}/recorder` | Who reads refunds (`ReadRefunds`) | 200 | 404 `refund_not_found` |

Each answers `{ "name": ... }`: the display name of the user who recorded the payment or drafted the refund, for a
sheet whose settings ask for it (`showCashierName`), or null where no user of the hospital did. It is a read of its
own, so that a payment and a refund answer the same wherever they are read. The name and nothing else of the user is
answered, to whoever may read the document.

### 8.20 Shifts of the cash counter and the day-end report (US-45 to US-48; BR-310 to BR-315, BR-903; ADR 0060)

A user who takes cash opens a shift with the float of the drawer and closes it with a count by denomination. While it
is open, the cash payments the user records and the cash refunds the user drafted join it: their ledger postings name
the shift, and what its drawer should hold is the float and the Cash entries of those postings, read from the ledger
(BR-312). A shift is not a financial document: it has no number and posts nothing, so its POSTs take no
`Idempotency-Key`; a second opening is refused by the rule of one shift at a time, and a closing or a decision sent
again by `If-Match`. A difference between the count and the expected cash is recorded on the shift with its reason and
is never posted to the ledger (BR-314).

| Method and path | Who | Success | Failures |
|---|---|---|---|
| `POST /api/v1/cashier-shifts` | TenantAdmin, Supervisor, BillingClerk, Cashier | 201, the shift (`Open`), `Location`, `ETag` | 400; 403 `shift_not_own`; 422 `shift_already_open` |
| `GET /api/v1/cashier-shifts/{id}` | every role; a Cashier and a BillingClerk only a shift of their own | 200, the shift, `ETag` | 404 `shift_not_found` |
| `GET /api/v1/cashier-shifts?cashierUserId=&mine=&status=&unclosed=&date=&page=&pageSize=` | every role; a Cashier and a BillingClerk get their own, whatever is asked | 200, a page, the newest first | 400 |
| `POST /api/v1/cashier-shifts/{id}/close` (needs `If-Match`) | the cashier of the shift | 200, the shift `Closed`; **202**, the shift in `PendingApproval` | 400; 403 `shift_not_own`; 404 `shift_not_found`; 409 `concurrency_conflict`; 422 `shift_not_open`, `shift_difference_needs_reason`; 428 |
| `POST /api/v1/cashier-shifts/{id}/approve` (needs `If-Match`; optional body `comment`) | TenantAdmin, Supervisor | 200, the shift `Closed` | 403 `approval_checker_is_maker`, `approval_checker_role`, `approval_needs_user`; 404; 409 `concurrency_conflict`; 422 `shift_not_pending_approval`; 428 |
| `POST /api/v1/cashier-shifts/{id}/reject` (needs `If-Match`; body `comment`, required) | TenantAdmin, Supervisor | 200, the shift `Open` again | 400; 403; 404; 409; 422 `shift_not_pending_approval`; 428 |
| `GET /api/v1/reports/day-end?date=` | TenantAdmin, Supervisor, Viewer, Accountant | 200, the day-end report | 400 |

**Opening.** The body is `openingFloat` (0 or more, at most two decimals). The shift belongs to the user who asks and
takes today's business date in India, which it keeps however long it stays open. A user has one shift at a time: a
second is refused while the first is `Open` or `PendingApproval` (422 `shift_already_open`), for two requests at the
same instant by a unique index.

**Cash in a shift (BR-311).** `POST /api/v1/payments` with `method` `Cash` needs the open shift of the user who
records it (422 `shift_not_open`) and is dated the business date of that shift: a cash payment sent without
`paymentDate` takes that date, and one dated another day is 422 `shift_date_mismatch`. A cash refund is paid out of
the open shift of the user who drafted it, when a checker approves it: `POST /api/v1/refunds/{id}/approve` answers 422
`shift_not_open` when that user has none, and 422 `shift_date_mismatch` when the refund is dated another day than that
shift; the refund still waits, to be approved when the shift is open or rejected and drafted again. A cash payment
whose reversal is approved while its shift is still open, on that shift's business date, comes out of that drawer; a
later reversal leaves the closed shift as it was and is cash outside the shifts in the day-end report. Money by any
other method needs no shift and names none. Every cash document that joins a shift changes the shift's version: a
closing and a cash document sent at the same instant cannot both be committed, and the loser answers 409
`concurrency_conflict`.

**Closing.** The body is `count`, a list of `{ denomination, pieces }` (each of 2000, 500, 200, 100, 50, 20, 10, 5, 2
and 1 at most once, 0 to 99,999 pieces; one left out has none), `looseChange` (the cash below one rupee, 0 to 0.99)
and `reason`. The counted cash is the count and the loose change; the difference is counted less expected. With no
difference the shift is closed at once (200). With one, `reason` is required (422 `shift_difference_needs_reason`) and
the closing waits (202): an approval request is opened in the same transaction, with the cashier as its maker, and is
listed in the checkers' inbox (section 8.6) as `CashierShift` with the action `Close`. A Supervisor or TenantAdmin who
is not the cashier approves, which closes the shift, or rejects with a comment, which opens it again without its
count. While it waits no cash joins the shift and its cashier opens no other. A closed shift never changes (BR-315).

**A shift:** `id`, `cashierUserId`, `cashierName`, `businessDate`, `status` (`Open`, `PendingApproval`, `Closed`),
`openingFloat`, `openedAtUtc`, `cashDocuments`, `cashReceived` and `cashPaidOut` (the debits and the credits to Cash of
the postings that name the shift, read when the shift is read), `expectedCash` (the float and their difference),
`count`, `looseChange`, `countedCash`, `difference` (above zero an excess, below zero a shortage),
`differenceReason`, `countedAtUtc`, `closedAtUtc`, `version`. The count and what follows from it are null until the
shift is counted.

**The day-end report (BR-903)**, for a business date in India, today when none is given: `date`; `shifts`, every shift
of that date in the order they were opened, each with `shiftId`, `cashierUserId`, `cashierName`, `status`,
`openedAtUtc`, `closedAtUtc`, `openingFloat`, `received`, `paidOut`, `net`, `expectedCash`, `countedCash` and
`difference`; `shiftsTotal`; `outsideShifts`, the cash of the date that no shift holds (a cash payment reversed after
its shift was closed, and cash recorded before shifts existed); `cash`, the two together; `difference`, the
differences of the counted shifts together; and `unclosedShifts`. Each of the three cash figures is `{ received,
paidOut, net }`. The `net` of `cash` equals the `net` of the Cash row of the daily collection of that date (section
8.15), always: both are the ledger's Cash entries of the cash documents of the date.

The trail of a shift (`entityType=CashierShift`: `Opened`, `Closed`, `SubmittedForApproval`, `ClosingApproved`,
`ClosingRejected`) is read by a TenantAdmin, a Supervisor, a Viewer and an Accountant. A cash document that joins a shift is not a
change of the shift and leaves no record in its trail: the trail of the document says what happened.

### 8.21 The accountant's export and the names of the accountant's ledgers (US-52; BR-904; ADR 0062)

| Endpoint | Who | Success | Refusals |
|---|---|---|---|
| `GET /api/v1/reports/accounting-export/summary?from=&to=` | TenantAdmin, Supervisor, Viewer, Accountant (`AccountingExport`) | 200, the control totals | 400 |
| `GET /api/v1/reports/accounting-export?from=&to=` | the same | 200, the file (`application/xml`, an attachment, `Cache-Control: no-store`) | 400; 422 `accounting_ledger_not_mapped` |
| `GET /api/v1/settings/accounting-ledgers` | TenantAdmin (`ManageTenant`) | 200, the twelve names, `ETag` | |
| `PUT /api/v1/settings/accounting-ledgers` | TenantAdmin (`ManageTenant`) | 200, the names, `ETag` | 400; 409 `concurrency_conflict`; 428 |

**The period** is two business dates in India, both required and both included, at most 92 days; anything else is
400 with the field `from` or `to`.

**The vouchers.** One posting of the ledger is one voucher, and its lines are the posting's entries, so every voucher
balances and the vouchers of a period add up to the ledger's entries of the period. A payment recorded is a
`Receipt`; a refund and a payment's reversal a `Payment`; everything else a `Journal`. An entry to `Cash` goes to the
ledger of the method of its payment or refund (`Cash`, `Card`, `Upi`, `BankTransfer`, `Cheque`); the `Revenue` of a
bill or of a credit note is split into `Revenue`, its taxable value, and `TaxOutput`, its tax. The voucher's number
is our document's number; a posting that follows from a document carries it with a mark of its own
(`RCT/26-27/000012/AL-1a2b3c4d`: `AL` an allocation, `AR` its reversal, `RV` a payment's reversal, `CA` an
application of credit, `CR` its reversal), so that no two vouchers share a number.

**The summary** answers `from`, `to`, `vouchers`, `receipts`, `payments`, `journals`; `controlTotal`, the debits of
every voucher; `ledgerTotal`, the debits of the ledger's entries of the period read on their own, always equal to
it; `ledgers`, what each ledger the period uses receives (`ledger`, `name` or null, `debit`, `credit`); and
`missing`, the ledgers the period uses and the hospital has not named. The summary never refuses for a missing name.

**The file** is the XML Tally imports as vouchers: `ENVELOPE` / `HEADER` / `TALLYREQUEST` "Import Data", then `BODY` /
`IMPORTDATA` with `REQUESTDESC` / `REPORTNAME` "Vouchers" and `REQUESTDATA`, a `TALLYMESSAGE` with one `VOUCHER`
(`REMOTEID`, `VCHTYPE`, `ACTION="Create"`) for each voucher: `DATE` (`YYYYMMDD`), `VOUCHERTYPENAME`, `VOUCHERNUMBER`,
`NARRATION` (what happened, and the payer's name) and an `ALLLEDGERENTRIES.LIST` for each line with `LEDGERNAME`,
`ISDEEMEDPOSITIVE` (`Yes` for a debit) and `AMOUNT` (negative for a debit). UTF-8. It is refused with 422
`accounting_ledger_not_mapped`, whose message lists them, while a ledger the period uses has no name. What of this
shape was confirmed against Tally's documentation, and what was not, is in ADR 0062; no Tally has imported one yet.

**The names** are `receivable`, `unappliedCash`, `creditAvailable`, `revenue`, `taxOutput`, `adjustments`,
`openingBalances`, `cash`, `card`, `upi`, `bankTransfer` and `cheque`, each null or at most 100 characters on one
line, and `version`. A hospital that saved none answers none, with the version of eight zero bytes, which is the
`If-Match` of its first save. A member left out, null or empty takes that name away; an unknown member is 400. Two
may be the same. The trail (`entityType=AccountingLedgers`, `Changed`) is the TenantAdmin's.

### 8.22 Settlement channels and the import of a bank's statement (US-60, US-61; BR-1001 to BR-1005; ADR 0065)

R2 Reconciliation, built by Prompt 13.1b. A channel is one of the hospital's bank accounts or gateway accounts; a bank
channel's statement is imported from a CSV through the channel's column mapping, a dry run first.

| Endpoint | Who | Success | Refusals |
|---|---|---|---|
| `POST /api/v1/settlement-channels` | TenantAdmin (`ManageTenant`) | 201, the channel, `ETag`, `Location` | 400; 409 `settlement_channel_name_duplicate`; 422 `settlement_channel_bank_invalid` |
| `GET /api/v1/settlement-channels` | TenantAdmin, Supervisor, Viewer, Accountant (`ReadReconciliation`) | 200, a page by name; `kind`, `status` | 400 |
| `GET /api/v1/settlement-channels/{id}` | the same | 200, the channel, `ETag` | 404 `settlement_channel_not_found` |
| `PATCH /api/v1/settlement-channels/{id}` (needs `If-Match`) | TenantAdmin | 200, the channel, `ETag` | 400; 404; 409 `concurrency_conflict`, `settlement_channel_name_duplicate`; 422 `settlement_channel_bank_invalid`, `settlement_channel_not_bank`; 428 |
| `POST /api/v1/settlement-channels/{id}/deactivate` (needs `If-Match`) | TenantAdmin | 200, the channel `Inactive` | 404; 409; 422 `settlement_channel_already_inactive`; 428 |
| `POST /api/v1/settlement-channels/{id}/reactivate` (needs `If-Match`) | TenantAdmin | 200, the channel `Active` | 404; 409; 422 `settlement_channel_already_active`; 428 |
| `POST /api/v1/statement-imports/dry-run` | TenantAdmin, Supervisor, Accountant (`ImportStatements`) | 200, what the import would do; stores nothing, no `Idempotency-Key` | 400 `statement_file_unreadable` and field errors; 404 `settlement_channel_not_found`; 422 `settlement_channel_not_bank`, `settlement_channel_inactive` |
| `POST /api/v1/statement-imports` (needs `Idempotency-Key`) | the same | 201, the import, `Location` | the dry run's, and 409 `statement_file_changed`, `statement_file_already_imported` |
| `GET /api/v1/statement-imports` | `ReadReconciliation` | 200, a page, the latest first; `channelId` | 400 |
| `GET /api/v1/statement-imports/{id}` | the same | 200, the import | 404 `statement_import_not_found` |
| `GET /api/v1/statement-lines` | the same | 200, a page, the latest first; `channelId`, `importId`, `status`, `direction`, `from`, `to`, `search`, `sort` (`transactionDate`, `amount`) | 400 |
| `POST /api/v1/settlement-channels/{id}/matching-runs` | TenantAdmin, Supervisor, Accountant (`ImportStatements`) | 200, the counts of the run; no `Idempotency-Key` (it posts nothing) | 404 `settlement_channel_not_found`; 409 `matching_run_in_progress`; 422 `settlement_channel_not_bank` |

**A channel** answers `id`, `kind` (`Bank`, `Gateway`), `name`, `accountLabel` (at most the last four digits, never the
full number), `gateway` (`Razorpay`, a gateway only), `bankChannelId` (the bank a gateway settles into), `dateWindowDays`
(0 to 10, 3 by default), `mapping` (a bank only), `status`, the two instants and `version`. The kind and the gateway
never change; a bank sends its mapping on every `PATCH` and a gateway its `bankChannelId`.

**The mapping** is `headerRow` (the line of the header, 1 to 50, the lines above it skipped), `dateColumn`,
`dateFormat` (one of `dd/MM/yyyy`, `dd-MM-yyyy`, `dd.MM.yyyy`, `dd/MM/yy`, `dd-MM-yy`, `dd-MMM-yyyy`, `dd-MMM-yy`,
`dd MMM yyyy`, `yyyy-MM-dd`), `valueDateColumn`, `descriptionColumn`, `referenceColumn`, `amountLayout` (`OneColumn`
with `amountColumn`, a signed amount, or a trailing `Cr` or `Dr`; `TwoColumns` with `debitColumn` and `creditColumn`),
`balanceColumn` and `decimalStyle` (`Point` for 1,23,456.78, `Comma` for 1.23.456,78). Columns are named by their
header, without regard to case; each value has a column of its own.

**The file** is sent as text in the body: `channelId`, `fileName`, `text` (UTF-8, at most 900,000 characters, about
1 MB: a longer statement is sent as several files) and, for the import, `fileHash`, the hash its dry run answered. A
text holding the replacement character (a file that was not UTF-8), with no header line, or whose header lacks a column
of the mapping is 400 `statement_file_unreadable`, whose message says which. A row is refused, not the file, for a
missing or unreadable date or amount, an amount of zero, both a debit and a credit, more than two decimals, a date
after today in India, or neither a description nor a reference.

**The dry run** answers `fileHash`, `alreadyImported`, `rowsRead`, `linesToAdd`, `linesAlreadyHeld`, `rowsRefused`,
`moneyIn` and `moneyOut` (of the lines it would add), `firstDate`, `lastDate`, `refusals` (each `rowNumber` and
`reason`) and the first 1,000 `lines` with `alreadyHeld`. **The import** stores the lines the channel does not hold
yet and answers the import: `rowsRead`, `linesAdded`, `linesAlreadyHeld`, `rowsRefused`, the dates and who imported it.
A line is held when the channel has as many lines with its fingerprint (the date, direction, amount, reference,
balance and description) as its place among the identical lines of the file (BR-1004). The same file again is 409
`statement_file_already_imported`; another file than the dry run's is 409 `statement_file_changed`.

**A line** answers `id`, `channelId`, `importId`, `rowNumber`, `transactionDate`, `valueDate`, `direction` (`In`,
`Out`), `amount` (above zero), `reference`, `description`, `balance` (may be below zero), `rawText` (the row as the file
held it), `status` (`Unmatched`, `Matched`, `Excluded`), `version`, and since 13.2 `matchReason` (why its active match
was made, or null), `proposalReason` and `proposalScore` (its best open proposal, or null) and `proposalCount`. Nothing
changes the read fields of a line, and nothing removes a line or an import. The trails are `SettlementChannel`,
`StatementImport` and `Match` (action `Made`), read with `ReadReconciliation`.

**A matching run** (13.2, ADR 0066) reads the channel's unmatched lines and the payments (money in) and refunds (money
out) no active match holds: posted, not reversed, never cash; UPI and card too unless an active gateway channel settles
into this bank. It **confirms** a line only when the reference agrees, the amount is equal, the item is dated in the
channel's window, and exactly one item fits the line and one line the item. A reference agrees when the line's equals
the item's (without case, spaces and leading zeros), or when an item's reference of 8 characters or more is written in
the line's description. Otherwise it **proposes**, by the first rule that finds anything: (1) the reference agrees but
not the rest, 90; (2) the amount and the date, one to three candidates, 70; (3) two to five items of one payer adding up
to the line, 50; each less 5 a day between the item and the line. A run withdraws a line's open proposals before
proposing again. One run of a channel at a time: a second answers 409 `matching_run_in_progress` (a run left under way for 15 minutes by a stopped process is abandoned). Lines are committed 100 at a time; a batch in which a user changed a line first is redone line by line, and that line skipped. It
answers `linesRead`, `matched`, `proposed`, `left` and `skipped`. A match posts nothing.

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
| `approval_checker_is_maker` | 403 | You asked for this yourself, so you cannot decide it: a second person has to approve or reject it (BR-603). For the user who wrote the document and did not ask: You wrote this document or changed it last, so you cannot approve it: a second person has to (BR-603). |
| `approval_already_decided` | 409 | This request has already been decided. Read the document again. |
| `approval_needs_user` | 403 | This action needs a signed-in user: an approval is asked for and given by people (BR-603). |
| `approval_checker_role` | 403 | Only a Supervisor or a TenantAdmin may approve or reject (BR-603). |
| `sort_not_supported` | 400 | This list is not sorted by "…". It is sorted by one of: …; a "-" in front gives the largest or latest first. |
| `filter_invalid` | 400 | A filter of this list cannot be read. The errors are listed per filter. |

### 9.3 Identity: tenants, users, sign-in, settings, prefixes

| Code | Status | Message |
|---|---|---|
| `invalid_credentials` | 401 | The hospital code, the e-mail address or the password is not right, or the user may not sign in. |
| `password_change_required` | 403 | Set a password of your own first: the one you were given must be changed. |
| `session_ended` | 401 | This session has ended. Sign in again. |
| `password_current_wrong` | 422 | The current password is not right; nothing was changed. |
| `second_step_code_wrong` | 422 | The code is not right. Give the code your authenticator app shows now, or a recovery code you have not used. |
| `second_step_not_enrolled` | 422 | You have no second step to give a code for. |
| `second_step_already_enrolled` | 422 | You have a second step already. An administrator can take it away if you lost it. |
| `second_step_enrolment_not_begun` | 422 | Begin the enrolment first: there is no secret to confirm. |
| `second_step_required` | 403 | This needs the second step of your sign-in: give the code of your authenticator app first. |
| `second_step_enrolment_required` | 403 | Your role needs a second step at sign-in. Set it up first. |
| `approval_on_hold` | 403 | Your password or second step was reset by an administrator: you can approve and reject again after …. |
| `password_reset_link_invalid` | 422 | This link is not good any more: it works once, for 30 minutes. Ask for a new one. |
| `tenant_invalid` | 400 | The tenant is not valid. |
| `tenant_suspended` | 403 | This hospital is suspended; ask the operator of BillingRecon. |
| `tenant_code_taken` | 409 | A hospital with this code exists already; nothing was created. |
| `tenant_not_found` | 404 | No hospital has this code. |
| `tenant_status_unchanged` | 422 | The hospital is suspended already (or active already); nothing was changed. |
| `user_not_found` | 404 | The user does not exist. |
| `user_email_taken` | 409 | Another user of this hospital already has this e-mail address (BR-601). |
| `user_already_disabled` | 422 | The user is already disabled. |
| `user_already_active` | 422 | The user is already active. |
| `user_last_tenant_admin` | 422 | This is the last active tenant administrator; make another user a TenantAdmin first. |
| `user_cannot_disable_self` | 422 | You cannot disable your own user; another administrator can. |
| `prefix_change_not_found` | 404 | The prefix change does not exist. |
| `letterhead_image_not_found` | 404 | The letterhead has no such image. |
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
| `credit_note_exceeds_bill_total` | 422 | The bill comes to … and … of it is credited already; this credit note would take the credits above the bill (BR-210). Since 2026-11-26 also when the bill's posted adjustments, with its credits, leave less than this credit note: the message then names what was taken off by adjustments. |
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
| `payment_reference_already_recorded` | 422 | A payment of this payer by this method with this reference is recorded already and was not reversed; the message names it. Cash is never compared (BR-301; review finding R2). |
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
| `opening_advance_not_reversible` | 422 | An opening advance is not reversed: it was brought in at the cut-over, not received here. Refund it instead. |
| `refund_not_found` | 404 | The refund does not exist. |
| `refund_not_draft` | 422 | Only a draft refund can be corrected, voided or submitted. |
| `refund_pending_approval` | 409 | The refund is waiting for approval and cannot be changed until it is approved or rejected (BR-604). |
| `refund_not_pending_approval` | 422 | The refund is not waiting for approval, so there is nothing to approve or reject. |
| `refund_already_posted` | 409 | The refund is posted already and can no longer be changed (BR-006, BR-308). |
| `refund_date_in_future` | 422 | The refund date cannot be in the future (BR-205). |
| `refund_date_too_old` | 422 | The refund date cannot be earlier than …, the earliest date this hospital allows (BR-205). |
| `refund_date_before_source_date` | 422 | The refund date cannot be before the date of the document it comes from, … (BR-307). |
| `refund_exceeds_available` | 422 | … has … left to refund; a refund cannot be for more (BR-307). |
| `shift_not_found` | 404 | The shift does not exist. Also the shift of another cashier, to a user who reads only their own. |
| `shift_not_open` | 422 | Cash is taken and paid out in an open shift (BR-311). Open your shift first. For a refund: the cashier who drafted it has no open shift. Also a shift that is counted or closed already. |
| `shift_already_open` | 422 | You have a shift that is open or waits for approval; a user has one shift at a time (BR-310). |
| `shift_date_mismatch` | 422 | Cash in this shift is dated …, the business date of the shift (BR-311). Use that date, or close the shift and open a new one. |
| `shift_not_own` | 403 | A shift is opened and closed by its own cashier. |
| `shift_difference_needs_reason` | 422 | The counted cash differs from the expected cash by …; a difference needs a reason (BR-313). |
| `shift_not_pending_approval` | 422 | The shift's closing is not waiting for approval, so there is nothing to approve or reject. |

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
| `document_not_printable` | 422 | Only a posted document is printed: this one is a draft, waits for approval, or was voided or reversed. |
| `accounting_ledger_not_mapped` | 422 | The accountant's export needs a ledger name for: …. An administrator sets them under Administration, Settings. |
| `opening_batch_not_found` | 404 | The opening batch does not exist. |
| `opening_batch_not_pending` | 409 | The opening batch was decided already; it is approved or rejected once. |
| `opening_batch_duplicate` | 409 | These files were submitted before for this cut-over date; opening balances are brought in once. |
| `opening_batch_has_rejections` | 422 | The files cannot be brought in as they are: *n* finding(s). Run the dry run, which names each with its file, line and reason; nothing was written. |

`ledger_balance_negative` is the ledger's own refusal under its lock (BR-405). No endpoint answers it as it is: every use
case whose posting can lower a balance answers it as 409 `concurrency_conflict`, since a request that committed first
changed what this one read; the others only raise balances and cannot meet it. `tenant_invalid` refuses a tenant row
that onboarding would make wrongly. No endpoint creates, suspends or resumes a tenant: `tenant_invalid`,
`tenant_code_taken`, `tenant_not_found` and `tenant_status_unchanged` are the refusals of the operator's command on the
server (ADR 0052, `docs/runbook.md` section 17), which prints them; they are listed for completeness.

### 9.9 Reconciliation: settlement channels and statements (R2)

| Code | Status | Message |
|---|---|---|
| `settlement_channel_not_found` | 404 | The settlement channel does not exist. |
| `settlement_channel_name_duplicate` | 409 | A settlement channel with this name already exists (BR-1001). |
| `settlement_channel_already_inactive` | 422 | The settlement channel is already inactive. |
| `settlement_channel_already_active` | 422 | The settlement channel is already active. |
| `settlement_channel_inactive` | 422 | The settlement channel is inactive and takes no import (BR-1001). |
| `settlement_channel_not_bank` | 422 | Only a bank channel has a column mapping and takes a bank's statement. |
| `settlement_channel_bank_invalid` | 422 | A gateway settles into an active bank channel of the hospital. |
| `statement_import_not_found` | 404 | The statement import does not exist. |
| `statement_file_already_imported` | 409 | This file was already imported into this channel; nothing was added (BR-1004). |
| `statement_file_changed` | 409 | The file differs from the one the dry run read. Run the dry run again (BR-1003). |
| `statement_file_unreadable` | 400 | The file cannot be read: … (too large, not UTF-8, no header, a missing column). |
| `matching_run_in_progress` | 409 | A matching run of this channel is under way. Wait for it and look at its result (BR-1029). |
