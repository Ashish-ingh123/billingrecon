# BillingRecon build history: what was decided for each built prompt

Moved here on 2026-11-26 (Prompt 10.0-close) from `docs/build-prompts.md`, where each note stood under its prompt.
Nothing was reworded: every note is as it was written on its day, so a date, a branch or a migration number in it is
that day's, and a later note or ADR may have changed it. The prompts themselves, their order and the status table stay in
`docs/build-prompts.md`; the decisions that became rules are in the ADRs (`docs/adr/`) and in CLAUDE.md. An ADR or a
commit message that cites `docs/build-prompts.md` for what was decided for a prompt means this file.

The headings are those of `docs/build-prompts.md`, Part B. A prompt with no note has no heading here.

### Phase 2. Reference data and bill drafts (branch `phase-2-billing`)

#### 2.4 Bill drafts

Merge `phase-2-billing` to `main` when 2.4 is green.

### Phase 3. The posting engine (branch `phase-3-posting`)

#### 3.3 Receivables ledger

**Owner's decisions for 3.3, recorded 2026-11-20 (give 3.3 alone, after 2.2 to 3.4 are committed; verification is deferred, see `docs/runbook.md` section 8):**

**Settled by the owner on 2026-11-20 and built:** the port (not a subscriber) opens the `PayerBalances` row; the
foreign keys to `Payers` come from a typed-id convention of the context, with no new module exception; the ledger
and the reconciliation are two more sanctioned raw-SQL files; `PayerBalances` has no `LastPostingId`;
`PostedByUserId` is nullable with no key until `Users` exists. ADR 0021 and the built note of ADR 0005 record them.
The list below is kept as the record of what was asked.

- The plan shown for 3.3 must include, besides what the prompt says:
  1. **Renumber every later migration**, here and in `docs/database.md` section 8: `0008_Receivables`,
     `0009_BillPosting` (3.5, which needs columns on `Bills`), `0010_Approvals`, `0011_Payments`, `0012_CreditNotes`,
     `0013_Adjustments`, `0014_Refunds`, `0015_Users`.
  2. **How the zero `PayerBalances` row is created.** The owner prefers a Receivables subscriber to the
     payer-created event over Payers calling a Receivables port, unless the plan shows why the port is better. The
     case for the port, to be put to the owner in that plan: a subscriber in Receivables has to name
     `Domain.Payers.PayerCreated`, which is a new module-dependency exception, and it contradicts
     `docs/domain-model.md` section 8 and ADR 0020 point 7 ("Audit is the only subscriber", with its architecture
     test); the port needs no exception, because Payers may call a public Application contract of Receivables.
     The owner decides.
  3. **No new module-dependency exception without the owner's approval.** The three Receivables tables have foreign
     keys to `Payers`; declaring them the way `BillConfiguration` does needs an exception
     (`Infrastructure.Receivables` to `Domain.Payers`). The plan must either ask for it or propose the typed-id
     foreign key convention that ADR 0003 (amendment of 2026-11-20) said to weigh again at this point.
  4. **The data step of `0008`** is `INSERT ... SELECT ... FROM Payers WHERE NOT EXISTS (...)`, the tenant taken
     from `Payers`. It is added by hand to the scaffolded migration; that exception to "never edit a migration by
     hand" is recorded in an ADR, and a test fails with the line to paste when it is missing.
- Also to be asked in that plan, because both go against a rule of CLAUDE.md section 2: the atomic change of
  `PayerBalances` and the reconciliation query of `docs/database.md` section 5.6 cannot be written without raw
  SQL (Entity Framework has no lock hints and no "add to the column" without the banned `ExecuteUpdate`, and the
  reconciliation joins tables of several modules). Each would be one more sanctioned raw-SQL file.
- A first draft of 3.3 and 3.5 was written on 2026-11-20 before these decisions and taken out of the repository
  again. It is parked in `C:\aptran2026\BillingRecon\parked-3.3-3.5-draft` (outside the repository) as reference
  only: it uses the port, an unasked exception, and raw SQL without the explicit lock read, so it does not match
  the decisions above.

