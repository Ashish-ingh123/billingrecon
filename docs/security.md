# Security

How BillingRecon protects patient and financial data, as built, with the decisions each part comes from, and the threat
model of R1 at the end. Written with Prompt 5.3 (2026-11-21), brought up to date with Prompts 5.3b and 5.4, and written
in full with Prompt 5.4b (2026-11-23, ADR 0035). Every row of the threat model is either done, accepted for R1 with
its reason, or named with the prompt that does it.

---

## 1. Authentication (ADR 0031)

- A user signs in at `POST /api/v1/auth/login` with the code of the hospital, the e-mail address and the password
  (`docs/api.md` section 8.12). Every refusal answers the same 401 `invalid_credentials` and costs one password
  verification, so the answer says nothing about which hospitals or addresses exist. The reason goes to the log,
  never the address or the password.
- The answer is a JWT the API issues and checks itself: HS256 with `Jwt:SigningKey` (256 bits or more), issuer
  `BillingRecon`, audience `BillingRecon.Api`, 60 minutes by the application's clock, with the user (`sub`), the
  tenant (`tenant_id`) and the roles (`role`). Only HS256 is accepted; a refused token gets a bare
  `WWW-Authenticate: Bearer`. No refresh token, no revocation: a token lives until it expires, also when its user is
  disabled or loses a role meanwhile.
- **Deny by default:** every endpoint needs a valid token but the sign-in, the health checks and, in Development, the
  OpenAPI document and the Scalar page. In Development a request without a token acts as the development user (a
  TenantAdmin); Development must never be the environment of a machine others can reach.
- Passwords (ADR 0030): PBKDF2-HMAC-SHA-256, 600,000 iterations, a salt of 16 bytes per password, 12 to 128
  characters. Neither a password nor its hash is ever in an answer, an audit image, a log line or the fingerprint of an
  idempotency key; the user POSTs and the sign-in take no Idempotency-Key for that reason.

## 2. Limits on signing in (ADR 0035)

Two limits, both with the rate limiter of ASP.NET Core, both on the sign-in alone:

| Limit | Partition | How much | Against |
|---|---|---|---|
| Per client address | The address the proxy that ended TLS saw (section 6), or the connection's | 30 sign-ins a minute, however they end | One source trying a common password on many accounts |
| Per account | The hospital's code and the address as typed, trimmed, in any case | 10 refused sign-ins in 15 minutes; then every sign-in of the account is refused until the window ends, the right password included; a success counts nothing | Many sources guessing one account's password |

Both answer **429 `too_many_sign_ins`**, the same for either limit, with `Retry-After` in seconds (60 or 900) and
`Cache-Control: no-store`. The account is what was typed, so an address nobody has is held exactly like a real one: the
limit tells nothing about which accounts exist. A warning in the log names the client address or the hospital's code,
never the e-mail address. The limits are fixed in code and kept in memory by each instance of the API (section 10,
rows S1 and S9).

## 3. Authorisation (ADR 0032)

The permission matrix of `docs/requirements.md` section 1 is applied as named policies, one per capability; every
action of the API names exactly one, and an architecture test keeps it so. A role a policy does not admit gets 403
`forbidden`. A blank cell of the matrix is no access, not even reading, with one exception: a Cashier reads credit
notes, to refund from one. The trail of a user in the audit is the TenantAdmin's alone, as the users are; so are the
trails of the tenant, of its prefix changes and of its number series (ADR 0033), and the settings themselves. The
dashboard is the TenantAdmin's, the Supervisor's and the Viewer's (US-44, ADR 0037); clerks and cashiers read the
same figures in the reports, which every role may. The table
of what each role may call is in `docs/api.md` section 2, and one table-driven test walks every endpoint with every
role against the database.

## 4. Tenant isolation (ADR 0009, ADR 0015, ADR 0031)

- The tenant of a request comes from its token alone (BR-002), never from a route, a query, a body or a header. The one
  other source is a sign-in, after it has verified the password.
- Every tenant-scoped query is filtered by the tenant and every save is guarded (ADR 0015); a query or a write that has
  no tenant fails. The one read across the filter is the sign-in's lookup of the user, with the tenant in its
  predicate. Another tenant's record answers 404, as an id nobody has (`docs/api.md` section 2); this is tested for
  every resource (`TenantIsolationApiTests` and the tests of each module).
- The database does not keep tenants apart: the application's login reads every row of the schema (section 10, row I2).

## 5. Maker-checker (BR-603 to BR-606, ADR 0023, ADR 0032, ADR 0033)

- Posting a credit note, an adjustment, a refund, a bill at or above the threshold, and reversing a payment, an
  allocation or an application of credit need a second person. The checker's approval is the posting itself.
