# Testing Strategy

- **Status:** written 2026-10-17 with the test infrastructure (Prompt 1.4, ADR 0016). The financial integrity
  tests, the frontend tests and the CI gates are described as they are planned and are marked so.
- **Rules cited:** CLAUDE.md section 4 (the twelve rules) and section 6 (the working agreement); BR-nnn from
  `docs/requirements.md`.

---

## 1. The pyramid, and where each kind of rule is proven

| Layer | What it proves | Tools | Where |
|---|---|---|---|
| Domain unit | Invariants and business rules of aggregates and value objects, `Result`/`Error`, typed ids. Named after the rule they test where one exists (`BR-nnn`). | xUnit, Shouldly | `tests/BillingRecon.Domain.Tests` |
| Application unit | Use-case handlers with ports replaced by stubs (what they check, in which order, and that nothing is written when a check fails); validators, case by case, with the field each message is reported under; the conventions the registration by scanning relies on (one handler per message, a handler takes its message's validator exactly when one exists, ADR 0018). | xUnit, Shouldly, NSubstitute | `tests/BillingRecon.Application.Tests` |
| Persistence without a database | The EF Core model and its conventions, the interceptors on the change tracker, the SQL text of queries (`ToQueryString`). The SQL Server provider against a server nothing listens on; the in-memory provider is banned. | xUnit, Shouldly, EF Core | `tests/BillingRecon.IntegrationTests/Persistence` |
| Database | What only the engine can show: migrations applied to SQL Server 2022, CHECK constraints, unique and filtered indexes, foreign keys, the row version, the tenant predicate of UPDATE and DELETE, the audit columns as stored, readiness against pending and applied migrations, and what the unit of work makes of a lost race. Every tenant-scoped row needs its tenant's row first (the foreign key on `TenantId`): `NewApiTenantAsync`, `AddProbeTenantRowsAsync`. | Testcontainers.MsSql, Respawn, Bogus | `tests/BillingRecon.IntegrationTests/Database` (the `Database` collection) |
| API | The pipeline that ships: routing and versioning, the error contract, correlation id, logging and PII masking, health, OpenAPI. In-process host, Production environment unless a test says otherwise. The endpoints of a resource end to end (status and error codes, ETag and If-Match, paging and search, simultaneous requests, and tenant isolation with two tenants) run in the `Database` collection on `DatabaseApiFactory`. | WebApplicationFactory | `tests/BillingRecon.IntegrationTests` (root), `DatabaseApiFactory` for endpoints that need the database and an acting tenant |
| Architecture | The layer rule, the module rule with its listed exceptions, every type in a known module, banned packages in every project file, the banned-API list, controller conventions, no minimal APIs, Reporting writes nothing, and docs/api.md names every endpoint and every error code (`ApiDocumentationTests`). | NetArchTest, file scans | `tests/BillingRecon.ArchitectureTests` |
| Financial integrity | Every identity of `docs/database.md` section 5.5 holds after every database test (cache equals ledger, balanced postings, gapless numbers): the reconciliation runs when each test ends (section 2). The scenario tests (`EndToEndScenarioTests`, `ReportsApiTests`) also check it step by step. Random sequences of operations, several seeds, are still to come. | xUnit, Bogus | `DatabaseTest`, the `Database` collection |
| Frontend (Phase 8) | Components and the critical flows (payment and allocation form). | Vitest, Testing Library | `frontend/` |

A test belongs to the lowest layer that can prove the point. The model and the SQL text answer most
questions about persistence without a database; a test needs the database only when the engine's
behaviour is the question.

## 2. The database tests (ADR 0016)

- **One SQL Server per test run.** `SqlServerFixture` starts one container with Testcontainers,
  the image `docker-compose.yml` names, shared by every class of the `Database` collection. The
  collection's tests run one after another; the other collections run in parallel with them.
- **Two databases.** `BillingRecon_Api` is created by the migrations and is what the API under
  test is pointed at. `BillingRecon_Probe` is created from the test model (`EnsureCreated`) and holds
  the probe entities (`Widget`, `LogLine`, `Setting`) that exist only in tests, until real
  tenant-scoped tables exist.
- **Empty tables before every test.** Respawn deletes the data of both databases; the schema and
  `__EFMigrationsHistory` stay. Writes commit for real; nothing is rolled back behind the test's back.
- **Acting for a tenant.** A client made with `CreateClientFor` sends with every request a bearer
  token minted for it by the API's own issuer (ADR 0031), for the tenant, user and roles it names; a
  client that names no role is a TenantAdmin, like the user row behind it. A client made with
  `CreateClient` is anonymous and gets 401 everywhere but the sign-in and the health endpoints. A
  context built by hand takes a `FixedIdentity` (tenant and user) or `SystemIdentity.Instance` (none).
- **Roles.** A client acts with the roles it names, and the matrix applies to it (ADR 0032): a test
  gives each client the role of what it does, and a client that both sets up and acts names both
  (`"Cashier", "BillingClerk"`). `PermissionMatrixApiTests` walks every endpoint with each role
  against its own copy of the matrix; a new endpoint needs a row there before the suite is green.
- **No drift after any test** (Prompt 6.2, ADR 0036 amendment). After every test, `DatabaseTest` runs the
  reconciliation (`docs/database.md` section 5.6) for every tenant the test left in `BillingRecon_Api` and fails on
  any drift row, a gap in a number series included. A test that leaves a defect on purpose says which identities with
  `ExpectDrift(DriftIdentity....)`; a class that tests one part on its own names them once by overriding
  `DriftLeftOnPurpose`. Nothing else is ignored, so a new financial test is checked without asking. A test may also
  call `ShouldHaveNoDriftAsync()` midway.
- **Over HTTPS, for a named host** (ADR 0035). Every host is called at `https://localhost` and names `localhost` in
  `AllowedHosts`, as a client of a deployment is; `RemoteAddressStartupFilter` gives a request a client address.
- **Time.** `FakeClock` everywhere; the API's clock is the factory's `Clock`, which a test moves.
- **Data.** `TestData` draws from one seeded Bogus generator: the same tenants and names every run.
- **Without Docker** the fixture fails with one message; the collection fails, every other test
  runs. Nothing is skipped silently. To run only the rest on such a machine:

  ```bash
  dotnet test --filter "FullyQualifiedName!~IntegrationTests.Database."
  ```

  (The trailing dot matters: without it the filter also drops `DatabaseStartupTests`, which needs no Docker.)

## 3. Why not the EF Core in-memory provider

It enforces nothing the financial rules depend on. Three things it would pass that SQL Server refuses:

1. A number prefix in lower case: the CHECK constraint with its binary collation does not exist in
   memory (`CK_Tenants_BillPrefix`, ADR 0008).
2. Two cashiers saving the same row from the same stale version: there is no `rowversion`, so no
   `DbUpdateConcurrencyException`, and the second write silently wins (BR-804).
3. A forged update of another tenant's row: there is no statement, so no `TenantId` predicate in it;
   the in-memory store would overwrite the row (BR-002, ADR 0015).

It also stores a `decimal` without the column's scale, so a value SQL Server would round or refuse is
kept as is (BR-001). A green run on it would say nothing about any of this.

## 4. Rules of the house

- **Never weaken a test** to make it pass: no deleting, skipping, loosening or widening (CLAUDE.md
  section 6). Change the code, or raise the disagreement with the owner and record the decision.
- **Name tests as sentences** that state the rule (`A_forged_update_of_another_tenants_row_changes_no_row`).
  Where a business rule has an id, the test cites it in its name or its summary.
- **One reason per test.** A test that would pass for two different reasons proves neither; split it.
- **No silent skips.** A test that cannot run on a machine fails there and says why.
- **Tests with every feature** (CLAUDE.md section 6): a feature without tests is not done, and a
  rule in `docs/requirements.md` without a test is an open item for Phase 7.

## 5. Running the tests

| Command | What runs |
|---|---|
| `dotnet test` | Everything. Needs a running Docker engine for the `Database` collection (the first run pulls the SQL Server image unless `docker compose up` already did). |
| `dotnet test --filter "FullyQualifiedName!~IntegrationTests.Database."` | Everything but the database tests, for a machine without Docker (the trailing dot keeps `DatabaseStartupTests` in). |
| `dotnet test tests/BillingRecon.ArchitectureTests` | The architecture rules alone; they read source files, so run from the repository. |

The office machine (CLAUDE.md section 2) cannot run the database tests at all; they run on the
owner's machine and in CI.

## 5a. The frontend's tests (Prompt 7.1, ADR 0038)

`npm test` in `frontend/`: Vitest with jsdom and React Testing Library, the files beside the code they test
(`*.test.ts`, `*.test.tsx`) and the shared helpers in `src/test/`. The API is faked by giving the client a fake `fetch`
(`fakeApi` answers each request in turn and keeps the requests it saw); there is no mocking package. A test renders the
whole app at an address with `renderApp`, a kept session and a fake network, so it exercises the router, the session,
the client and the page together, as a user meets them.

What the frontend's tests hold, beyond the behaviour of each screen:

- **The navigation against the permission matrix:** a table in `navigation.test.ts`, written out from
  `docs/requirements.md` section 1, against the navigation's own copy, for each of the five roles.
- **No raw colour:** `colours.test.ts` scans `index.html` and every style and script file of `src/` but `tokens.css`
  and the tests, and fails with the file and line of a colour value; it first proves it finds one.
- **WCAG AA in both themes:** `contrast.test.ts` computes the contrast of the tokens of `tokens.css` (text 4.5:1,
  borders, focus and chart series 3:1).
- **The inline theme script:** `themeScript.test.ts` runs the text of `index.html`'s script as the browser does, for every
  stored setting and system scheme, checks it runs before the app, and pins its SHA-256 for the content security policy.
- **The API's types:** the generated `schema.d.ts` must be what `npm run api:types` writes from the committed document
  (CI), and `OpenApiSnapshotTests` (.NET) holds that document to the API's own.

Since Prompt 7.2 (ADR 0039):

- **Screens are tested against `routedApi`**, a fake that answers each request by its method and path (`'GET
  /api/v1/patients/42'`) and keeps what was sent (`sent(method, path)`), since a record's page asks for several things at
  once; `inTurn` gives a route a different answer each time (a record, then the record someone else changed). Records as
  the API answers them come from `src/test/records.ts`.
- **The permissions against the API's:** `permissions.test.ts` reads
  `src/BillingRecon.Api/Common/Authorization/Policies.cs` and fails when the frontend's copy differs in a policy or a role.
- **What is sent is asserted as sent:** the `If-Match` header of every change, the body of every create and correction
  (blanks as null, decimals as JSON numbers), and the query of every list.

Since Prompt 7.3 (ADR 0040):

- **One arithmetic, two implementations, one set of cases.** `frontend/src/bills/lineCases.json` lists bill lines and
  what each comes to; `lineMath.test.ts` holds the browser's estimate to it and the Domain's
  `LineCalculatorSharedCasesTests` holds the server's `LineCalculator` to it. A new case goes in the file, and both
  sides must agree with it.
- **The key of an operation is asserted on the wire:** the same `Idempotency-Key` on a retry after no answer or 409
  `idempotency_in_progress`, a new one after a refusal or a changed request.

Since Prompt 7.4 (ADR 0041):

- **A cap is tested at the paisa:** an amount one paisa above a bill's balance due, a payment's unapplied cash or a
  refund's source is refused before anything is sent (`sent(...)` stays empty). `money.test.ts` holds the exact paise of
  a typed amount and of a figure of the server, and accepts the amount at the cap.
- **An answer about one line lands at that line:** a refusal naming `allocations[i].amount` is shown at the bill of that
  line and not again in `ProblemAlert`.

Since Prompt 7.5 (ADR 0042):

- **Every kind of request is decided in one table-driven test** (`Approvals.test.tsx`): for each of the eight, the
  document the inbox reads, the path the approval goes to, the version in `If-Match` and the key (none for a prefix
  change). A new kind of maker-checker document adds its row.
- **The maker's own request is asserted to have no review**, as the API refuses it (BR-603); a conflict is asserted to
  name the state the checker saw and the state now.

Since Prompt 7.5b (ADR 0043):

- **An export is read back from the file the page made**: `URL.createObjectURL` is replaced in the test to keep the
  Blob, whose bytes are read with `FileReader` (the BOM included), and the CSV's lines are asserted whole.

The jsdom environment has no `matchMedia`; `src/test/setup.ts` gives every test a system that prefers light, which a
test changes with `stubColourScheme`, and clears both storages before each test.

## 5b. The load test and the browser journey (Prompt 8.2, ADR 0046)

Two suites that need the whole application running, so neither is part of `dotnet test` or `npm test`, and neither
runs in CI: shared runners say nothing about latency, and the journey needs SQL Server, the API and nginx. Both run
against the one-command local run with the demo hospital (`docs/runbook.md` sections 13 and 14).

- **The load test**, `deploy/load/load.mjs` (Node, no package): 50 users of one hospital, each with a patient and a
  payer of their own, draft a bill, post it, record a payment and allocate it, in a loop. The hospital is first filled
  to 10,000 posted bills in the month; then two minutes with a pause of one second between one user's requests are
  judged against `docs/requirements.md` section 5 (p95 of posting a bill and of recording a payment under 500 ms; each
  number series held under 20 ms at p95; no error); then a minute with no pause is reported as a stress figure; then
  the reconciliation must find no drift. How long each posting waited for its number series and held it comes from the
  API's `Server-Timing`, switched on for the run (`Diagnostics:ServerTiming`). The exit code is the verdict.
  `deploy/load/stats.test.mjs` holds its arithmetic (nearest-rank percentiles, the header, the verdict) to known figures
  with Node's own test runner, in CI.
- **The sign-in page in the browser** (Prompt 9.1, ADR 0047), `frontend/e2e/signin.spec.ts`: the built page at
  1672 x 941 held within 4 px to what was measured on `docs/ui/sign-in.png`, in both themes; nginx's content security
  policy, read from `frontend/deploy/nginx.conf` and put on the page's response by the test, with no
  `securitypolicyviolation`, no console error and no layout shift; no animation at all with reduced motion; the order
  of the keyboard; 390 and 360 px without a sideways scroll. It needs the built app, not the dev server.
- **The shell in the browser** (Prompt 9.2, ADR 0048), `frontend/e2e/shell.spec.ts`: a session in the tab and the API's
  answers given by the test; the patients list in both themes at 1440 and 390 px under nginx's policy, with nothing
  refused, no console error, no sideways scroll and no layout shift; the drawer by keyboard (it holds the focus, Escape
  gives it back); nothing animated with reduced motion; the specimen page absent from a production build.
- **The money screens in the browser** (Prompt 9.3, ADR 0049), `frontend/e2e/money.spec.ts`: the bill editor, a posted
  bill, a credit note and an adjustment with the API's answers given by the test, which fails on a request it did not
  expect; in both themes at 1440 and 390 px under nginx's policy, with nothing refused, no console error, no sideways
  scroll and no layout shift (the elements that moved are named when it fails); the totals card in view while the lines
  scroll; nothing animated with reduced motion.
- **The cashier's screens in the browser** (Prompt 9.3b, ADR 0049 amended), `frontend/e2e/cashier.spec.ts`, on the helper
  `e2e/stubbed.ts` it shares with `money.spec.ts`: the payment, allocating, a refund, the statement and the inbox, the same
  checks as the money screens, the meter in view and warning before the cap, nothing animated with reduced motion.
- **The dashboard in the browser** (Prompt 9.4, ADR 0050), `frontend/e2e/dashboard.spec.ts`, on `e2e/stubbed.ts`: both
  themes at 1440 and 390 px with the same checks as the other screens; the crosshair moved by the arrow keys with a dot
  on each line, closed by Escape; the tooltips of a card, a sparkline, the donut and a bar inside the window and off the
  pointer; nothing animated with reduced motion. A failing layout-shift check names what moved, by how much and when.
- **Every route** (Prompt 9.4b, ADR 0050 amended), `frontend/e2e/routes.spec.ts`: the 48 routes of the shell in both themes
  at 1440 and 390 px with the checks of the other screens and one h1; axe's WCAG 2.2 A and AA rules
  (`@axe-core/playwright`) with no serious or critical finding; no sideways scroll at 1,920, 720 (200% zoom) and 360 px;
  every screenshot kept in `test-results`. `frontend/e2e/print.spec.ts`: a bill, a statement and a report printed to A4
  in both themes (no shell, dark on white, the hospital and the day, no row cut in two); forced colours and
  prefers-contrast; the keyboard's first stop.
- **Searching while typing** (Prompt 10.2b; ADR 0039 and ADR 0040, amended 2026-11-25): `LiveSearch.test.tsx` and
  `RecordPicker.test.tsx` with real timers (nothing before the pause and one request after it, one character sends
  nothing, a stale request cancelled and its answer dropped, the address replaced and Back, page 1, the picker by the
  keyboard alone and by the pointer), and `frontend/e2e/search.spec.ts` on the same helper: a list in both themes with no
  layout shift, the picker by the keyboard with axe while its results are open, and by a tap on a phone.
- **Opening balances** (Prompt 10.3; ADR 0053): `OpeningBalanceTests` (domain: the opening receivable and advance, the
  batch decided once, the fixed prefixes), `OpeningBatchCheckerTests` (every kind of rejected row with its file, line and
  reason, the trial balance to the paisa, the CSV reader), and `OpeningBalancesApiTests` in the `Database` collection: the
  dry runs write nothing, patients matched and never merged, a batch posted whole by a second person's approval, the same
  batch refused twice, a payment settling an opening receivable, an approval refused when a row no longer holds, and the
  whole export of the reference hospital (`deploy/reference/export`) imported to its trial balance, payer by payer. The
  export is held to its generator by `node deploy/reference/generate-export.mjs --check` in CI.
  The screens (Prompt 10.3b): `Imports.test.tsx` (Import and Submit closed while a row is rejected and after a file
  changes, a file too large for one request refused before sending, the key of a submission reused after a fault, a
  batch's state) and `frontend/e2e/imports.spec.ts` in a real browser (files chosen from the disk, the findings and
  their download, no layout shift, a phone, the keyboard, axe in both themes); the four routes are in `routes.spec.ts`.
- **Printed documents, their data** (Prompt 10.4; ADR 0054): `PrintedDocumentsTests` (domain: the letterhead set, trimmed
  and cleared, a GSTIN's shape, the SAC code on an item and kept on a line, the original and the duplicate),
  `PrintedDocumentsHandlerTests` (the next copy number, a document that cannot be printed, two prints at once, the
  letterhead under the settings' version) and `PrintedDocumentsApiTests` in the `Database` collection (the letterhead
  set by the administrator and read by a cashier, a posted bill's line keeping its SAC code after the catalogue changes,
  the same request being one print, a draft refused, another hospital's document not found). On the screens:
  `Administration.test.tsx` (the letterhead saved with the settings' version, which the settings form then sends).
  The printed pages (Prompt 10.4b): `words.test.ts` (the amount in words for 0.50, 1,00,000.01, 12,34,56,789.99 and the
  edges of each scale; the tax in its two halves, always summing to the stored tax), `Printing.test.tsx` (each document's
  fields against its record, the three titles of a bill, Original then Duplicate with the dialog opened only after the
  record, a refused print opening none, what is not printed) and `frontend/e2e/printed.spec.ts` (each document recorded,
  marked and printed to an A4 PDF in both themes with no shell and dark text on white, a phone, axe).
- **The logo and the signature** (Prompt 10.4c; ADR 0059): `LetterheadImagesTests` (Domain: each of the three formats
  told by its bytes with its size, an SVG and every other file refused, every file cut short refused, the limits of
  size, the tenant's hashes with one event a change), `LetterheadImageHandlerTests` (Application: stored once, a stale
  version, every refusal with its reason, a removal, a read), `LetterheadImagesApiTests` (the `Database` collection:
  upload, replace, remove and read; a wrong type, an oversized file and an SVG refused with nothing stored; another
  hospital reading and removing nothing of this one; four audit records with the hashes and no image), the two
  `LetterheadImages.test.tsx` of the frontend (the cards of the Settings screen; the sheets with and without images,
  Print waiting for them, a duplicate signed like the original) and `frontend/e2e/letterhead.spec.ts` (the three sheets
  with real images in both themes under nginx's policy, nothing moving when an image arrives, an A4 PDF, a phone, axe,
  and the upload on the Settings screen). The test images are drawn by the tests (`png` of `e2e/stubbed.ts`).
- **The print settings** (Prompt 10.4c-2; ADR 0059, amended): `PrintSettingsTests` (Domain: the default is the sheet as
  it was, every limit, a roll for no bill, the two a bill never switches off), `PrintSettingsHandlerTests` (the first
  save and its version of none, a change, two first saves at once, every refusal at its field, who recorded a
  document), `PrintSettingsApiTests` (the `Database` collection: read, save, audit, the CHECK constraints, another
  hospital, the recorder reads), `PrintSettings.test.tsx` (each sheet at each paper, logo place and size, stationery,
  every switch; the same figures and mark whatever the settings; no `style` attribute; the preview equal to the print,
  part for part), `PrintSettingsPage.test.tsx` (the Print tab) and `frontend/e2e/printsettings.spec.ts` (the PDF's
  page box for A4, A5 and the roll, the rules under nginx's policy, no inline style, the A5 table within its sheet
  with no figure broken, the preview on paper in both themes, a phone, axe).
- **Signing out** (Prompt 10.2c; ADR 0057): `Shell.test.tsx` holds that Sign out sends `POST /api/v1/auth/logout` with
  the token of the session that ends, signs out here all the same when the API refuses or cannot be reached, tells the
  other tabs who signed out and keeps nothing of it, and that a tab signs out when another says the same user did. On
  the server: `SessionGateTests` (no database: the claim, one question per request, 401 `session_ended`, the health
  checks not concerned) and `AuthApiTests` in the `Database` collection (a sign-out, a disabling, a reset, changed roles
  and a change of one's own password, each against tokens issued before).
- **The budgets of the build** (Prompt 9.4b), `npm run budgets` after `npm run build`, in CI: the first visit to the
  sign-in page and every route's chunk, compressed, against their budgets.
- **The browser journey**, `frontend/e2e/journey.spec.ts` (Playwright, Chromium or an installed Edge or Chrome): a
  clerk registers a patient and their payer, drafts a bill with a catalogue line and a free-text line and posts it; a
  cashier records the payment and applies it oldest first, which settles the bill and empties the statement; a
  supervisor sees the dashboard and a reconciliation without drift. Selectors are roles and labels, as in the component
  tests; no fixed waits. Any error a page writes to the console fails the step, so a script the content security
  policy of nginx refuses fails it too.

## 6. CI quality gates (Prompt 1.5, to come)

Build with warnings as errors; `dotnet format --verify-no-changes`; every test including the
`Database` collection (the hosted runners have Docker); a coverage report of the Domain and
Application unit tests as an artifact (a guide, not a goal; the integration tests run
uninstrumented, see ADR 0017); no vulnerable package; the idempotent migration script generates; the
API image builds and starts. Written in `.github/workflows/ci.yml` with Prompt 1.5; the gates and
their local equivalents are in `docs/runbook.md` section 5.

## Production deployment (Prompt 10.5, ADR 0055)

`SecretFilesTests` (a file is a setting, wins over an earlier source, loses its last line break; a missing directory adds
nothing), `DatabaseSettingsOnSqlServerTests` in the `Database` collection (the API refuses to start with
`READ_COMMITTED_SNAPSHOT` off and names no database), and `ProductionDeploymentTests` in the architecture tests (the
production compose file sets no secret, takes every secret from the host's directory, builds nothing, pins every tag,
runs the release's tag, and publishes only nginx). The deployment itself is proved by walking runbook section 19 on a
staging server, which has not happened.