#### 3.5 Post a bill

**Owner's decisions for 3.5, recorded 2026-11-20 (give 3.5 in a later session, after 3.3 is committed):**

**Settled by the owner on 2026-11-20 and built (ADR 0022):** BR-401 and identity 2 are amended for a bill that comes
to nothing; a second posting gets 409 `concurrency_conflict` for a stale version and 409 `bill_already_posted` for
the current one, and a posted bill answers `bill_already_posted` to an edit or a void as well. The list below is kept
as the record of what was asked.

- **Accepted:** a draft without lines is refused with 422; the payer must be active at posting (BR-105); the
  patient is not re-checked (BR-102 only stops new bills).
- **Zero-total bill:** numbered, audited, status `Settled`, no ledger posting. **Open, to settle with the owner
  before coding:** read literally, BR-401 says otherwise ("a posted document without ledger entries ... is a
  defect"), and so does identity 2 of `docs/database.md` section 5.5, while `CK_LedgerEntries_OneSide` forbids an
  entry of zero. Recommended: amend BR-401 and identity 2 to except a document whose total is zero, and have the
  reconciliation skip such bills; the alternative is to allow zero entries, which weakens the constraint.
- **Lock order, to be written as an ADR:** the number series first; then an explicit `UPDLOCK` read of the
  `PayerBalances` row; both before `SaveChanges`. The ADR states where the bill row falls: it is read without a
  lock at the start and written by the save, after both locks, under its row version.
- **Three separate tests, each with its own error code:**
  - the same key while the first request is in flight: 409 `idempotency_in_progress`;
  - two different keys at once: one succeeds, the other gets 409 `concurrency_conflict` from the row version check;
  - a different key after the commit. Proposed: with the old version in `If-Match`, 409 `concurrency_conflict`
    (the API answers a stale version with 409 everywhere, CLAUDE.md section 4 rule 6, and has no 412); with the
    current version, 409 `bill_already_posted`, a new code for a bill that is Posted, PartiallyPaid or Settled,
    instead of the general 422 `bill_not_draft`, which stays for a voided bill. The owner confirms.
- **More tests:** a posted bill refuses edit and void; a tenant whose threshold is null needs no approval.
- **BR-207:** until 3.6, a bill at or above the threshold is refused with 422 `bill_requires_approval`. That is
  temporary and has an item in CLAUDE.md section 9 for 3.6 to remove.
- **Migration:** `0009_BillPosting` (number, fiscal year, sequence number, posted stamps, the later statuses,
  `CK_Bills_NumberIffPosted`, `CK_Bills_StatusMatchesBalance`, the two filtered unique indexes).

#### 3.6 Maker-checker approvals

Merge `phase-3-posting` to `main`.

### Phase 4. Payments and corrections (branch `phase-4-payments`)

#### 4.1 Record a payment

**Owner's decisions for 4.1, recorded 2026-11-20:** the ledger port answers the id of the posting and the payment
stores it; the `Payments` table holds only what recording needs (reversal columns and states with 4.5); an amount
is at most 9,999,999,999.99. Built on `phase-2-billing`, not on a new branch, like everything since 2.2.

#### 4.2 Allocate a payment to bills

**How 4.2 was built, recorded 2026-11-21 from the code (ADR 0024, which the owner still confirms):** Payments
reaches a bill through `IBillSettlement`, a contract of Billing, with no new module-dependency exception; the lock
order is that of ADR 0022 without a number (checks first, bills in the order of their ids, then the payer's row of
`PayerBalances` under the ledger's lock, then the save under the row versions of the payment and of every bill); the
loser of the last rupee gets 409 `concurrency_conflict`, whether its save or the ledger notices; the ledger now
refuses a balance below zero instead of failing a CHECK constraint; one request names at most 50 bills, each once;
a deactivated payer's payment can still be allocated. The table is its own migration, **`0012_PaymentAllocations`**,
so the later migrations move up by one: `0013_CreditNotes`, `0014_Adjustments`, `0015_Refunds`, `0016_Users`. Built
on `phase-2-billing`, like everything since 2.2.

#### 4.3 Credit notes

**Owner's decisions for 4.3, recorded 2026-11-21 (the plan was approved "as recommended"; ADR 0025):** the caps of
BR-210 and BR-211 are checked before any lock (422) and again under the payer's ledger lock (409
`concurrency_conflict`), with no new column on `Bills`; a credit note that comes to nothing is refused at `post`
(422); the payer need not be active, and the date is checked again at approval; `CreditNoteAllocations` has no
reversal columns and the status `Active` alone; a line may name a line of the bill and takes its defaults from it;
applying available credit to another bill needs no second person. Built in the same session as the documents of 4.2,
at the owner's word, after 4.2 was committed. **Still open, asked in the plan and not answered:** no remaining
prompt reverses an application of credit (4.5 names payment allocations and payments only); add it to 4.5 or leave it
out of R1.

#### 4.4 Adjustments

**Owner's decisions for 4.4, recorded 2026-11-21 (the plan was approved "as recommended"; ADR 0026):** open
question 1 of the domain model is answered **no**: a rounding under one rupee is not exempt from maker-checker in
R1; the cap against the balance due is asked when the maker submits and again at approval; the adjustment date is
not before the bill date, which BR-501 now says; the payer need not be active; a posted adjustment is not reversed.
Built without a new module exception: Adjustments reaches the bill through `IBillSettlement` of Billing.

#### 4.5 Reversals

**Owner's decisions for 4.5, recorded 2026-11-21 (the plan was approved "as recommended"; ADR 0027):** reversing an
application of credit is its own prompt, 4.5b below; the check "refused when a refund exists" is built with refunds
(4.6), and until then the aggregate throws if part of a payment went anywhere but to allocations; a payment cannot
be asked for reversal while one of its allocations waits for a decision, nor the other way round (409); a reversal
takes no document number, and its mirror posting names the posting it reverses; the reason is required on the
request, kept by the approval request, and copied to the row when the checker approves. The columns are migration
**`0015_PaymentReversals`**, so refunds and users move to `0016_Refunds` and `0017_Users`.

#### 4.5b Reverse an application of credit

**Owner's decisions for 4.5b, recorded 2026-11-21 (the plan was approved "as recommended"; ADR 0028):** only a
later application, made through `POST /credit-notes/{id}/applications`, can be reversed; the application a credit
note makes to its own bill at posting cannot (422 `credit_application_made_at_posting`), and the row says which one
it is in a stored flag, `AppliedAtPosting`; one decision at a time for each application (409), while new
applications from the same credit note stay allowed; no document number, and the reason is required on the
request, kept by the approval request and copied to the row at approval; migrations `0003` to `0015` were committed
on their own first (commit `740fb31`), so that **`0016_CreditApplicationReversals`** was added the ordinary way.
Refunds and users move to `0017_Refunds` and `0018_Users`.

#### 4.6 Refunds

**Owner's decisions for 4.6, recorded 2026-11-21 (the plan was approved "as recommended"; ADR 0029):** the BR-306
check is asked when the reversal of a payment is requested, through the refund repository (422 `payment_refunded`),
with no cached refunded amount on `Payments`; the cap and the source's state are asked when the maker submits and
again at the approval, and the payer must be active at the draft and at the approval; the refund date is not before
the date of its payment or credit note, a sentence BR-307 gains (422 `refund_date_before_source_date`); a payment
that waits for its reversal, or was reversed, refunds nothing (409), while a refund that only waits does not stop a
reversal from being asked for; the payment and the credit note record `Refunded` in their own audit trails. The table
is migration **`0017_Refunds`**; users move to `0018_Users`.

Merge `phase-4-payments` to `main`.

### Phase 5. Identity and security (branch `phase-5-identity`)

#### 5.1 Users and roles

**Owner's decisions for 5.1, recorded 2026-11-21 (the plan was approved "as recommended"; ADR 0030):** every column
that names a user becomes a foreign key to `Users`, not only the creation and update stamps; the development database
is disposable and is dropped before `0018` (no data step); passwords are PBKDF2-HMAC-SHA-256 with 600,000 iterations,
a 16-byte salt and a 32-byte hash, 12 to 128 characters without rules of composition, and an administrator's reset
sets a given password with no forced change; a user holds at least one role, the tenant's last active administrator
keeps the role and cannot be disabled, and nobody disables themselves; no role check before 5.2 and 5.3; the
development user is a TenantAdmin with a password nothing matches. Settled while building, and confirmed by the owner
with the plan of 5.2: the user POSTs take no Idempotency-Key, because the stored fingerprint of a body with a password
would be a fast hash of the password (ADR 0030 point 7).

#### 5.2 Sign-in and JWT

**Owner's decisions for 5.2, recorded 2026-11-21 (the plan was approved "as recommended"; ADR 0031):** the package
`Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.12 in the Api project; HS256 with a key of at least 256 bits from
`Jwt:SigningKey`, a key for Development in `appsettings.Development.json` that is refused anywhere else; every refused
sign-in answers the same 401 `invalid_credentials` (unknown or suspended tenant, unknown address, wrong password,
disabled user), with the reason in the log only; the sign-in is recorded (`LastSignInAtUtc` and a `SignedIn` audit
record), the sign-in request acting as the user it has verified through the `ISignInScope` port; a token stays valid
for its 60 minutes after its user is disabled; in Development a request without a token acts as the development
user; the tests use tokens minted by the API's own issuer for every request; the sign-in takes no Idempotency-Key, and
the owner confirmed ADR 0030 point 7 for the user POSTs with it. Rate limiting of sign-ins is a follow-up.

#### 5.3 Role permissions and tenant settings

**Owner's decisions for 5.3, recorded 2026-11-21 (the plan was approved "as recommended"):** the prompt is built in two
steps, each with its own commit: **5.3, the role permissions (ADR 0032)**, then **5.3b, the tenant settings**. For the
permissions: a blank cell of the matrix is no access, not even reading, except that a Cashier reads credit notes (a
refund may come from one); applying a credit note's credit is in the credit notes' row (TenantAdmin, Supervisor,
BillingClerk); the checker's inbox `GET /approval-requests` is for TenantAdmin and Supervisor; the audit trail is read
by every role, but the trail of a user by a TenantAdmin only; the checker's role is checked by the policy of every
approve and reject endpoint and again by the decision itself, with 403 `approval_checker_role`. For 5.3b: a prefix
change is one maker-checker request for one document type, kept in a new table (`PrefixChanges`), whose approval
changes the tenant's default prefix and the prefix of that type's series of the current fiscal year, so the next number
uses it and numbers already issued keep theirs; the five document types keep distinct prefixes.

#### 5.3b Tenant settings and prefix changes

**Owner's decisions for 5.3b, recorded 2026-11-21 (the plan was approved "as recommended"; ADR 0033):** the checkers
(TenantAdmin and Supervisor) read the prefix changes, which also reach their inbox; the approval request's unused
document type `NumberSeries` is renamed `PrefixChange`; a prefix belongs to one document type for good (never another
type's default, nor a prefix another type's series or changes ever had); the trails of the tenant, of its prefix
changes and of its number series are the TenantAdmin's alone. No new raw SQL. Built as decided; the build added no
Idempotency-Key on the prefix-change posts and answers the request with 201 (ADR 0033 built note). **Found while
building, closed 2026-11-23:** a first number of a type in a fiscal year, taken in the milliseconds of an approval,
kept the old prefix for that year (ADR 0033 built note point 5). The owner chose option B with the plan of 5.4: the
approval holds the series with the allocator's locked read alone (one more statement in the allocator's file, no new
sanctioned file), built and committed on its own before 5.4 (ADR 0033 "Amended 2026-11-23"; CLAUDE.md section 9,
item 31).

#### 5.4 Database logins and hardening

**Owner's decisions for 5.4, recorded 2026-11-23 (the plan was approved "as recommended"):** the prompt is built in two
steps, each with its own commit, after the fix of ADR 0033 built note point 5 is committed: **5.4, the database logins
and grants (ADR 0034)**, then **5.4b, the HTTP hardening and `docs/security.md` in full**. For 5.4:
- the grants are in a migration, **`0020_DatabaseRoles`**, scaffolded empty and filled by hand with
  `migrationBuilder.Sql`, idempotent, with a `Down()`: it creates the database role `billingrecon_application`, so the
  grants exist wherever the migrations run; the logins, their passwords and their database users are made per
  environment by a script in the runbook (a new section 10), never in git;
- the grants are schema-wide with exceptions taken from the model's markers: `SELECT, INSERT, UPDATE` on the schema
  `dbo`; `DENY UPDATE, DELETE` on the append-only tables (`LedgerPostings`, `LedgerEntries`, `AuditRecords`);
  `DELETE` only on the tables of the `IHardDeletable` entities (`BillLines`, `CreditNoteLines`, `UserRoles`,
  `IdempotencyKeys`: four, where `docs/database.md` section 7 lists two, which is corrected); `Roles` and
  `__EFMigrationsHistory` read-only. A table-driven test compares every table's effective permissions with its markers,
  so a future append-only table without its `DENY` fails it with the line to add;
- the migrator login holds `db_ddladmin`, `db_datareader`, `db_datawriter` and `db_securityadmin` (it creates the role
  and grants), where section 7 named two roles;
- the database tests run as the application login wherever production code meets the migrated test database (the API
  host and the contexts of the production code); Respawn, the migration, the catalogue checks, the probe database and
  the Development and Pending databases stay on the administrator's connection. The test login has a random password
  per run. LocalDB 2019 on the office machine runs in mixed mode, so this is pre-checked there.

#### 5.4b HTTP hardening and the security document

**Owner's decisions for 5.4b, recorded 2026-11-23 (the plan was approved "as recommended"; ADR 0035):** TLS ends at a
reverse proxy in front of the API; forwarded headers are believed from the loopback addresses and
`ReverseProxy:KnownProxies` only, one proxy deep; no HTTPS redirection: outside Development a request that did not
arrive over HTTPS is answered 400 `https_required`, the two health endpoints excepted; HSTS for one year, this host
only, not preloaded. `AllowedHosts` checked by the API's own middleware (400 `host_not_allowed`), required and without
`*` outside Development. Two sign-in limits, both 429 `too_many_sign_ins` with `Retry-After`: 30 a minute per client
address, and an account held 15 minutes after 10 refusals (the right password included; a success counts nothing),
fixed in code, in memory per instance. CORS from `Cors:Origins` (none by default, `http://localhost:5173` in
Development, https elsewhere), no credentials. No `Server` header; `nosniff` on every response and `no-store` with a
content security policy on the answers of `/api`. The threat model's accepted risks are the rows marked so in
`docs/security.md` section 10; where the browser keeps the token goes to 7.1, the frontend's own headers and TLS at the
local demo's nginx to 8.1, each prompt below gaining a sentence. Built as decided (ADR 0035 built note).

