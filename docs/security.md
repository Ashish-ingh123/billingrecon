# Security

Authentication, authorisation, tenant isolation, maker-checker, secrets, data protection and the audit, as built, with
the decisions they come from. Written with Prompt 5.3 (2026-11-21) and brought up to date with Prompt 5.3b; the threat
model at the end lists what is known and not done yet.

---

## 1. Authentication (ADR 0031)

- A user signs in at `POST /api/v1/auth/login` with the code of the hospital, the e-mail address and the password
  (`docs/api.md` section 8.12). Every refusal answers the same 401 `invalid_credentials` and costs one password
  verification, so the answer says nothing about which hospitals or addresses exist. The reason goes to the log,
  never the address or the password.
- The answer is a JWT the API issues and checks itself: HS256 with `Jwt:SigningKey` (256 bits or more), issuer
  `BillingRecon`, audience `BillingRecon.Api`, 60 minutes by the application's clock, with the user (`sub`), the
  tenant (`tenant_id`) and the roles (`role`). No refresh token, no revocation: a token lives until it expires, also
  when its user is disabled or loses a role meanwhile.
- **Deny by default:** every endpoint needs a valid token but the sign-in, the health checks and, in Development, the
  OpenAPI document and the Scalar page. In Development a request without a token acts as the development user (a
  TenantAdmin); Development must never be the environment of a machine others can reach.
- Passwords (ADR 0030): PBKDF2-HMAC-SHA-256, 600,000 iterations, a salt of 16 bytes per password, 12 to 128
  characters. Neither a password nor its hash is ever in an answer, an audit image, a log line or the fingerprint of an
  idempotency key; the user POSTs and the sign-in take no Idempotency-Key for that reason.

## 2. Authorisation (ADR 0032)

The permission matrix of `docs/requirements.md` section 1 is applied as named policies, one per capability; every
action of the API names exactly one, and an architecture test keeps it so. A role a policy does not admit gets 403
`forbidden`. A blank cell of the matrix is no access, not even reading, with one exception: a Cashier reads credit
notes, to refund from one. The trail of a user in the audit is the TenantAdmin's alone, as the users are; so are the
trails of the tenant, of its prefix changes and of its number series (ADR 0033), and the settings themselves. The table
of what each role may call is in `docs/api.md` section 2, and one table-driven test walks every endpoint with every
role against the database.

## 3. Tenant isolation (ADR 0009, ADR 0015, ADR 0031)

- The tenant of a request comes from its token alone (BR-002), never from a route, a query, a body or a header. The one
  other source is a sign-in, after it has verified the password.
- Every tenant-scoped query is filtered by the tenant and every save is guarded (ADR 0015); a query or a write that has
  no tenant fails. The one read across the filter is the sign-in's lookup of the user, with the tenant in its
  predicate. Another tenant's record answers 404, as an id nobody has (`docs/api.md` section 2); this is tested for
  every resource (`TenantIsolationApiTests` and the tests of each module).

## 4. Maker-checker (BR-603 to BR-606, ADR 0023, ADR 0032, ADR 0033)

- Posting a credit note, an adjustment, a refund, a bill at or above the threshold, and reversing a payment, an
  allocation or an application of credit need a second person. The checker's approval is the posting itself.
- So does a change of a document-number prefix (BR-802): a TenantAdmin asks, and the approval is the change. The
  approval threshold, the back-date window and the default due days are changed by a TenantAdmin alone, audited
  (BR-607).
- The checker is another user than the maker and holds Supervisor or TenantAdmin. Both are checked by the decision
  itself (`ApprovalRequest.Approve` and `Reject`), whatever the endpoint, and the role also by the policy of every
  approve and reject endpoint.

## 5. Secrets

`Database:ConnectionString`, `Jwt:SigningKey` and `Seq:ApiKey` come from user-secrets or the environment, never from a
file in git. The one key in git, the Development signing key, is refused outside Development. Changing the signing key
signs everybody out (`docs/runbook.md` section 9).

## 6. Data protection and the audit

- Phone numbers and e-mail addresses are masked before any log sink (ADR 0012); the request log leaves out query
  strings.
- Every change of a financial or security-relevant record writes an audit record in its transaction, with who, when,
  the correlation id and the record before and after (BR-701 to BR-703, ADR 0020). The grants that stop the
  application's login from changing or deleting audit records are Prompt 5.4.

## 7. Threat model: known gaps

| Threat | Today | Where it is planned |
|---|---|---|
| Guessing passwords | No rate limit or lockout of sign-ins; the slow hash limits the speed per guess | A follow-up of ADR 0031 (point 12) |
| A stolen token | Valid until it expires, at most 60 minutes; no revocation | Accepted for R1 (ADR 0031 point 8) |
| A disabled user or a removed role | Takes effect at the next sign-in | Accepted for R1 (ADR 0031 point 8, ADR 0032 point 8) |
| The application's database login changing what it must not | No grants yet; the guards of the EF layer refuse it | Prompt 5.4 |
| Transport and host headers | TLS, allowed hosts and the server header are not settled | Review finding 2.4 (CLAUDE.md section 9) |