- So does a change of a document-number prefix (BR-802): a TenantAdmin asks, and the approval is the change. The
  approval threshold, the back-date window and the default due days are changed by a TenantAdmin alone, audited
  (BR-607).
- The checker is another user than the maker and holds Supervisor or TenantAdmin. Both are checked by the decision
  itself (`ApprovalRequest.Approve` and `Reject`), whatever the endpoint, and the role also by the policy of every
  approve and reject endpoint.

## 6. Transport, hosts and browsers (ADR 0035)

- **TLS ends at a reverse proxy** in front of the API (an ingress, a load balancer, the nginx of Prompt 8.1). The API
  believes the proxy's `X-Forwarded-For` and `X-Forwarded-Proto` from the loopback addresses and from
  `ReverseProxy:KnownProxies` only, one proxy deep; from any other address it ignores them. The framework's switch
  `ASPNETCORE_FORWARDEDHEADERS_ENABLED`, which would believe anyone, stops the API at startup. How to set the proxy up
  is `docs/runbook.md` section 11.
- **HTTPS only, outside Development:** a request that did not arrive over HTTPS (directly, or as a known proxy says) is
  answered 400 `https_required` before anything of it is read. There is no redirect: by then the token or the password
  has crossed the network in clear. An answer over HTTPS carries `Strict-Transport-Security: max-age=31536000` (one
  year, this host only, not preloaded).
- **Allowed hosts:** a request for a host `AllowedHosts` does not list is answered 400 `host_not_allowed`. Outside
  Development the setting is required and `*` is refused at startup.
- **The two health endpoints** answer for any host and over plain HTTP, for probes from inside; they tell nothing but
  "Healthy" or "Unhealthy".
- **CORS:** a browser may call the API from the origins of `Cors:Origins` only (none by default; https outside
  Development), with the methods and headers the API takes, without credentials. The token travels in the
  `Authorization` header, never in a cookie, so cross-site request forgery has nothing to ride on.
- **What an answer tells:** no `Server` header; `X-Content-Type-Options: nosniff` on every response; on the answers of
  `/api`, `Cache-Control: no-store` (unless the action set its own) and
  `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`. Errors outside Development carry no internal
  detail (ADR 0011).
- **The frontend** (ADR 0038) keeps the token in `sessionStorage`: a refresh keeps the user signed in, closing the tab
  signs them out, and it is forgotten at its expiry and on any 401. It renders no raw HTML (a lint rule refuses
  `dangerouslySetInnerHTML`). Its one inline script, which sets the theme before the first paint, has the SHA-256
  `EiUJJFQJQCBNqB/aazghpxtqLJ7StNqlkar3PJPRG+g=`, pinned by a test; the content security policy of the frontend
  (Prompt 8.1) lists it as `script-src 'self' 'sha256-EiUJJFQJQCBNqB/aazghpxtqLJ7StNqlkar3PJPRG+g='`. A change of the
  script changes the hash, and the test fails until both are updated.

## 7. Secrets

`Database:ConnectionString`, `Jwt:SigningKey` and `Seq:ApiKey` come from user-secrets or the environment, never from a
file in git. The one key in git, the Development signing key, is refused outside Development. Changing the signing key
signs everybody out (`docs/runbook.md` section 9). The database logins' passwords are made per environment and kept in
the deployment's secret store (`docs/runbook.md` section 10).

## 8. Data protection and the audit

- Phone numbers and e-mail addresses are masked before any log sink (ADR 0012); the request log leaves out query
  strings; no log line carries a password, a token or the address a sign-in typed.
- Every change of a financial or security-relevant record writes an audit record in its transaction, with who, when,
  the correlation id and the record before and after (BR-701 to BR-703, ADR 0020). The images hold the patient data of
  the record (section 10, row I9).

## 9. The database logins (ADR 0034)

- The API connects as `billingrecon_app`, a member of the database role `billingrecon_application` alone; the
  migrations run as `billingrecon_migrator` (`db_ddladmin`, `db_datareader`, `db_datawriter`, `db_securityadmin`).
  Neither is `db_owner` nor can create a database. The logins and their passwords are made per environment
  (`docs/runbook.md` section 10), never in git.
- The role reads, adds and changes the tables of `dbo`. It cannot change or delete a ledger row or an audit record
  (`DENY`), deletes only the four kinds of row that are not financial records (lines of a draft, roles of a user, the
  key of a failed request), and only reads the roles and the migrations history (`docs/database.md` section 7). So a
  statement that goes around the guards of the context, through raw SQL, a tool or a leaked connection string, is
  still refused at the database (error 229).
- Every database test runs as such a login, and a table-driven test compares each table's grants with the markers of
  its entities.

## 10. Threat model (R1)