Merge `phase-5-identity` to `main`.

### Phase 6. Reporting (branch `phase-6-reporting`)

#### 6.1 Registers and balances

**Owner's decisions for 6.1, recorded 2026-11-23 (the plan was approved "as recommended"; ADR 0036):** Reporting's
Infrastructure reads the tables of every business module through five listed exceptions to the module rule (ADR 0003,
amendment of 2026-11-23), and an architecture test keeps Reporting from writing. The registers list documents and their
totals are held against the ledger; the balances are the cached balances (BR-404); the daily collection is read from
the Cash entries of the business date (received, reversed under the payment's method, refunded under the refund's
method, net). Eight GET endpoints under `/api/v1/reports`; a paged report answers its totals over every page and, for a
register, its period (both ends or neither, at most 366 days, the current IST month by default). A new policy `Reports`
for all five roles. The work stays on `phase-2-billing`. Built as decided; the build added three words of Reporting's
own for the Billing and Payments enums, which its Application and Api layers may not name (ADR 0036 point 7), and sums
the totals in memory, since the database cannot sum a converted `Money` (built note).

#### 6.2 Reconciliation and audit trail

**Owner's decisions for 6.2, recorded 2026-11-23 (the plan was approved "as recommended"; ADR 0036, "Amended
2026-11-23 (Prompt 6.2)"):** the reconciliation report is `GET /api/v1/reports/reconciliation` under `Reports`, through
the Receivables module's contract, paged with its counts per identity, and logged as an error when it finds drift;
gapless numbering joins the reconciliation as identity 11, `NumberSeriesGap`, in the statement already sanctioned; the
zero-drift helper is automatic, the reconciliation of every tenant after every database test, with an explicit, named
opt-out for the tests that leave a defect on purpose; US-43 is proven for a bill read by a Viewer; `docs/api.md` is
completed with every endpoint and an error-code catalogue, kept complete by two architecture tests. Built as decided;
the first run with the automatic check found the 41 tests that seed defects or take numbers without documents, and none
that leaves drift by mistake. Found: the decimals of a creation's audit image differ in scale from later images (ADR
0036 amendment).

#### 6.3 Dashboard figures

**Owner's decisions for 6.3, recorded 2026-11-23 (the proposal was approved "as recommended"; ADR 0037):** the
requirements gain US-44 (a Supervisor, a TenantAdmin or a Viewer sees the dashboard of a period against the previous one,
with the prompt's tests as its acceptance criteria), BR-902 (the period, the comparison and what each figure is) and the
matrix's row "Dashboard" for TenantAdmin, Supervisor and Viewer; **no trend for the drift card** (its count of now, no
snapshot table, no migration); the figures of past days are summed from the ledger by business date in SQL, through
`ILedgerMovements` in Receivables and a sixth sanctioned raw-SQL file, `LedgerMovementsQuery`; collected is gross of
reversals and refunds; seven GET endpoints, one per panel, the four lists calling the handlers of the 6.1 and 6.2
reports; the payment register gains `order=NewestFirst` for the recent payments; the policy `Dashboard`; no package.
Built as decided. Found: Entity Framework cannot sum `Money` behind its converter (a probe failed), which is why the SQL;
`ReportingConventionTests` refuses `.Add(` in Reporting, so the series are filled by index (the test was not loosened).
The merge to `main` is the owner's step; nothing has been merged.

### Phase 7. Frontend (branch `phase-7-frontend`)