**What is protected:** patients' identities and contact data; bills, payments and the other financial documents; the
receivables ledger and its cached balances, whose correctness is the product's promise; the audit trail; the users'
passwords, their tokens and the signing key.

**Who threatens it:** anyone on the internet who can reach the proxy; a signed-in user of one hospital, curious about
another or reaching beyond their role; a dishonest or careless insider of a hospital (including a TenantAdmin); someone
who obtains a token, a password or the application's connection string; a compromised dependency.

**What R1 assumes:** the API runs behind a proxy that ends TLS, on a private network; Development is never the
environment of a machine others can reach; the secrets of section 7 are kept in a secret store; one instance of the API
and one SQL Server.

State: **Done** (built and tested, with its decision), **Accepted** (a known residual risk of R1, accepted by the owner
with the plan of Prompt 5.4b on 2026-11-23 or earlier where named, with the reason), or the **prompt** that deals with
it.

### Spoofing

| # | Threat | What stops it | State |
|---|---|---|---|
| S1 | Guessing a user's password through the sign-in | The slow hash; one answer for every refusal; the limit per account (10 refusals, then held 15 minutes) and per address (section 2) | Done (ADR 0031, ADR 0035). The limits live in memory per instance and a restart forgets them: Accepted, R1 runs one instance |
| S2 | Password spraying: one common password tried on many accounts | The limit per client address (30 a minute); passwords of 12 characters or more | Done (ADR 0035). From many addresses at once only the per-account limit applies: Accepted |
| S3 | Forging a token | HS256 with a key of 256 bits or more from the secret store; only HS256; issuer, audience and lifetime checked against the application's clock | Done (ADR 0031) |
| S4 | A stolen token used by someone else | Valid 60 minutes at most | Accepted for R1: no revocation (ADR 0031 point 8) |
| S5 | A token stolen from the browser by a script (XSS) | The token in `sessionStorage`, gone with the tab and after 60 minutes; React's escaping and a lint rule against raw HTML; the frontend's content security policy, which lists the inline theme script by its SHA-256 (section 6) | Where the token is kept: Done (ADR 0038 point 9). A script that runs in the page can still use the token while the page is open, wherever it is kept: Accepted, the defence is keeping scripts out. The policy: Prompt 8.1 |
| S6 | The development key or the development identity used in a deployment | The API refuses the key of the repository outside Development; the development identity exists only in Development | Done (ADR 0031). Keeping Development off reachable machines is operational: Accepted |
| S7 | A forged client address or scheme in forwarded headers | Believed from the loopback and `ReverseProxy:KnownProxies` only, one proxy deep; the framework's switch that believes anyone stops the API at startup | Done (ADR 0035) |
| S8 | Learning which hospitals or addresses exist | One 401 for every refusal, at the same cost; the per-account limit holds an address nobody has exactly like a real one | Done (ADR 0031, ADR 0035) |
| S9 | Keeping a known user out by guessing wrong on purpose | The hold lasts 15 minutes and ends by itself; the limit per address slows one source | Accepted for R1: the trade-off of the per-account limit (ADR 0035 point 3) |

### Tampering

| # | Threat | What stops it | State |
|---|---|---|---|
| T1 | Changing a posted document or the ledger through the API | Posted records are immutable; corrections are new documents; sensitive ones need a second person | Done (CLAUDE.md section 4, rules 2, 3 and 9) |
| T2 | Deleting financial rows or changing the ledger and the audit trail around the application (raw SQL, a tool, a leaked connection string) | The application's database login has no DELETE on financial tables and no UPDATE or DELETE on the ledger and the audit trail | Done (ADR 0034). It may UPDATE the other tables, which the application needs; a changed cached balance is found by the reconciliation (rule 4): Accepted |
| T3 | Writing another tenant's rows | The write guard of the context, the tenant in the WHERE clause of every UPDATE and DELETE | Done (ADR 0015) |
| T4 | Sending fields the endpoint does not take (over-posting) | Unknown JSON members refused; explicit mapping to commands | Done (ADR 0018) |
| T5 | SQL injection | Entity Framework parameterises; raw SQL is banned (RS0030) but for six constant, parameterised statements in four files a test lists, each with the tenant in its predicate | Done (ADR 0015, ADR 0021, ADR 0033, ADR 0037) |
| T6 | A financial POST replayed or sent twice | `Idempotency-Key`: a replay gets the first answer, a different body with the same key 422 | Done (ADR 0006) |
| T7 | Two changes of one record racing (a lost update) | Row versions and `If-Match` | Done (ADR 0007) |
| T8 | A forged `Host` header poisoning links the API writes (`Location`) | `AllowedHosts`, required outside Development | Done (ADR 0035) |
| T9 | A compromised or vulnerable package | NuGet audit on restore (any advisory stops it), the CI gate on High and Critical advisories, Dependabot, central package versions | Done for direct packages. No lock files (review finding 2.5): Accepted for R1 |