#### 7.1 Scaffold and sign-in

**Owner's decisions for 7.1, recorded 2026-11-23 (the proposal was approved "as recommended"; ADR 0038):** React 19,
TypeScript 5.9 (the lint and the type generator accept nothing later yet), Vite 8; React Router 8 in data mode; TanStack
Query; a client typed from the API's OpenAPI document (openapi-typescript, openapi-fetch), committed as a snapshot that
`OpenApiSnapshotTests` keeps equal to the API's and rewrites on request; React Hook Form without a schema library; our
own components on CSS Modules and the tokens, no UI kit; Recharts named for 7.6; the token in `sessionStorage`; ESLint 9
with the accessibility rules, Prettier as the format gate; the branch stays `phase-2-billing`; 27 npm packages pinned
exactly, resolved as of two weeks before. Built as decided. Found: the OpenAPI document gave enums as numbers, because the
generator reads the minimal-API JSON options, so `Program.cs` gives them the enum converter too; it gives amounts as
`number | string`, which is true of what the API reads and is left for the owner; one advisory in a lint tool's tree was
fixed by its next patch. No screen of users and settings is planned in any prompt; the TenantAdmin's section says so.

#### 7.2 Patients, payers and catalogue screens

**Owner's decisions for 7.2, recorded 2026-11-23 (the plan was approved "as recommended"; ADR 0039):** lists keep their
search, status and page in the address; every change sends `If-Match` from the record's `version`; who may change is a
copy of the API's policies, held to `Policies.cs` by a test; deactivating asks once more, in place; decimals are typed as
text and sent as JSON numbers, refused beyond 15 significant digits; **payers have a section of their own**, created from
the patient's page or as guarantors from the list; **a conflict says nothing was saved, lists what changed, and offers
"Load the latest version" or "Keep my changes"**. Built as decided, with no package. Found: the OpenAPI snapshot of 7.1
held Windows line breaks inside its descriptions and would have failed on the Linux runners; the test now writes every
line break as LF.

#### 7.3 Bill screens

**Owner's decisions for 7.3, recorded 2026-11-23 (the plan was approved "as recommended"; ADR 0040):** **one editor for a
draft**, its header and lines saved as a `PATCH` then a `PUT` with the version the first answered, each only when it
changed; **the key of an operation in memory**, sent again with the same request after no answer, a fault or "still
running", dropped after a success or a refusal; **live totals by exact arithmetic written on `BigInt`**, with no package;
**names in the bill list read per patient and payer** and cached, the API unchanged. Approve and reject stay with 7.5; a
bill waiting for approval says so. Built as decided. Found: the browser's and the server's line arithmetic are now held
to one file of cases (`frontend/src/bills/lineCases.json`) by a test on each side; the parties of a new draft are asked
for together with its fields.

#### 7.4 Cashier screens

**Owner's decisions for 7.4, recorded 2026-11-23 (the plan was approved "as recommended"; ADR 0041):** **allocating lists
the payer's bills that owe something, oldest first**, with "Apply oldest first" filling them from the unapplied cash and
the caps checked in exact paise before sending; **a refund starts from its source**, a payment's page or the payer
statement's lists, with no free-standing new refund; **the payer statement is a page of its own**, linked from the
payer's page. Approving and rejecting reversals and refunds stay with 7.5; a document waiting for a decision says so.
Built as decided, with no package; one hook, `useDocumentAction`, carries every financial action on a document.