### Repudiation

| # | Threat | What stops it | State |
|---|---|---|---|
| R1 | A user denies a change they made | An append-only audit record per change, with the user, the instant, the correlation id and both images, in the transaction of the change; `DENY UPDATE, DELETE` at the database | Done (ADR 0020, ADR 0034) |
| R2 | A maker approves their own request | Maker and checker differ, checked by the decision and a CHECK constraint; the checker holds Supervisor or TenantAdmin | Done (ADR 0023, ADR 0032) |
| R3 | A TenantAdmin creates a second user and approves their own requests with it | The audit trail shows who created the user and who approved | Accepted for R1: shown, not prevented; a hospital's TenantAdmin is trusted with its users |

### Information disclosure

| # | Threat | What stops it | State |
|---|---|---|---|
| I1 | Reading another tenant's records through the API | The tenant filter of every query; another tenant's record answers 404 | Done (ADR 0009, ADR 0015) |
| I2 | Reading another tenant's rows with the application's database login (raw SQL, a leaked connection string) | Nothing at the database: the role reads every table. The application's own raw SQL names the tenant | Accepted for R1: isolation is a query filter, not row-level security (ADR 0009); the connection string is a secret (section 7) |
| I3 | Reading traffic between the client and the proxy | TLS at the proxy; the API answers HTTPS only and sends HSTS | Done (ADR 0035) |
| I4 | Reading traffic between the proxy and the API | The proxy and the API on one host or a private network | Accepted for R1 |
| I5 | Reading traffic between the API and SQL Server | `Encrypt=True` in the connection string of `docs/runbook.md` section 10 | Accepted for R1: required by the runbook, not checked at startup |
| I6 | Internals in an error answer | ProblemDetails without exception details outside Development | Done (ADR 0011) |
| I7 | Patient data or secrets in the logs | Phones and e-mail addresses masked; no query strings; no password, token or typed sign-in address | Done (ADR 0012, ADR 0031, ADR 0035). The sign-in limit's warning names the client's IP address, for security monitoring: Accepted |
| I8 | Patient data kept by a browser or a proxy cache | `Cache-Control: no-store` on every answer of `/api` | Done (ADR 0035) |
| I9 | Patient data kept for ever in the audit images | The audit trail is never changed or deleted | Accepted for R1: a retention or erasure rule is R5 (CLAUDE.md section 9, item 14) |
| I10 | Telling a scanner which server to attack | No `Server` header | Done (ADR 0035) |
| I11 | A page on another site reading the API in a user's browser | No CORS permission but for `Cors:Origins`; no credentials; the token is never a cookie, so there is nothing for a forged request to carry | Done (ADR 0035) |
| I12 | Secrets in the repository | User-secrets and the environment; the Development key refused elsewhere | Done (section 7) |
| I13 | Stolen backups | Backups and their protection | Prompt 8.3 (backup and restore in `docs/runbook.md`) |

### Denial of service

| # | Threat | What stops it | State |
|---|---|---|---|
| D1 | A flood of requests | No limit in the API but the sign-in's; the proxy's | Accepted for R1: the proxy's job |
| D2 | Large or slow requests | Kestrel's defaults (30 MB a body, a minimum data rate) | Accepted for R1 |
| D3 | Wearing the server out through the slow password hash | The limit per client address on the sign-in | Done (ADR 0035). From many addresses at once: Accepted, with D1 |
| D4 | Keeping a user out | See S9 | Accepted |

### Elevation of privilege

| # | Threat | What stops it | State |
|---|---|---|---|
| E1 | Doing what one's role does not allow | One policy per action, deny by default, a test of every endpoint with every role | Done (ADR 0031, ADR 0032) |
| E2 | A disabled user, or one who lost a role, keeps acting | Their token expires within 60 minutes | Accepted for R1 (ADR 0031 point 8, ADR 0032 point 8) |
| E3 | A tenant left without an administrator | The last active TenantAdmin cannot be disabled or demoted; nobody disables themself | Done (ADR 0030) |
| E4 | A password reset by an administrator who then knows it | The reset is audited | Accepted for R1: no forced change at the next sign-in (ADR 0030) |
| E5 | The application's database login doing what only the migrator may | It holds the application's role alone, not `db_owner`, not `db_ddladmin` | Done (ADR 0034) |
| E6 | Framing a page of the frontend to trick a user into a click | The API's answers refuse every frame; the frontend's pages need their own header | Done for the API (ADR 0035); Prompt 8.1 for the frontend |