#### 7.5 Approvals and reports

**Owner's decisions for 7.5, recorded 2026-11-23 (the plan was approved "as recommended"; ADR 0042):** **the prompt is
split**: 7.5 builds the inbox, the credit notes and the adjustments and is done with the three-user scenario, and the
reports are **7.5b** below; **the pending approvals report answers `parentDocumentId` and `makerComment`**, a change of
the API, so that an allocation or an application of credit is decided through its payment or credit note and the checker
reads the maker's reason; **decisions are made in the inbox**, a request reviewed in place, the maker's own request
without a review; **a credit note's lines come from its bill**, by quantity at the bill line's price and tax, with free-text
lines beside them. Built as decided: the allocation form of 7.4 is shared with the application of credit
(`ApplyToBills`), and credit notes and adjustments are drafted from the posted bill they correct.

#### 7.5b Reports

**Owner's decisions for 7.5b, recorded 2026-11-23 (the plan was approved "as recommended"; ADR 0043):** **the CSV is
written in the browser** from every page of the filter and held to the server's totals (read once more if they differ,
refused if they differ again; at most 10,000 rows; UTF-8 with a BOM, plain decimals, quoted fields, the formula guard);
**the six reports are eight screens**; **`/reports` is an index**, each screen's filters in its address and its totals
the server's. Built as decided, with no package and no change of the API.

#### 7.6 Dashboard

**Owner's decisions for 7.6, recorded 2026-11-23 (the plan was approved "as recommended"; ADR 0044):** the layout
reference is the owner's prototype screenshot, copied to `docs/ui/dashboard.png` with its dark and phone views;
Recharts is installed at 3.10.1 (3.11.0 was under a week old). Built as decided; the dashboard and Recharts load with
the dashboard's route.

### Phase 8. Delivery (branch `phase-8-delivery`)

#### 8.1 One-command local run

**Owner's decisions for 8.1, recorded 2026-11-23 (the plan was approved "as recommended"; ADR 0045):** the demo is
entered through the API, after an opt-in bootstrap of the demo hospital and its administrator; nginx makes a local
self-signed certificate on its first start; the migrations run as in runbook section 10 (logins as the administrator, the
idempotent script as the migrator, the role as the administrator); the branch `phase-8-delivery` is cut from
`phase-2-billing`. Built as decided; the migration step uses the runbook's script, not a bundle.

#### 8.2 Performance and browser tests

**Owner's decisions for 8.2, recorded 2026-11-24 (ADR 0046):** the plan was approved "as recommended": the load test a
Node script with no package, run as a compose service behind a profile; `@playwright/test` 1.65.1 for the journey; the
number series' wait and hold told by the API in `Server-Timing`, off by default; the 20 ms read as the hold. The first
full run on the office machine, with no pause between requests, missed (posting p95 1,130 ms: the 50 users queued on the
series of bills) and then filled the machine's disk. The owner then chose: judge 50 users with a one-second pause between
their requests and report the no-pause run as a stress figure; read the idempotency key's row before the number (one
round trip out of the hold); and run smaller on the office machine (2,000 bills), leaving the full 10,000 to the
personal machine. Built as decided; every target met there with no drift.

#### 8.3 Release R1

**Owner's decisions for 8.3, recorded 2026-11-24:** given in the session of 8.2, after 8.2 was committed, at the owner's
word. The plan was approved "as recommended": every ADR still Proposed (0003, 0007, 0009, 0010, 0024) and every point
still awaiting review (ADR 0015's additions, ADR 0019 point 5, ADR 0027 point 9) is accepted as built; the tag
`v1.0.0` is annotated, on the 8.3 commit, local only (not pushed), and says what is still owed (runbook section 8);
`main` is an ancestor of `phase-8-delivery`, so a fast-forward merge keeps it on `main`. The traceability table covers
US-01 to US-44 (US-44 was added by 6.3) and is held by an architecture test; what section 9 of CLAUDE.md left open is in
`docs/backlog.md`.

### Phase 9. The interface (branch `phase-9-interface`)

#### 9.1 Brand, design tokens and the sign-in page

**Owner's decisions for 9.1, recorded 2026-11-24 (ADR 0047):** the reference was found in the owner's Downloads and
copied in; there is no photograph apart from it, so the page was built to take one later and stands without it. The plan
was approved "as recommended": field borders darker than the reference's, to meet 3:1 (the alternative, matching the pale
border, was offered); Inter through `@fontsource-variable/inter` 5.3.0, the one new package; our own icons; the theme
control as the second addition to the reference. Built as decided.

#### 9.2 The shell and the shared components

**Owner's decisions for 9.2, recorded 2026-11-24 (ADR 0048):** the plan was approved: tables scroll inside their own
frame with the first column pinned (rows folding into cards was offered); and, asked for by the owner during the plan, the
theme becomes a modern toggle, a pill of three with Light, Dark and System (a two-way switch was offered, which would have
lost System). Built as decided; fields with ₹ and %, the audit timeline and the one status mapping move to 9.3.

#### 9.3 The money screens (shared components, bills, credit notes and adjustments)

**Owner's decisions for 9.3, recorded 2026-11-24 (ADR 0049):** the plan (seven shared components, no new package) was
approved as one prompt and then split, at the owner's word, into 9.3 (the shared components, bills, credit notes and
adjustments) and 9.3b (payments, refunds, the statement and the inbox). 9.3b amends ADR 0049; 9.4 keeps ADR 0050.

#### 9.3b The cashier's screens and the inbox

**Owner's decision for 9.3b, recorded 2026-11-24 (ADR 0049, amendment):** the plan was approved with the inbox as a list of
cards, its test finding list items instead of rows with the same assertions (keeping rows drawn as cards was offered).

#### 9.4 The dashboard, the reports and the finishing pass (split: 9.4 the dashboard, 9.4b the rest)

**Owner's decisions for 9.4, recorded 2026-11-24 (ADR 0050):** the plan was approved with its recommended split (the
dashboard and its tooltips now; the rest in 9.4b). Two dashboard tests change their way in (the card's figure as the
button of its tooltip, the period by its radio button), not what they check.

#### 9.4b The reports, print, the finishing pass and the release

**Owner's decisions for 9.4b, recorded 2026-11-24 (ADR 0050, amendment):** the plan was approved with
`@axe-core/playwright` 4.14.0. The first visit's budget was set at 145 kB, above the 130 kB the plan proposed, because
the libraries the sign-in page uses come to about 135 kB (said in the result).
