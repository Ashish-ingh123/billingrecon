# BillingRecon build prompts, from zero to a working R1

Written 2026-11-20 after inspecting this repository. The original blueprint file
(`billing_recon_blueprint.md`) is no longer on the office machine, so the prompts below are
**reconstructed** from what the repository records (CLAUDE.md, `docs/requirements.md`,
`docs/domain-model.md`, `docs/database.md`, ADRs 0000 to 0018, the commit messages). Prompts 0.1 to
2.2 describe what was already built. Prompts from 2.2-close onward are new wording for the work that
is left; their numbering is mine, not the blueprint's.

## Where the project stands

| Step | State |
|---|---|
| Phase 0 (0.1 to 0.4), Phase 1 (1.1 to 1.5) | Done, commit `03c42fb` on `main` |
| 2.1 Money and the shared kernel | Done, commit `ae97209` on `phase-2-billing` |
| 2.2 Patients and payers | Code, tests, docs and ADR 0018 written; migration scaffolded with EF Core 10.0.12 on 2026-10-18 under the name `0002_SyncCurrentModel`; not committed that day; no test result recorded. **Since then committed, and built on .NET 10 and run green on SQL Server 2022 by the first run of the personal machine (2026-11-24, at `4c2e75f`; runbook section 8)** |
| 2.3, 2.4, 3.1, 3.2 and 3.4 (service catalogue, bill drafts, number series, idempotency, event dispatch and audit) | Written on 2026-11-20 on top of the uncommitted 2.2, each pre-checked on .NET 9 with LocalDB only; not built on .NET 10 and not committed that day; migrations `0003` to `0007` in the repository since 2026-11-21, scaffolded with EF Core 10 (CLAUDE.md section 9, item 16). **Since then committed, and built on .NET 10 and run green on SQL Server 2022 by the first run of the personal machine (2026-11-24, at `4c2e75f`; runbook section 8)** |
| 3.3 Receivables ledger | Written on 2026-11-20 after 3.4, with the owner's decisions (ADR 0005 built note, ADR 0021); pre-checked on .NET 9 with LocalDB only; not built on .NET 10 that day; migration `0008_Receivables` in the repository since 2026-11-21, completed by hand as ADR 0021 says. **Since then committed, and built on .NET 10 and run green on SQL Server 2022 by the first run of the personal machine (2026-11-24, at `4c2e75f`; runbook section 8)** |
| 3.5 Post a bill | Written on 2026-11-20 with the owner's decisions (ADR 0022); pre-checked on .NET 9 with LocalDB only; not built on .NET 10 that day; migration `0009_BillPosting` in the repository since 2026-11-21. **Since then committed, and built on .NET 10 and run green on SQL Server 2022 by the first run of the personal machine (2026-11-24, at `4c2e75f`; runbook section 8)** |
| 3.6 Maker-checker approvals | Written on 2026-11-20 with the owner's decisions (ADR 0023); pre-checked on .NET 9 with LocalDB only; not built on .NET 10 that day; migration `0010_Approvals` in the repository since 2026-11-21. The role of the checker is left to 5.3. **Since then committed, and built on .NET 10 and run green on SQL Server 2022 by the first run of the personal machine (2026-11-24, at `4c2e75f`; runbook section 8)** |
| 4.1 Record a payment | Written on 2026-11-20 with the owner's decisions (CLAUDE.md section 9, item 20); pre-checked on .NET 9 with LocalDB only; not built on .NET 10 that day; migration `0011_Payments` in the repository since 2026-11-21. **Since then committed, and built on .NET 10 and run green on SQL Server 2022 by the first run of the personal machine (2026-11-24, at `4c2e75f`; runbook section 8)** |
| 4.2 Allocate a payment to bills | Code and tests written on 2026-11-20, pre-checked and documented on 2026-11-21 (ADR 0024, Proposed; CLAUDE.md section 9, item 21); pre-checked on .NET 9 with LocalDB only; not built on .NET 10 that day; migration `0012_PaymentAllocations` in the repository since 2026-11-21. **Since then committed, and built on .NET 10 and run green on SQL Server 2022 by the first run of the personal machine (2026-11-24, at `4c2e75f`; runbook section 8)** |
| 4.3 Credit notes | Written on 2026-11-21 with the owner's decisions (ADR 0025; CLAUDE.md section 9, item 22); pre-checked on .NET 9 with LocalDB only; not built on .NET 10 that day; migration `0013_CreditNotes` in the repository since 2026-11-21. **Since then committed, and built on .NET 10 and run green on SQL Server 2022 by the first run of the personal machine (2026-11-24, at `4c2e75f`; runbook section 8)** |
| 4.4 Adjustments | Written on 2026-11-21 with the owner's decisions (ADR 0026; CLAUDE.md section 9, item 23). **Built and tested on .NET 10** (SDK 10.0.404, installed on the office machine that day) in a throwaway copy, with the database tests on LocalDB 2019; not run on SQL Server 2022; migration `0014_Adjustments` in the repository since 2026-11-21 |
| 4.5 Reversals | Written on 2026-11-21 with the owner's decisions (ADR 0027; CLAUDE.md section 9, item 24). Built and tested on .NET 10 in a throwaway copy, with the database tests on LocalDB 2019; not run on SQL Server 2022; migration `0015_PaymentReversals` in the repository since 2026-11-21. It also corrects what a late decision answers in every approve and reject use case since 3.6 (409, not 500) |
| 4.5b Reverse an application of credit | Written on 2026-11-21 with the owner's decisions (ADR 0028; CLAUDE.md section 9, item 25). Built and tested on .NET 10: build, format gate and the tests without a database in the repository, the database tests in a throwaway copy on LocalDB 2019; not run on SQL Server 2022; migration `0016_CreditApplicationReversals` in the repository, added the ordinary way on top of `0003` to `0015`, which were committed on their own first |
| 4.6 Refunds | Written on 2026-11-21 with the owner's decisions (ADR 0029; CLAUDE.md section 9, item 26). Built and tested on .NET 10 like 4.5b: build, format gate and the tests without a database in the repository, the database tests in a throwaway copy on LocalDB 2019; not run on SQL Server 2022; migration `0017_Refunds` in the repository. With it the end-to-end scenario test of the prompt |
| 5.1 Users and roles | Written on 2026-11-21 with the owner's decisions (ADR 0030; CLAUDE.md section 9, item 27). Built and tested on .NET 10 like 4.6: build, format gate and the tests without a database in the repository, the database tests in a throwaway copy on LocalDB 2019; not run on SQL Server 2022; migration `0018_Users` in the repository. A development database with data must be dropped before `0018` |
| 5.2 Sign-in and JWT | Written on 2026-11-21 with the owner's decisions (ADR 0031; CLAUDE.md section 9, item 28). Built and tested on .NET 10 like 5.1; no migration |
| 5.3 Role permissions | Written on 2026-11-21 with the owner's decisions (ADR 0032; CLAUDE.md section 9, item 29), the first half of the prompt. Built and tested on .NET 10 like 5.1; no migration |
| 5.3b Tenant settings and prefix changes | Written on 2026-11-21 with the owner's decisions (ADR 0033; CLAUDE.md section 9, item 30). Built and tested on .NET 10 like 5.1; migration `0019_PrefixChanges` in the repository. Committed (`31243e3`, `7755271`) |
| 5.3b fix: the prefix approval holds the series | Written on 2026-11-23 with the owner's decision on ADR 0033 built note point 5 (option B; ADR 0033 "Amended 2026-11-23"; CLAUDE.md section 9, item 31). Built and tested on .NET 10 like 5.1; no migration. Committed (`ce64e6c`) |
| 5.4 Database logins and grants | Written on 2026-11-23 with the owner's decisions (ADR 0034; CLAUDE.md section 9, item 32), the first half of the prompt. Built and tested on .NET 10 like 5.1, every database test now as the application's login; migration `0020_DatabaseRoles` in the repository, written by hand. Found: the application relies on `READ_COMMITTED_SNAPSHOT ON` |
| 5.4b HTTP hardening and the security document | Written on 2026-11-23 with the owner's decisions (ADR 0035; CLAUDE.md section 9, item 33), the second half of 5.4. Built and tested on .NET 10 like 5.1; no migration, no package. TLS at a proxy, HTTPS only with HSTS, allowed hosts, two sign-in limits, CORS, the headers of an answer; `docs/security.md` written in full with its threat model. The CI smoke run changed and has not run |
| 6.1 Registers and balances | Written on 2026-11-23 with the owner's decisions (ADR 0036; CLAUDE.md section 9, item 34). Built and tested on .NET 10 like 5.1; no migration, no package. Eight read-only report endpoints, every figure held against ledger sums in the tests; Reporting reads every module through five listed exceptions to the module rule and writes nothing |
| 6.2 Reconciliation and audit trail | Written on 2026-11-23 with the owner's decisions (ADR 0036 amendment; CLAUDE.md section 9, item 35). Built and tested on .NET 10 like 5.1; no migration, no package. The reconciliation report with gapless numbering as identity 11, the reconciliation after every database test, US-43 for a bill, and `docs/api.md` complete with every endpoint and an error-code catalogue that a test keeps complete |
| 6.3 Dashboard figures | Written on 2026-11-23 with the owner's decisions (ADR 0037; US-44 and BR-902 added to `docs/requirements.md`; CLAUDE.md section 9, item 36). Built and tested on .NET 10 like 5.1; no migration, no package. Seven GET panels under `/api/v1/dashboard` for TenantAdmin, Supervisor and Viewer, every figure derived from the ledger by business date through a sixth sanctioned raw-SQL file; no trend for the drift card. The backend of R1 is written to its end |
| 7.1 Scaffold and sign-in | Written on 2026-11-23 with the owner's decisions (ADR 0038; CLAUDE.md section 9, item 37). Built and tested on the office machine with Node 24.22.2: the frontend's format, lint, types, 161 tests and build, and the .NET gates with the new `OpenApiSnapshotTests`; signed in through the real API in the browser. 27 npm packages approved; no NuGet package, no migration. The CI job has not run |
| 7.2 Patients, payers and catalogue screens | Written on 2026-11-23 with the owner's decisions (ADR 0039; CLAUDE.md section 9, item 38). Built and tested like 7.1: 220 frontend tests, the .NET gates; in the browser against the real API, a real conflict included. No package, no migration, no change of the API; `OpenApiSnapshotTests` now writes line breaks as LF |
| 7.3 Bill screens | Written on 2026-11-23 with the owner's decisions (ADR 0040; CLAUDE.md section 9, item 39). Built and tested like 7.1: 262 frontend tests, the .NET gates with a Domain test that holds the server to the browser's line cases; in the browser against the real API, posting and approval included. No package, no migration, no change of the API |
| 7.4 Cashier screens | Written on 2026-11-23 with the owner's decisions (ADR 0041; CLAUDE.md section 9, item 40). Built and tested like 7.1: 296 frontend tests, the .NET gates; in the browser against the real API, recording, allocation, a reversal request, the statement and a refund submitted. No package, no migration, no change of the API |
| 7.5 Approvals, credit notes and adjustments | Written on 2026-11-23 with the owner's decisions (ADR 0042; CLAUDE.md section 9, item 41). The prompt was split with its plan: the reports are 7.5b. Built and tested like 7.1: 330 frontend tests, the .NET gates and the whole `Database` collection on LocalDB (the pending approvals report answers two more fields); in the browser against the real API, the Phase 4 scenario by three users. No package, no migration |
| 7.5b Reports | Written on 2026-11-23 with the owner's decisions (ADR 0043; CLAUDE.md section 9, item 42). Built and tested like 7.1: 345 frontend tests; in the browser against the real API, the registers, the collection, the balances and the reconciliation, and a CSV read in the page. No package, no migration, no change of the API |
| 7.6 Dashboard | Written on 2026-11-23 with the owner's decisions (ADR 0044; CLAUDE.md section 9, item 43): the layout reference from the owner's prototype, Recharts 3.10.1. 357 frontend tests; in the browser, a Supervisor's figures equal to the 6.1 reports in both themes. No migration, no change of the API |
| 8.1 One-command local run | Written on 2026-11-23 with the owner's decisions (ADR 0045; CLAUDE.md section 9, item 44), on `phase-8-delivery`. The SQL steps and the demo seed run here on LocalDB. On 2026-11-24 the owner ran `docker compose up --build` with the demo on the personal machine (signed in, the dashboard shown) and `dotnet test` with Docker; two fixes to the images came back from that run as their own commit (ADR 0045, "Run with Docker"). Still owed: the page's headers and the http redirect (runbook section 8, row 8.1) |
| 8.2 Performance and browser tests | Written on 2026-11-24 with the owner's decisions (ADR 0046; CLAUDE.md section 9, item 45), on `phase-8-delivery`. A load test in Node with no package (`deploy/load`), the number series' wait and hold in `Server-Timing` (off by default), the idempotency key's row read before the number, and a Playwright journey (`@playwright/test` 1.65.1). On the office machine, on LocalDB at 2,000 bills: every target met with a one-second pause, no drift; the full 10,000 and the journey through nginx are owed by the personal machine |
| 8.3 Release R1 | Written on 2026-11-24 with the owner's decisions, in the session of 8.2: the runbook finished (rolling back a migration, backup and restore with `deploy/backup`, run on LocalDB, and the incident procedures); `docs/traceability.md` with every user story and its tests, held by `TraceabilityTests`; every item of CLAUDE.md section 9 closed or re-filed to `docs/backlog.md` or runbook section 8; the README for R1; every ADR Accepted; the tag `v1.0.0`, annotated and local |
| 9.1 Brand, design tokens and the sign-in page | Written on 2026-11-24 with the owner's decisions (ADR 0047), on `phase-9-interface`. The tokens of both themes, Inter self-hosted (`@fontsource-variable/inter` 5.3.0), our own icons, the BillingRecon mark, the ashline credit and the sign-in page laid out in the reference's own pixels, with its motion; the shared buttons and fields restyled. In the installed Edge: within 4 px of `docs/ui/sign-in.png` in both themes, nothing refused by nginx's policy (applied in the test), the journey of 8.2 passing through it. **Built without the photograph**, which the owner does not have yet; not seen through nginx |
| 9.2 The shell and the shared components | Written on 2026-11-24 with the owner's decisions (ADR 0048), on `phase-9-interface`: the sidebar with icons, its rail and its drawer, the top bar, the ashline credit, a three-way theme pill in place of the drop-down, the tab's title and the heading's focus after a navigation, the shared components restyled from the tokens, skeletons and empty states, a specimen page in Development builds. 392 component tests and 16 browser tests in the installed Edge, the 8.2 journey through the new shell. Some components were left for 9.3 (ADR 0048, built note); not seen through nginx |
| 9.3 The money screens: shared components, bills, credit notes, adjustments | Written on 2026-11-24 (ADR 0049), on `phase-9-interface`, after the owner split 9.3 in two: money as one component, one mapping of every status with its icon, the totals card, banners, the history as a timeline, units and a required mark in fields, pending buttons, the refused field focused; bills, credit notes and adjustments, the no-access and not-found pages. 422 component tests and 31 browser tests in the installed Edge, the 8.2 journey through the new screens. Not seen through nginx |
| 9.3b The cashier's screens and the inbox | Written on 2026-11-24 (ADR 0049, amended), on `phase-9-interface`: the meter of what is left to apply, figures cards and banners on the payment's and refund's pages, the statement's four tiles drawn without a shift, the inbox as cards, units and required marks on the cashier's forms. 422 component tests and 53 browser tests in the installed Edge, the 8.2 journey. Not seen through nginx |
| 9.4 The dashboard and its tooltips | Written on 2026-11-24 (ADR 0050), on `phase-9-interface`, after the owner split 9.4: one tooltip component on two new tokens, opened and closed by pointer, keyboard and tap, on the cards, sparklines, line chart (crosshair by the arrow keys), donut (drawn here) and the top payers' bars; a table for every chart; the period as a segmented control; no inline style; no layout shift. 448 component tests and 60 browser tests in the installed Edge, the 8.2 journey. Not seen through nginx |
| 9.4b The reports, print, the finishing pass and the release | Written on 2026-11-24 (ADR 0050, amended), on `phase-9-interface`: every route on demand within budgets held in CI (backlog B21), the reports' cards, filter bar, framed table with its totals in view and the export's progress, print on A4, a browser suite over all 48 routes with axe (`@axe-core/playwright` 4.14.0, approved), the README. 449 component tests and 261 browser tests in the installed Edge, the 8.2 journey. Not seen through nginx; `v1.1.0` proposed |
| 10.0 The verification debt | Started 2026-11-24 on `phase-10-golive`: `deploy/verify/personal-machine.ps1` runs every check of runbook section 8 and writes `verify-10.0.log`. First run on the personal machine 2026-11-24 at `4c2e75f`: the backend green on SQL Server 2022 but three timing failures of `PiiMaskerTests`, 283 of 288 browser tests through nginx; five defects fixed, one commit each (`a6cf3c3`, `fc7c07d`, `c85f539`, `f89c96b`, `17af1e5`). The second run was never made (see 10.0-close) |
| 10.0-close Record the verification, merge, push, CI | Stage 1 of the owner's eight stages, 2026-11-26. No second run of the script: the owner tested by hand on the personal machine and sent no output, so runbook section 8 records what the owner saw and leaves the rest open with its reason (the load, the journey and the browser suites through nginx among them). The decisions under each built prompt moved to `docs/build-history.md`. Waits for the merge to `main`, the push with `v1.0.0` and `v1.1.0`, and the first CI run |
| 10.1 Users and settings screens | Written on 2026-11-24 (ADR 0051, amending ADR 0030), on `phase-10-golive`: the users and the settings under `/administration` for the TenantAdmin, the prefix change as a request, and a forced password change after creation and after a reset (`Users.PasswordChangeDue`, migration `0021_PasswordChange`, a 15-minute token good for `POST /api/v1/auth/password` alone). Backlog B18 and B24 closed. 461 frontend tests, the routes suite with the six new routes in Edge; the `Database` tests on LocalDB 2019 only (runbook section 8, row 10.1) |
| 10.2 Tenant onboarding | Written on 2026-11-24 (ADR 0052, amending ADR 0045), on `phase-10-golive`: `tenant create`, `tenant suspend` and `tenant resume`, commands of the API's own binary run on the server, audited with no user; the first TenantAdmin with a one-time password and a forced change; a suspension that also refuses tokens issued before it (within 30 seconds); the one-command run in a `Demo` environment, the only one besides Development where the demo runs. Runbook section 17. No migration. The `Database` tests on LocalDB 2019 only (runbook section 8, row 10.2) |
| 10.2b Live search and autocomplete | Written on 2026-11-25 at the owner's request (amending ADR 0039, ADR 0040 and ADR 0018), on `phase-10-golive`: every search box answers while the user types, after 300 ms and from 2 characters (`useLiveSearch`: the lists of patients, payers, service items and users, the outstanding report), the rows kept until the new ones arrive, a stale request cancelled, the address replaced; the record picker an autocomplete in the combobox pattern; no Search or Find button; the MRN and the user's e-mail matched in part (`LikePattern`). No migration, no package. Text search on the document lists is left for later. The `Database` tests on LocalDB 2019 only (runbook section 8, row 10.2b) |
| 10.2c Sign-out on the server | Written on 2026-11-25 at the owner's request (ADR 0057, amending ADR 0031 point 8 and ADR 0032 point 8), on `phase-10-golive`: `Users.SessionVersion` (migration `0022_UserSessions`), the claim `ses`, `SessionGateMiddleware` (401 `session_ended`, the user's row read on every request with a token), `POST /api/v1/auth/logout`; a sign-out, a changed or reset password, a disabling and changed roles end every session of the user; the Sign out buttons tell the API, and the other tabs of the same user sign out too. Verified here without SQL Server 2022 and nginx (runbook section 8, row 10.2c) |
| 10.3 The hospital's records and opening balances | Written on 2026-11-25 (ADR 0053, against the reference hospital of ADR 0058), on `phase-10-golive`: the import of patients and guarantors (matched by MRN, never merged, a dry run), and the opening batch (three CSV files checked row by row and against a trial balance, submitted by one person and posted by the approval of another): opening receivables and advances as bills and payments with `Origin = Opening`, their own series `OBR` and `OBA`, a seventh ledger account `OpeningBalances`, reconciliation identity 12, migration `0023_OpeningBalances`. The whole export of the reference hospital imports to the totals of its trial balance with no drift. The inbox decides a batch. Verified here on LocalDB 2019 (runbook section 8, row 10.3) |
| 10.3b The import screens | Written on 2026-11-25 (ADR 0053, amended), on `phase-10-golive`: Users and settings gains a third tab, Import, for the TenantAdmin: the two steps and the batches, the import of patients and guarantors, the import of opening balances, and a batch's page. Files are checked before Import or Submit is offered, stay in the page only, and their findings can be downloaded. The API's file limits were lowered to what one request through the proxy carries (1 MB). Verified in Edge against the built app; through nginx it is owed (runbook section 8, row 10.3b) |
| 10.4 Printed documents: their data | Written on 2026-11-25 (ADR 0054), on `phase-10-golive`, split by the owner into 10.4 and 10.4b: the hospital's letterhead on the tenant (`GET` and `PUT /api/v1/settings/letterhead`, a form on the Settings screen), the SAC code on a service item and kept on the bill line, and the record of each print (`POST .../prints` on bills, payments and refunds; the first is the original, later ones duplicates; table `DocumentPrints`, insert-only); migration `0024_PrintedDocuments`. The tax split, the kind of tax document and the amount in words are derived at print. `docs/go-live.md` begun, with the accountant's sign-off open. Verified here on LocalDB 2019 and in Edge (runbook section 8, row 10.4) |
| 10.4b The printed pages | Written on 2026-11-25 (ADR 0054, amended), on `phase-10-golive`: `/bills/:id/print`, `/payments/:id/print` and `/refunds/:id/print` (`frontend/src/printing`), each a sheet with the letterhead, reached from its document's page. Print records the print, then opens the browser's dialog; the sheet says Original or Duplicate. The bill is a Tax Invoice (CGST and SGST from the stored tax), a Bill of Supply or a Statement of Opening Balance; the amount in words in lakh and crore. Verified in Edge against the built app; through nginx and on paper it is owed (runbook section 8, row 10.4b), and the accountant's sign-off is open (`docs/go-live.md`) |
| 10.4c The logo, the signature and print settings | Stage 2 of the owner's eight stages, split with its plan on 2026-11-26 into 10.4c-1, 10.4c-2 and a proposal. **10.4c-1 is written (2026-11-26, ADR 0059, amending ADR 0054):** the hospital's logo and its signatory's signature, uploaded as the file itself by the TenantAdmin under the version of the settings (`PUT` and `POST .../remove` at `/api/v1/settings/letterhead/logo` and `/signature`), PNG, JPEG or WebP told by their bytes, SVG refused, kept as insert-only rows of `TenantImages` and named on the tenant by their SHA-256, audited by the hash; read with the token and shown as `data:` URLs, with no change of nginx's policy; the logo at the head and the signature block (image, name, designation) at the foot of the three sheets in boxes of a fixed size, Print waiting for them, a duplicate signed like the original; migration `0025_LetterheadImages`. Verified here on LocalDB 2019 and in Edge (runbook section 8, row 10.4c). **10.4c-2 is written too (2026-11-26, ADR 0059 amended):** print settings for each hospital and document (`GET /api/v1/settings/print`, `PUT .../print/{documentType}`, table `PrintSettings`, migration `0026_PrintSettings`): A4, A5 or an 80 mm roll for a receipt and a voucher, margins, the logo's place and size or pre-printed stationery, seven switches (a bill's SAC column and tax split locked on, with the reason), a footer line and terms; the default is the sheet as it was; a Print tab with a live preview drawn by the components that print; the layout through fixed classes and one stylesheet object, no inline style, nginx's policy unchanged. The cashier's name is a read of its own (`GET .../recorder`), not a field of the payment and refund answers as first planned. Verified here on LocalDB 2019 and in Edge (runbook section 8, row 10.4c). **Part C decided (2026-11-26):** no "Download PDF" now; the browser's dialog saves the PDF (ADR 0059, backlog B37). Stage 2 is written to its end |
| 10.4d The search lens, and Tab in a search | Written on 2026-11-26 (stage 3 of the owner's eight stages; ADR 0039 and ADR 0040 "Amended 2026-11-26"): every search box is `SearchInput`, with a lens that is drawn and not read, the spinner in its place while it searches, and a Clear button outside the Tab order; a list's box sends its search when it is left; a record picker marks its first result, which Tab (and Shift+Tab) takes while moving on, a late answer included unless the user typed again, "No record chosen" with none; the payer of a payment is taken only on a single match. No package, no change of the API. 638 component tests; in Edge under nginx's policy the two search suites and the rest (runbook section 8, row 10.4d) |
| 10.5 Production deployment | Written on 2026-11-25 (ADR 0055), on `phase-10-golive`, on the owner's four answers: one Linux server with Docker Compose (`docker-compose.prod.yml`, `deploy/prod`), secrets as files of the host read by `SecretFiles`, SQL Server 2022 Standard with TDE, Let's Encrypt, nightly full and 15-minute log backups with an off-site copy and a monthly restore drill, and `.github/workflows/release.yml` (a tag builds, pushes and deploys staging; production by hand). The API refuses to start with `READ_COMMITTED_SNAPSHOT` off. Runbook section 19. **Nothing of the deployment has run**: there is no server yet (runbook section 8, row 10.5; `docs/go-live.md`, G4 and G5) |
| 10.6 to 10.7 Go-live (operations; readiness) | Written as prompts on 2026-11-24 from the owner's draft, not started; since 2026-11-25 built against `docs/reference-hospital.md` (ADR 0058), as there is no client yet. They come after the eight stages of 2026-11-26 |
| 10.8 An independent review | Stage 4 of the owner's eight stages. The review was made on 2026-11-26 by a separate agent that had seen none of the session's work, reading only (nothing built or run): `docs/review-2026-11.md`, 14 findings, none Critical, two High, with its ten to fix first. Fixes, one commit each, each approved by the owner first: **R1 fixed** (a credit note is capped by what was billed and not adjusted off; ADR 0025, amended). **R2 fixed** (a payer's reference is recorded once: 422 `payment_reference_already_recorded`, a filtered unique index, migration `0027_PaymentReferenceOnce`; cash is not compared; ADR 0006, amended). **R10 fixed** (old numbers and guarantor references of the import are compared without case, as their unique indexes compare them; the collation was confirmed `SQL_Latin1_General_CP1_CI_AS`; ADR 0053, amended). **All ten of the owner's list are closed: R1, R2, R3, R7, R8, R9, R10, R11 fixed, R5 and R6 accepted and recorded.** **R8 fixed** (the pending-approvals report and the dashboard panel show a role the requests of the documents it may read; ADR 0032, amended). **R3 fixed** (a key left in progress is taken over by a retry after five minutes; expired keys are swept once a day by a hosted service, tenant by tenant; ADR 0006, amended). **R9 fixed** (who created a bill, a credit note, an adjustment or a refund does not approve it, though another user submitted it; a mere editor in between is not caught, said in ADR 0023, amended). **R11 fixed** (tests only: the six "at once" tests hold their requests at a `RaceGate` and assert the loser's one answer; each seen to fail with its guard removed; no race defect found; `docs/testing-strategy.md`). **R7 fixed** (the audit trail of a record is read by who may read the record: `AuditTrailPolicies`, an unknown kind refused; ADR 0032, amended). **R6 accepted and recorded** by the owner (an administrator can lift the posting threshold and put it back; `docs/security.md`, row R4). **R5 accepted and recorded** by the owner (one administrator can be maker and, through a user they create or reset, checker; `docs/security.md`, row R3; to be narrowed by 11.3). The other nine wait for the owner's word, several of them being rules to decide and not code |
| 11.1 Search on the document lists | Stage 5 of the owner's eight stages, on the branch `phase-11-counter`, in two commits. **11.1a built 2026-11-26:** `search` on the five lists (number, patient, payer, exact amount; `DocumentSearch`, `IPartySearch`, `DocumentSearchFilter`), migration `0028_DocumentSearch` (five amount indexes), measured on 100,000 documents (ADR 0036, amended). Verified on LocalDB 2019 only. **11.1b built 2026-11-26:** `DocumentSearchField` on the five list pages, the search in the address (`e2e/docsearch.spec.ts`). Verified in Edge against the built app with no API behind it; by hand through nginx is owed |
| 11.2 The cashier's shift and the day-end closing | Stage 6 of the owner's eight stages, in two commits on the owner's approval of the split (2026-11-27). The requirements were approved "as recommended": cash needs an open shift, a difference is recorded and not posted, a Supervisor or TenantAdmin who is not the cashier approves. **11.2a built 2026-11-27:** `CashierShift` (Payments), `LedgerPostings.ShiftId` and `IShiftCash` (the expected cash from the ledger), the closing with its approval, `GET /api/v1/reports/day-end`, three policies, migration `0029_CashierShifts`, ADR 0060; BR-310 to BR-315, BR-903, US-45 to US-48. The count is one column, not child rows as the plan said (ADR 0060, alternatives). Verified on LocalDB 2019 only. **11.2b built 2026-11-27:** the Shifts section (`frontend/src/shifts`: the list with the user's own shift, opening, counting and closing, a shift's page), the day-end report under Reports, cash on Record payment asking for an open shift and taking its date, and the closing slip (`/shifts/:id/print`) by a fourth kind of print settings, `ShiftClosing` (migration `0030_ShiftClosingSlip`; ADR 0060, amended; US-49). Verified in Edge against the built app; through nginx and on paper is owed |
| 11.3 Two-step sign-in and a forgotten password | Stage 7 of the owner's eight stages, in two commits on the owner's approval of the proposal "as recommended" (2026-11-27): our own QR encoder, the BCL's SMTP client, a 24-hour hold after an administrator's reset (review finding R5), forced enrolment with no grace period. **11.3a built 2026-11-27:** the second step by TOTP on the BCL (`Totp`, `SignInTokens`, the claim `step`, `SecondStepGateMiddleware`, `DecisionGateMiddleware`), the secret encrypted under `TwoStep:EncryptionKey`, ten recovery codes, an administrator's reset, the two pages and the QR code drawn by `auth/qr.ts`; migration `0031_SecondStep`, ADR 0061, BR-608 to BR-610, US-50. Verified on LocalDB 2019 and by component tests; no camera has read the QR code here. **11.3b, the forgotten password, is next** |
| 11.4 The accountant's export | Given on 2026-11-26 as stage 8 of the owner's eight stages, not started |

**Verification is deferred (owner, 2026-11-20): the personal-machine run comes later, and `docs/runbook.md` section 8 is the one running list of every command it owes, in order. Meanwhile every prompt from 2.2 on is committed unverified, one commit per prompt. Phase 3 is written to its end (3.1 to 3.6), and 4.1 (record a payment), 4.2 (allocate a payment to bills), 4.3 (credit notes) and 4.4 (adjustments) are written. Since 2026-11-21 the office machine has the .NET 10 SDK: the whole tip of the branch built and passed there on .NET 10 with LocalDB 2019, and the migrations `0003` to `0015` are in the repository; only the run on SQL Server 2022 in Docker is still owed. 4.5 (reversals), 4.5b (reverse an application of credit) and 4.6 (refunds) are written too, and `0016` and `0017` are in the repository. Phase 4 is written to its end; its branch merge to `main` is the owner's step. 5.1 (users and roles) is written too, and `0018` is in the repository. 5.2 (sign-in and JWT) is written too; it adds no migration. 5.3 was split with its plan: its first half, the role permissions, is written too, with no migration. 5.3b (tenant settings and prefix changes) is written too, and `0019` is in the repository. ADR 0033 built note point 5 was decided on 2026-11-23 (the approval holds the series) and is built as its own commit. 5.4 was split with its plan: its first half, the database logins and grants, is written too, and `0020` is in the repository. 5.4b (HTTP hardening and `docs/security.md` in full) is written too, with no migration; Phase 5 is written to its end, and its merge to `main` is the owner's step. 6.1 (registers and balances), 6.2 (reconciliation and audit trail) and 6.3 (dashboard figures) are written too, with no migration, on `phase-2-billing` like the rest: the backend of R1 is written to its end, and its merge to `main` is the owner's step. 7.1 (the frontend's scaffold and sign-in) is written too, on the same branch, with Node 24 on the office machine. 7.2 (patients, payers and catalogue screens), 7.3 (bill screens), 7.4 (cashier screens) and 7.5 (the checker's inbox, credit notes and adjustments; split with its plan), 7.5b (reports) and 7.6 (dashboard) are written too: the frontend of R1 is written to its end. 8.1 (one-command local run) is written too, on `phase-8-delivery`, and so are 8.2 (performance and browser tests) and 8.3 (release R1, tagged `v1.0.0`): R1 is written to its end. On 2026-11-24 the owner reported the first run on the personal machine: `docker compose up --build` with the demo, and `dotnet test` with Docker, which passed; its output is still to be sent, so the rows of runbook section 8 stay until then. No prompt is left in R1. Phase 9, the interface, has begun: 9.1 (the brand, the tokens and the sign-in page) is written, without the photograph, which is still to come, and so are 9.2 (the shell and the shared components) and 9.3 (the money screens' shared components, bills, credit notes and adjustments); and 9.3b (payments, refunds, the statement and the inbox) is written too; and 9.4 (the dashboard and its tooltips) is written too; and so is 9.4b (the reports, print, the finishing pass and the release): Phase 9 is written, `v1.1.0` proposed; what else follows is in `docs/backlog.md` and the release plan of CLAUDE.md. What was decided for each built prompt is in `docs/build-history.md` since 2026-11-26 (it stood under each prompt below until then).**

## How to use these prompts

- Open the Claude Code session in `C:\aptran2026\BillingRecon\BillingRecon` so that CLAUDE.md loads.
- Give one prompt per session, in order. Do not start a prompt until the one before it is committed.
- Paste the standing preamble first, then the prompt.
- One commit per prompt, one branch per phase, merged to `main` when the phase is green.

### Standing preamble (paste before every prompt)

```
Read CLAUDE.md fully before anything else, then the documents this prompt names.
Follow the working agreement in CLAUDE.md section 6: show me a short plan (files, steps, tests,
risks, any new package) and wait for my approval before you edit code.
Obey the twelve financial rules of section 4 and the conventions of section 5. Do not weaken or
skip a test. Ask before adding any package.
This machine builds and tests .NET 10 and the frontend but has no Docker and little free disk
(CLAUDE.md section 2): run the database tests in a throwaway copy on LocalDB, tell me plainly what
was and was not verified, and give me the exact commands to run on my personal machine.
When the work is done: update CLAUDE.md (sections 3, 7, 9), the affected docs and ADRs, delete the
README placeholder of any module folder that received code, and propose one commit message.
```

---

## Part A. Already built (for reference; do not run again)

**0.1 Project memory.** Create CLAUDE.md for a multi-tenant hospital billing, payments and
accounts-receivable platform in India: purpose, release plan R1 to R5, tech stack (.NET, ASP.NET Core
controllers, EF Core, SQL Server, xUnit, Shouldly, NSubstitute, Testcontainers, NetArchTest),
modular-monolith layout with nine modules, the layer and module dependency rules, the twelve
financial correctness rules, coding conventions, working agreement, and ADR 0000 with the ADR format.

**0.2 Requirements.** Write `docs/requirements.md`: vocabulary (bill, payer, payment, allocation),
roles and permission matrix, R1 scope and exclusions, the numbered BR catalogue (BR-001 to BR-901),
user stories US-01 to US-43 with acceptance criteria, non-functional requirements, R2 to R5 outlines.

**0.3 Domain model.** Write `docs/domain-model.md`: ubiquitous language, module map, value objects,
every aggregate with its invariants citing BR ids, the state machines, transaction boundaries and the
allocation exception, domain events, ports, open questions.

**0.4 Database design and ADRs.** Write `docs/database.md` (conventions, ER diagram, every R1 table,
the number-series lock, ledger accounts and posting templates, balance identities and reconciliation
SQL, idempotency store, permissions, migrations) and ADRs 0001 to 0010.

**1.1 Solution scaffold.** `BillingRecon.sln` with Domain, Application, Infrastructure, Api and four
test projects; `Directory.Build.props` (nullable, warnings as errors, analyzers), central package
management, `.editorconfig`, `global.json`, module folders with README placeholders, architecture
tests for the layer rule. ADR 0002, later ADR 0014 (.NET 10).

**1.2 Cross-cutting infrastructure.** Validated options, Serilog with correlation id and PII masking,
`Result`/`Error` and the RFC 9457 ProblemDetails contract, global exception handler, health checks,
`/api/v1` route prefix, OpenAPI and Scalar in Development, Docker Compose for SQL Server 2022 and Seq.
ADRs 0011 to 0013.

**1.3 Persistence foundation.** `BillingReconDbContext` with conventions (typed ids, tenant query
filter and write guard, audit stamps, rowversion, explicit decimal and string sizes), the banned-API
list, `IClock`, the `Tenant` aggregate and migration `0001_Tenants`. ADR 0015.

**1.4 Test infrastructure.** Testcontainers SQL Server 2022 with Respawn, the `Database` collection,
`DatabaseApiFactory` acting for a tenant, Bogus `TestData`, the database-backed tests the persistence
step promised, the module dependency rule with its listed exceptions. ADR 0016.

**1.5 CI and image.** GitHub Actions workflow (restore with audit, format gate, build, all tests,
coverage, vulnerability report, migration script, image build and smoke run), the API Dockerfile,
Dependabot, LF line endings. ADR 0017.

**2.1 Money and the shared kernel.** `Money`, `UnitPrice`, `Quantity`, `Percent` as readonly record
structs with `Of`/`TryOf`, `Money.Round` (away from zero, 2 places), EF converters, `Tenant.
PostingApprovalThreshold` as `Money?`, tests. ADR 0004 accepted.

**2.2 Patients and payers.** `Patient` (MRN unique per tenant) and `Payer` (self-pay only, at most one
per patient, guarantor allowed) with deactivate and reactivate; the use-case contracts
`ICommandHandler`/`IQueryHandler`, FluentValidation, `IUnitOfWork`, paging, ETag and `If-Match`;
controllers, repositories, read services; unit, API and SQL Server tests; `docs/api.md`. ADR 0018.

---

## Part B. Remaining prompts

Each prompt is kept as it was given. What the owner decided for a built prompt, and how it was built where that
differs, is in `docs/build-history.md` under the same heading (moved there on 2026-11-26, Prompt 10.0-close).

### Phase 2. Reference data and bill drafts (branch `phase-2-billing`)

#### 2.2-close. Verify and commit Prompt 2.2

```
Prompt 2.2 (patients and payers) is written but uncommitted, and CLAUDE.md section 9 item 9 is out
of date: the migration now exists as 20261018182819_0002_SyncCurrentModel (EF Core 10.0.12).
1. Compare that migration and the model snapshot with docs/database.md section 3.2 and with the
   entity configurations. Report any difference. Do not rename or edit the migration; record its
   real name in docs/database.md section 8.
2. Give me the exact commands for my personal machine (dotnet tool restore, dotnet build,
   dotnet format --verify-no-changes, dotnet test with Docker running) and wait for my output.
3. From my output: fix real defects, then update CLAUDE.md section 9 (items 1, 8, 9), section 2
   (the SDK list I send) and README.
4. docs/build-prompts.md renumbers the remaining phases (ledger and audit are now Phase 3, identity
   stays Phase 5). Correct the "Phase 4" labels in CLAUDE.md section 9 to match.
5. Propose the commit "Prompt 2.2: patients and payers (ADR 0018)". I will push main and the branch
   and watch the first CI run; then we close section 9 item 7 together.
Done when: build and every test are green on .NET 10 with Docker, and the work is committed.
```

#### 2.3 Service catalogue

```
Read docs/domain-model.md section 4.3 and docs/database.md section 3.3 (ServiceItems).
Build the ServiceItem reference data in the Billing module, shaped exactly like the Payers slice
(ADR 0018): aggregate with code unique per tenant, name, default UnitPrice, default tax rate
(Percent), Active/Inactive status; create, update, get, list (paged, filter by status and search
text), deactivate, reactivate; ETag and If-Match; no DELETE.
Tests: domain, validators, handlers, API tests in the Database collection, tenant isolation.
Migration 0003_ServiceItems. Add the endpoints to docs/api.md.
Done when: a clerk can maintain the catalogue through the API and another tenant gets 404.
```

#### 2.4 Bill drafts

```
Read docs/requirements.md BR-201 to BR-205 and US-20, US-23; docs/domain-model.md sections 4.4,
5.1 and 6; docs/database.md section 3.3 (Bills, BillLines).
Build the Bill aggregate with BillLine children, Draft and Voided states only:
- line arithmetic per BR-202 through Money.Round, totals per BR-203 as sums of rounded lines;
- a line may name a ServiceItem (defaults for price and tax come through a Billing contract) or be
  free text;
- bill for an active patient, owed by an active payer (BR-102, BR-105, BR-106), checked through
  the Payers module's public contracts, never its entities;
- dates per BR-205 using IClock.TodayIst and the tenant settings;
- create draft, replace lines, update header, void with reason, get, list (paged, filters);
- the unit of work marks the root modified when only lines change, so the row version is checked
  and advanced (CLAUDE.md section 9 item 5).
Tests: property-style arithmetic tests with awkward values (0.005, 1/3 quantities, 100% discount),
state machine, API and SQL Server tests, CHECK constraints. Migration 0004_Bills.
Done when: drafts can be created, edited and voided, have no number and no ledger effect.
```

### Phase 3. The posting engine (branch `phase-3-posting`)

#### 3.1 Fiscal year and gapless number series

```
Read ADR 0008, BR-004, BR-801, BR-802 and docs/database.md section 4.
Build FiscalYear and DocumentNumber value objects (Common), the NumberSeries table and the
INumberSeries port. The implementation takes the next number inside the caller's transaction with
UPDLOCK, HOLDLOCK on the (TenantId, DocumentType, FiscalYear) row, creating the row on first use
with the tenant's default prefix. Format PREFIX/YY-YY/NNNNNN, at most 16 characters. Raw SQL is
needed here: sanction it in BannedSymbolsTests style, one file only.
Tests on real SQL Server: 31 March and 1 April boundaries, 50 parallel takers produce 1..50 with
no gap and no duplicate, a rolled-back transaction leaves no gap, the gapless verification query
of section 4.3. Migration 0005_NumberSeries. No document uses it yet.
Done when: the concurrency test passes repeatedly.
```

#### 3.2 Idempotency for financial POSTs

```
Read ADR 0006, BR-803 and docs/database.md section 6.
Build IIdempotencyStore and the API filter/attribute for financial POSTs: the key is a UUID
scoped to the tenant, reserved before the business transaction, completed inside it with the
request hash and the stored response; replay returns the original response unchanged; same key
with another payload is 422 idempotency_key_reuse; in flight is 409 idempotency_in_progress;
missing or malformed is 400 idempotency_key_missing. Retention 30 days (state the clean-up, do not
schedule it yet). Prove it on a test-only probe endpoint in the integration tests, including two
simultaneous requests with one key. Migration 0006_IdempotencyKeys.
Done when: the probe endpoint shows all four outcomes against SQL Server.
```

#### 3.3 Receivables ledger

```
Read ADR 0005, BR-401 to BR-406 and docs/database.md sections 3.7 and 5 (all of it).
Build the Receivables module: LedgerPosting and LedgerEntry (append-only, balanced, six accounts
with subject and payer rules of BR-403), the PayerBalances cache, and the IReceivablesLedger port
that business modules call inside their own unit of work. Mark the ledger entities IAppendOnly: the
EF-layer guard that refuses updates and deletes of append-only tables exists since Prompt 3.4
(ADR 0020).
Build the reconciliation query of section 5.6 as a query service that returns drift rows.
Create the zero PayerBalances row when a payer is created (through the Receivables port) and a
migration step for payers that already exist.
Tests: unbalanced posting refused, negative subject balance refused, update/delete of a ledger row
refused, reconciliation returns nothing when clean and reports a seeded drift.
Migration 0008_Receivables.
Done when: a posting written through the port reconciles to zero drift.
```

#### 3.4 Domain event dispatch and the audit trail

```
Read BR-701 to BR-703, docs/domain-model.md sections 4.13 and 8, docs/database.md section 3.8.
Make the unit of work dispatch the aggregates' domain events in-process before commit, stamping
tenant and audit columns first and reading the clock once per use case (CLAUDE.md section 9 item 5).
Build the Audit module: AuditRecord (append-only), IAuditWriter, a generic subscriber over
IDomainEvent that writes who, when, tenant, entity, action, before/after JSON and correlation id in
the same transaction. Wire the events Patient, Payer, ServiceItem and Bill already raise.
Tests: every existing state change writes exactly one audit row; a rolled-back use case writes
none; audit rows cannot be updated or deleted. Migration 0007_Audit.
Done when: the audit trail of a patient shows register, update, deactivate with correlation ids.
```

#### 3.5 Post a bill

```
Read BR-206, BR-208, BR-006, BR-007, US-21, US-25, docs/domain-model.md sections 5.1 and 7,
docs/database.md section 5.3.
Build POST /api/v1/bills/{id}/post (Idempotency-Key and If-Match required): validate, take the
gapless number for the fiscal year of BillDate, post Dr Receivable(bill) / Cr Revenue for Total,
set BalanceDue = Total, status Posted (Settled when Total is zero), freeze the bill, write the
audit record: all in one IUnitOfWork commit. A posted bill refuses edit and void.
Tests: the happy path asserts number, ledger, caches, audit and zero reconciliation drift; a
failure injected at each step leaves no number consumed, no ledger and no audit rows (US-25); two
simultaneous posts of the same bill give one success and one 409; replay returns the same response.
Migration 0009_BillPosting.
Done when: every scenario ends with zero drift rows and a gapless series.
```

#### 3.6 Maker-checker approvals

```
Read BR-603 to BR-606, BR-207, US-22, docs/domain-model.md sections 4.9 and 5.4,
docs/database.md section 3.6.
Build ApprovalRequest (Common): submit, approve, reject with comment, list pending; maker not
equal to checker enforced in the domain and by a CHECK constraint; at most one pending request per
document. Approval handlers live with their documents (ADR 0003 amendment). First use: a bill
with Total at or above Tenant.PostingApprovalThreshold goes to PendingApproval, is locked, gets no
number, and the checker's approval is the posting (same transaction as 3.5); rejection returns it
to Draft. Until Identity exists, tests act as two users with the existing test identity helpers.
Migration 0010_Approvals.
Done when: US-22 passes, including approval by the maker being refused.
```

### Phase 4. Payments and corrections (branch `phase-4-payments`)

#### 4.1 Record a payment

```
Read BR-301, BR-302, BR-309, US-30, docs/domain-model.md section 4.6, docs/database.md section 3.4.
Build Payment in the Payments module: amount > 0, method (Cash, Card, UPI, BankTransfer, Cheque),
reference required except for Cash, date per BR-205, active payer only. Recording is posting:
gapless payment number, Dr Cash / Cr UnappliedCash(payment), UnappliedAmount = Amount, audit, one
transaction, Idempotency-Key required. Get and list. Migration 0011_Payments.
Done when: US-30 passes with zero drift.
```

#### 4.2 Allocate a payment to bills

```
Read BR-303, BR-304, BR-208, US-31 and docs/domain-model.md section 6 (the allocation exception),
docs/database.md section 5.4.
Build allocation: one request may allocate to several posted bills of the same payer; caps per
BR-303; paired entry Dr UnappliedCash(payment) / Cr Receivable(bill); Payment.UnappliedAmount,
Bill.BalanceDue, bill status and PayerBalances change in the same transaction. Payments reaches
bills only through Billing's public contracts. Lock order is fixed and documented to avoid
deadlocks.
Tests: over-allocation refused, cross-payer refused, two cashiers allocating the last rupee of one
bill at once (one wins, one gets 409), partial then full settlement moves the status
Posted -> PartiallyPaid -> Settled, zero drift after each.
Done when: US-31 passes under the concurrency test.
```

#### 4.3 Credit notes

```
Read BR-210 to BR-214, US-24, docs/domain-model.md section 4.5, docs/database.md section 3.3.
Build CreditNote with lines and allocations: against one posted bill, caps per BR-210 and BR-211,
draft then submit (maker-checker, BR-604); approval takes the gapless number, posts
Dr Revenue / Cr CreditAvailable, then applies min(Total, Bill.BalanceDue) to the bill; the rest
stays as AvailableAmount and can be applied to another posted bill of the same payer.
Migration 0013_CreditNotes.
Done when: US-24 passes with zero drift, including a credit note larger than the balance due.
```

#### 4.4 Adjustments

```
Read BR-501 to BR-503, docs/domain-model.md section 4.8, docs/database.md section 3.5.
Build Adjustment (Discount, WriteOff, Rounding under Rs 1.00): against one posted bill, amount
capped by BalanceDue at posting, reason required, maker-checker, gapless number,
Dr Adjustments / Cr Receivable(bill). Raise domain-model open question 1 with me before coding.
Migration 0014_Adjustments.
Done when: an approved write-off settles a bill with zero drift.
```

#### 4.5 Reversals

```
Read BR-305, BR-306, US-32, US-33, docs/domain-model.md section 5.3.
Build allocation reversal and payment reversal as maker-checker actions: mirror ledger pairs,
caches and bill status restored, original rows marked Reversed, nothing deleted. Payment reversal
first reverses every active allocation, then posts Dr UnappliedCash / Cr Cash; refused when a
refund exists. A rejected reversal leaves the document as it was.
Done when: US-32 and US-33 pass and a fully reversed payment leaves every balance where it began.
```

#### 4.5b Reverse an application of credit

```
Read BR-213, docs/domain-model.md section 4.5, ADR 0025 point 5 and ADR 0027.
Build the reversal of a CreditNoteAllocation as a maker-checker action, shaped exactly like the reversal of a
payment allocation (ADR 0027): the cashier asks with a reason (202), a second person approves or rejects;
approval posts the mirror pair Dr Receivable(bill) / Cr CreditAvailable(creditNote), raises
CreditNote.AvailableAmount and Bill.BalanceDue, moves the bill's status back, and marks the row Reversed.
Nothing is deleted. Decide with me first whether the application a credit note makes to its own bill at
posting may be reversed, or only later applications to other bills. Migration 0016_CreditApplicationReversals
(refunds and users move up again).
Done when: an application reversed leaves the credit available again and the bill owed again, with zero drift.
```

#### 4.6 Refunds

```
Read BR-307, BR-308, US-34, docs/domain-model.md section 4.7.
Build Refund from one source (a payment's unapplied amount or a credit note's available amount):
cap checked at posting, method and reference rules of BR-301, active payer, maker-checker, gapless
number, Dr UnappliedCash or CreditAvailable / Cr Cash. Not reversible. Migration 0017_Refunds.
Then write one end-to-end scenario test that walks bill -> part payment -> credit note ->
adjustment -> allocation reversal -> refund and asserts zero drift and gapless series at the end.
Done when: US-34 and the scenario test pass.
```

### Phase 5. Identity and security (branch `phase-5-identity`)

#### 5.1 Users and roles

```
Read BR-601, US-01, docs/domain-model.md section 4.12, docs/database.md section 3.1.
Build User with roles (TenantAdmin, Supervisor, BillingClerk, Cashier, Viewer): e-mail unique per
tenant, enable/disable, password hashed with a slow salted BCL algorithm, admin password reset.
Users and Roles tables, the development user row, and the foreign keys from CreatedByUserId and
UpdatedByUserId that were waiting for it. Audited. Migration 0018_Users.
Done when: US-01 passes.
```

#### 5.2 Sign-in and JWT

```
Read BR-602, BR-002, US-02, US-04, ADR 0009. Write an ADR for token issuing first.
Build POST /api/v1/auth/login issuing a 60-minute JWT with tenant id, user id and roles; JWT
validation; ITenantContext and ICurrentUser resolved from the token and from nowhere else; the
development identity stays for Development only. LoginUserLookup is the one sanctioned use of
IgnoreQueryFilters (add it to BannedSymbolsTests). Default-deny authorisation on every controller.
Tests: no token is 401, disabled user refused, a token of tenant A reading tenant B by id gets 404
on every resource built so far.
Done when: every existing API test runs with real tokens.
```

#### 5.3 Role permissions and tenant settings

```
Read the permission matrix in docs/requirements.md section 1, BR-603, BR-607, US-03.
Apply the matrix as named policies on every endpoint, with one table-driven test that walks every
endpoint with every role. The checker must hold Supervisor or TenantAdmin. Build the tenant
settings endpoints (approval threshold, back-date window, default due days, default prefixes;
prefix change is maker-checker, BR-802), audited.
Done when: the matrix test passes and US-03 passes.
```

#### 5.3b Tenant settings and prefix changes

```
Read BR-607, BR-802, BR-604, US-03, ADR 0008, ADR 0023, ADR 0032 and the decisions of 5.3 above.
Build GET and PATCH /api/v1/settings (TenantAdmin, If-Match): the approval threshold (empty or above
zero), the back-date window and the default due days (0 to 365), applied from the next request and
audited. Build the prefix change: a TenantAdmin asks for a new prefix of one document type, a
Supervisor or TenantAdmin who is not the maker approves or rejects; the approval changes the default
and the series of the current fiscal year; prefixes stay distinct and 1 to 3 characters [A-Z0-9].
Migration 0019_PrefixChanges. The trail of the tenant's settings is the TenantAdmin's alone.
Done when: US-03 passes and the matrix test names the new endpoints.
```

#### 5.4 Database logins and hardening

```
Read docs/database.md section 7 and the open review findings in CLAUDE.md section 9 item 6.
Create the application and migrator logins with their grants (no DELETE on financial tables,
insert-only audit and ledger, SELECT on __EFMigrationsHistory); run the database tests as the
application login. TLS and HSTS, allowed hosts, server header removed, rate limit on login, CORS
for the frontend origin. Write docs/security.md in full, with a threat model.
Done when: a test proves the application login cannot delete a bill or update an audit row.
```

#### 5.4b HTTP hardening and the security document

```
Read the 5.4 decisions above, ADR 0031 point 12, the review finding 2.4 in CLAUDE.md section 9 item 6,
the security row of docs/requirements.md section 5 and docs/security.md.
Propose first, for my approval: where TLS ends (the API or a proxy in front of it) and what that means
for HSTS, HTTPS redirection and forwarded headers; how allowed hosts are configured and checked at
startup; the rate limit of sign-ins (the built-in limiter, its partition and limits, the 429 answer as a
ProblemDetails with its errorCode and Retry-After); CORS for the frontend origin from configuration.
Then build them with the server header removed, and write docs/security.md in full with a threat model.
No new package. Keep the CI smoke run of the image green.
Done when: a test proves each header and refusal, and docs/security.md has no "planned" row left
without a prompt or an accepted risk.
```

### Phase 6. Reporting (branch `phase-6-reporting`)

#### 6.1 Registers and balances

```
Read BR-901, US-40, US-42. Build read-only query services in the Reporting module (it never
writes): bill register, payment register, daily collection by method for an IST business date,
outstanding by payer, unapplied cash and available credit, pending approvals. Paged, filterable,
tenant-filtered, every figure derived so that it ties to ledger entries. Tests compare each report
with the ledger after the Phase 4 scenario.
Done when: US-40 and US-42 pass.
```

#### 6.2 Reconciliation and audit trail

```
Read US-41, US-43, docs/database.md section 5.6. Expose the reconciliation report (cache versus
ledger, drift rows) and the audit trail of any document. Add a test helper that asserts zero
drift, and call it at the end of every financial integration test that does not already do so.
Complete docs/api.md with every endpoint and the full error-code catalogue.
Done when: US-41 and US-43 pass and a seeded drift is reported.
```

#### 6.3 Dashboard figures

```
Read BR-901, US-40 to US-43, docs/database.md sections 5.5 and 5.6, and the Reporting query
services of 6.1 and 6.2.
The dashboard is new R1 scope: first propose the additions to docs/requirements.md (one user
story with acceptance criteria, and its row in the permission matrix) and wait for my approval.
Build read-only query services in the Reporting module (it never writes) and
GET endpoints under /api/v1/dashboard, for a period given as from/to IST business dates
(default: the last 30 days), tenant-filtered:
- summary: total billed (bills posted in the period), amount collected (payments recorded in
  the period), outstanding dues, unapplied cash, pending approvals, reconciliation drift rows;
  each with the same figure for the previous period of equal length and the change in percent
  (null when the previous figure is zero);
- trend: billed and collected per business date or per week, cumulative within the period, and
  the daily series each summary card needs for its sparkline;
- ageing: outstanding BalanceDue of posted bills by days past DueDate as of TodayIst, in the
  buckets 0-30, 31-60, 61-90 and over 90, with amount and share;
- top outstanding payers (first 5), recent payments (last 5), pending approvals (first 3) and
  drift rows (first 3), reusing the 6.1 and 6.2 services, not copying their SQL.
Raise with me before coding: outstanding dues and unapplied cash "as of" a past date can be
derived from ledger entries, and pending approvals from the request timestamps, but drift rows
have no history. Propose either no trend for that card or a snapshot table, with the cost of each.
All amounts leave the API as exact decimals; no rounding to lakh on the server.
Tests: after the Phase 4 scenario, every summary figure equals the figure derived from ledger
entries; the ageing buckets sum to outstanding dues; a bill due today falls in 0-30; period
boundaries at IST midnight; another tenant's data never appears; empty period returns zeros.
Add the endpoints to docs/api.md. No migration unless I approve the snapshot table.
Done when: every dashboard figure ties to the ledger and the ageing total equals outstanding dues.
```

Merge to `main`. The backend of R1 is complete here.

### Phase 7. Frontend (branch `phase-7-frontend`)

Node 22.22 or newer is needed (React Router 8's minimum, `engines` in `frontend/package.json`); the office machine has
Node 24.22.2 since 2026-11-23 (CLAUDE.md section 2).

#### 7.1 Scaffold and sign-in

```
Propose the frontend stack for my approval first (React, TypeScript, Vite; routing, data fetching,
forms and a component approach, each with a reason), then write the ADR. Build frontend/: login,
token handling, a typed API client generated from /openapi/v1.json, one error component that reads
ProblemDetails errorCode, role-aware navigation, lint and test set-up, and a CI job.
Theming is part of the scaffold. Propose it in the stack ADR: design tokens as CSS variables
(surface, text, border, accent, success, warning, danger, and a fixed series palette for
charts), a light and a dark set switched by data-theme on <html>, and no raw colour value in any
component (add a lint rule or a test that fails on one). The user chooses System, Light or Dark
from the top bar; System follows prefers-color-scheme and reacts when it changes; the choice is
kept in localStorage and applied by an inline script in index.html before React mounts, so there
is no flash. Name the chart library in the same ADR, with the reason, and say how its colours
follow the theme. Text contrast meets WCAG AA in both themes.
Say in the stack ADR where the browser keeps the token and why (docs/security.md section 10,
row S5): memory, sessionStorage or localStorage, weighed against a script injected into the page.
Done when: I can sign in and see a role-appropriate empty shell, and the empty shell switches
between the three settings without a reload and without a flash on a hard refresh.
```

#### 7.2 Patients, payers and catalogue screens

```
Build list, create, edit, deactivate and reactivate screens for patients, payers and service
items, with paging and search. Send If-Match on every change and show a clear "someone else
changed this record" message on 409. Component tests for the forms.
```

#### 7.3 Bill screens

```
Build the bill draft editor (lines from the catalogue or free text, live totals that mirror
BR-202 and BR-203 but always display the server's figures after save), void, post with a generated
Idempotency-Key that is reused on retry, and the bill view with status, balance and audit trail.
```

#### 7.4 Cashier screens

```
Build record payment, allocate to bills (outstanding bills of the payer, caps enforced in the
form and by the server), request reversal, draft refund, and the payer statement (outstanding,
unapplied cash, available credit).
```

#### 7.5 Approvals and reports

```
Build the checker's inbox (approve, reject with comment; hidden from the maker), credit note and
adjustment drafting, and the six R1 reports with date filters and CSV export.
Done when: the Phase 4 scenario can be performed entirely in the browser by three users.
```

#### 7.5b Reports

```
Read the reports in docs/api.md section 8.15 and BR-901, and the screens ADRs 0039 to 0042.
Build the six R1 reports of BR-901 under /reports: the bill register, the payment register with
the daily collection by method, outstanding by payer with a payer's balance, unapplied cash and
available credit, pending approvals, and the reconciliation. Each with its filters in the address
(the registers a period of business dates in India, the current month by default), its totals over
every page as the API answers them, exact amounts with Indian grouping, and a CSV export of every
row of the filter, not only the page shown, with the totals and the period.
Decide with the plan where the CSV is written (in the browser from every page, or by the API).
Tests: each report's filters sent and kept in the address, its totals the server's, the CSV's rows,
amounts and escaping, and access by role.
Done when: every report after the Phase 4 scenario shows the figures the API answers, and its CSV
opens in a spreadsheet with the same rows and totals.
```

#### 7.6 Dashboard

The layout reference `docs/ui/dashboard.png` must be in the repository before this prompt is given.

```
Read the dashboard user story in docs/requirements.md, the dashboard endpoints in docs/api.md
and the theming section of the frontend ADR. The layout reference is docs/ui/dashboard.png;
follow its structure, not its sample data.
Build the dashboard as the landing page for the roles the permission matrix allows:
- period picker (last 7, 30, 90 days, custom), IST business dates;
- six summary cards: total billing, amount collected, outstanding dues, unapplied cash, pending
  approvals, reconciliation issues. One component, one colour token per card: the background is
  the token mixed into the card surface (color-mix, a stronger share in dark), the icon tile is
  the solid token, and the sparkline is a 2px line with a fill beneath it fading to transparent,
  running flush to the card's bottom edge. The change against the previous period is coloured by
  meaning, not direction: a fall in outstanding dues, unapplied cash, approvals or issues is good;
- billing against collections line chart (collections dashed, tooltip with both values);
- ageing donut with the total in the centre and a legend showing amount and share;
- recent payments, top outstanding payers (with proportional bars), pending approvals and
  reconciliation exceptions, each with a "view all" link to the screen built in 7.4, 7.5 or 7.5b.
  The approvals panel follows the checker's-inbox rule of 7.5 (a maker does not see a Review
  action on their own request).
Money: cards may show a compact figure (₹12.8L) with the exact amount in a tooltip and for
screen readers; tables show exact amounts with Indian grouping, right-aligned, tabular digits.
Every figure is the server's; the client computes nothing but the compact display form.
Each panel has its own loading skeleton, empty state and error state (the ProblemDetails
component of 7.1), so one failed request does not blank the page. Charts carry a text
alternative. The grid reflows from six cards per row down to one.
Tests: component tests for the card (good and bad direction, null change, zero), the compact
money formatter (99,999 / 1,00,000 / 12,80,000 / 1,00,00,000), each panel's empty and error
states, and a test that renders the page in both themes and fails on any colour not taken from
a token.
Done when: the dashboard after the Phase 4 scenario shows the same figures as the 6.1 reports,
in both themes, for a Supervisor, and a role without the permission does not see it.
```

Merge to `main`.

### Phase 8. Delivery (branch `phase-8-delivery`)

#### 8.1 One-command local run

```
Add api and frontend services to docker-compose.yml (frontend served by nginx, API behind it),
a migration step that runs as the migrator login, and an opt-in demo seeder: one tenant, five
users (one per role), patients, payers, a catalogue and a week of bills and payments.
nginx ends TLS with a local certificate and is the API's known proxy (docs/runbook.md section 11):
outside Development the API refuses plain HTTP. Give the frontend's pages their own headers: a
content security policy, frame-ancestors 'none' and nosniff (docs/security.md section 10, rows S5
and E6).
Done when: docker compose up on a clean machine gives a working, signed-in demo.
```

#### 8.2 Performance and browser tests

```
Read the performance row of docs/requirements.md section 5. Write a load test for bill posting
and payment recording at 50 concurrent users, report p95 and number-allocation time, and fix what
misses the target. Add a small Playwright suite for the main journey (ask before adding it).
Done when: p95 is under 500 ms and reconciliation shows zero drift after the load run.
```

#### 8.3 Release R1

```
Finish docs/runbook.md (migrations outside Development, backup and restore, incident steps,
reconciliation drift procedure), bring README, CLAUDE.md and every ADR status up to date, close or
re-file every item of CLAUDE.md section 9, walk every user story US-01 to US-43 and list the test
that proves it, then tag v1.0.0.
Done when: the traceability table has no empty row.
```

Merge to `main`; R1 is the tag `v1.0.0`.

### Phase 9. The interface (branch `phase-9-interface`)

R1 is released and every screen works, but the screens are plain. Phase 9 gives the whole application one designed
interface, at the level of the owner's reference for the sign-in page, **without changing what any screen does**. Asked
for by the owner on 2026-11-24: "make all the screens one of the best, like this", and the company's logo and name in the
product. Four prompts, one per session, in order: the brand, the tokens and the sign-in page (9.1); the shell and the
shared components (9.2); the money screens (9.3); the dashboard, the reports and the finishing pass (9.4). Each begins
with a plan and ends with screenshots the owner can judge by eye.

**Before 9.1, in the repository** (9.1 stops without the first two):

1. `docs/ui/sign-in.png`: the reference, 1672 x 941, as the owner gave it on 2026-11-24. **Not in the repository yet.**
2. `frontend/src/assets/brand/signin-hospital.webp`: the photograph of the reference on its own (the hospital building,
   no tint, no text, no card), at least 2,400 px wide, a photograph the owner has the right to use. **Not in the
   repository yet.** The page must stay legible without it: the tint, not the photograph, carries the text.
3. `frontend/src/assets/brand/ashline-logo.svg`: the company's mark. **In the repository since 2026-11-24**, copied path
   for path from the company's site.

**The company** is **ashline**, the owner's studio (https://ashish-singh-ai-studio.vercel.app/). Its logo there is drawn
inline: a violet hexagon (`#7c3aed`) holding a white A whose crossbar runs out to a dot (`#1a1033` on a light ground,
`#c4a1ff` on a dark one), followed by the name in lower case, "ash" at medium weight in the text colour and "line" at
regular weight in the violet (`#7c3aed` on light, `#b78cff` on dark). When the pointer is over it the dot travels in along
the line (0.35 s, not with reduced motion). The file above is that mark with its light colours; the name is set as text
beside it, as on the site. The product is credited as "A product of ashline" and "BillingRecon by ashline".

**What the reference shows** (measured by eye on its 1672 x 941 frame; 9.1 measures it properly and says so in its plan):
the card spans about 41% of the width, from about 52% to 93% across and from about 9% to 93% down, with corners of about
28 px; its fields are about 575 px wide and 56 px high with corners of about 12 px; the button is about 58 px high; the
left block starts about 5.5% in, with a mark of about 120 px. The type looks like Inter.

**The bar of Phase 9.** Every prompt of the phase is held to all of it:

1. **One system.** Every colour, gradient, shadow, radius, space, type size, duration and easing is a token of
   `frontend/src/theme/tokens.css`, in a light and a dark set. No screen writes one of its own: the colour scan of ADR 0038
   covers every new file, and gradients, shadows and masks are tokens too.
2. **Both themes, WCAG 2.2 AA.** Text at 4.5:1; large text, controls and focus rings at 3:1; the contrast test holds every
   pair in use. Nothing is said by colour alone: a status has an icon and a word. Every control works by keyboard, shows
   its focus and is at least 24 px to hit. Windows forced colours stay usable.
3. **Dynamic, with a purpose.** Motion says what happened: something arrived, changed, is pending, was refused. Durations
   and easings are tokens (about 120, 200 and 320 ms). Only `transform` and `opacity` are animated. Nothing loops but the
   brand's waves. With `prefers-reduced-motion: reduce` nothing moves and states change at once. Nothing shifts the layout
   after the first paint.
4. **Fluid.** From 360 to 1,920 px wide with no sideways scroll of the page; sizes scale with the window (`clamp`); layouts
   change at breakpoints that are tokens; at 200% zoom nothing is clipped.
5. **Fast and calm.** The first paint has the theme, the layout and the type's metrics: no flash, no jump. The typeface is
   self-hosted and preloaded, images have their sizes, a route's code loads when the route is visited, and loading is a
   skeleton of what is coming.
6. **Nothing from elsewhere.** No font, script, style, image or request from another origin, and no inline style attribute
   or script but the pinned theme script. nginx's content security policy (`frontend/deploy/nginx.conf`) stays as it is; a
   browser test applies that exact policy to the built app and fails on any refusal, because the office machine has no
   nginx.
7. **No change of behaviour.** The API, the routes, the policies and what each screen sends stay as they are. So do the
   labels, names and headings that the tests, the browser journey of 8.2 and `docs/traceability.md` use: a test title the
   table names is not renamed.
8. **Proven and shown.** Every existing test passes unweakened; what is new has tests; each prompt leaves screenshots of
   its screens in both themes in `docs/ui/`, and an ADR.
9. **No package without asking.** Our own components on CSS Modules stay (ADR 0038: no UI kit). A package the plan argues
   for (icons, motion, an accessibility check) is asked for with its name, a version at least a week old, its licence and
   its size.

**Not in this phase** (each sends a request no screen sends today, or is a feature; ask for it as its own prompt): a count
of waiting approvals in the navigation; a dashboard that refreshes itself; the screens for users and settings (backlog
B18); a command palette; languages other than English.

#### 9.1 Brand, design tokens and the sign-in page

```
Read ADR 0038 (stack, theming, the raw-colour rule, the pinned theme script), ADRs 0039 to 0046,
docs/security.md section 10 (rows S5, E6 and I14), frontend/deploy/nginx.conf (the headers nginx
sends since 8.1) and "The bar of Phase 9" in docs/build-prompts.md.

The reference is docs/ui/sign-in.png, 1672 x 941: match it exactly, in layout, proportions,
colours, type sizes, icons and wording. The photograph is
frontend/src/assets/brand/signin-hospital.webp and the company's logo is
frontend/src/assets/brand/ashline-logo.svg: use both files as they are, do not redraw the logo,
and load nothing from another origin (the content security policy). If the reference or the
photograph is not in the repository, stop and tell me: never substitute, fetch or generate one.

Show me the plan first:
- what you measured on the reference: each element's position and size on the 1672 x 941 frame
  and as a share of it, the type sizes and weights, the radii, the colours;
- the tokens for light and dark (the brand blues, surfaces, text, borders, states, the gradients,
  the shadows, the photograph's mask, radii, spacing, the type scale, durations and easings) with
  the contrast of every text and surface pair in use (WCAG 2.2 AA). White text over the
  photograph is judged against the tint alone, at the lightest point any text sits on;
- the typeface (the reference looks set in Inter: confirm it or name a closer open-licence face),
  its licence and weights, how it is self-hosted (woff2 in the repository, subset, preloaded, a
  fallback with matched metrics so that nothing moves when it arrives), that it has the rupee
  sign and tabular figures, and what it weighs;
- the icons as our own inline SVG on one grid, drawn with currentColor (ask before any package);
- what changes in index.html and in nginx's headers, if anything. The inline theme script and its
  pinned hash should stay as they are; if they cannot, the hash in nginx.conf and its test move
  in the same change.

Build the sign-in page as the reference shows it, measured on its frame and scaling with the
window:
- the background: the photograph over the left two thirds, fading out to the right, under a blue
  tint that runs from deep navy at the left to the pale page colour at the right; two waves along
  the bottom edge; a pale right side with one soft curved shape;
- left, on the tint: the BillingRecon mark (a blue cross with rounded arms and a darker cross
  inside it), "BillingRecon" in heavy white type, "Reconcile. Verify. Resolve." beneath it; then
  three lines with outline icons: "Secure hospital billing reconciliation" (a shield with a
  tick), "Trusted financial insights" (a bar chart), "Better care through accurate billing" (a
  handshake);
- right: a white card with large rounded corners and a soft shadow, about 41% of the width; a
  blue pulse line at its top; "Sign in to BillingRecon"; "Access your hospital's billing
  dashboard"; the fields Hospital code, E-mail address (placeholder you@hospital.org, an envelope
  at its right) and Password (placeholder "Enter your password", a show/hide button at its right
  with aria-pressed), each label with a solid blue icon (a building, an envelope, a padlock); a
  full-width blue "Sign in" button with its icon and a pending state; a divider reading "Need
  help?" and "Contact support" beneath it with its icon, a mailto from the build's configuration
  (VITE_SUPPORT_EMAIL, passed to the frontend image from .env; no request is made for it), the
  divider and the link absent when none is set;
- the company: "A product of" and the ashline logo with its name ("ash" at medium weight, "line"
  at regular weight in the company's violet), bottom left over the wave, on a plate that keeps
  the logo legible on blue. This is one of two additions to the reference;
- the theme choice of today, small, in the top right corner over the pale side: the other one.

Make it move, with a purpose, from the motion tokens. The tint, the waves and the card are in the
first paint, and the photograph fades in over the tint once it has decoded: no flash, no jump.
The card rises a few pixels as it fades in, the three lines follow one after another, and the
pulse line draws itself once and rests. The two waves drift slowly, the only thing that loops. A
field's border and ring ease in on focus, the show/hide icon cross-fades, and the button answers
the pointer and the press and, while the request runs, shows a spinner in place of its icon. A
refusal eases in above the fields and is announced. Over the company's logo the dot travels in
along its line, as on the company's site. Only transform and opacity are animated, and with
prefers-reduced-motion nothing moves.

Keep what sign-in does today: the same request; the refusals through the ProblemDetails component
(429 too_many_sign_ins shows the wait from Retry-After); the two notices of a session that ended;
the remembered hospital code; the return address; autocomplete attributes; labels tied to their
fields; visible focus; the theme choice with its three settings. Under 900px the three lines and
the photograph fold away (the photograph is not requested at all) and the card stays; at 360px
the page has no sideways scroll. A dark set of the same tokens for the dark theme.

Then the brand and the controls everywhere: the favicon (the BillingRecon mark), the page title
("Sign in · BillingRecon"), theme-color following the theme from a token (index.html holds no raw
colour), and the shared buttons and fields restyled from the same tokens, so that the screens of
7.2 to 7.6 already wear the new palette and controls. Their layouts are 9.2 to 9.4.

No change of behaviour: no change of the API, of a route, of a policy or of what a screen sends.
Every existing frontend test passes unweakened, and the test that refuses a raw colour holds for
the new files too (the gradients, the shadow and the photograph's mask are tokens). The journey
of 8.2 fills the field labelled "Password": with a show/hide button beside it, make that selector
exact in the same change and say so; its one exception, the browser's request for /favicon.ico,
goes away with the favicon.
Tests, in Vitest: the form (labels, show/hide, pending, a refusal, the 429 wait, the notices, the
support link present and absent), the logo and the photograph rendered with their alternative
text (the photograph decorative), every colour from a token in both themes, the token pairs'
contrast.
Tests, in the browser (frontend/e2e): the built page at 1672 x 941 held to your measurements of
the reference within a stated tolerance; the built app under nginx's exact policy, read from
nginx.conf and applied in the test, with no securitypolicyviolation and no console error, in
both themes; no layout shift after the first paint; with reduced motion no animation runs; the
tab order; 390 and 360 px wide without a sideways scroll.
Add screenshots of the built page (both themes at 1672 x 941, and at 390 px) to docs/ui/. Write
ADR 0047, and add the nginx check of this prompt to runbook section 8.
Done when: beside docs/ui/sign-in.png in the browser at 1672 x 941 the built page differs only by
the ashline credit and the theme control; I sign in through it in both themes without a flash;
with reduced motion nothing moves; and the console shows no content security policy refusal.
```

#### 9.2 The shell and the shared components

```
Read ADR 0047 (the tokens, the typeface, the icons, the motion) and "The bar of Phase 9" in
docs/build-prompts.md; ADRs 0038 to 0043 for what the shell and each shared component do today;
docs/ui/sign-in.png and the screenshots 9.1 left in docs/ui/.

Show me the plan first: the shell at 1440, 900 and 390 px wide (a drawing in words is enough);
the shared components and the states each one gets; what a table does under 720 px (rows that
fold into labelled cards, or a table that scrolls inside its own frame with its first column
pinned: recommend one); the icon of each section of the navigation; any new token; any package
you want (ask).

The shell:
- a sidebar: the BillingRecon mark and name at its top; the sections of the permission matrix,
  each with its icon, the current one marked by more than colour. It collapses to a rail of
  icons with tooltips (the choice remembered in this browser), and under 900 px it is a drawer
  behind a menu button: focus stays inside it, Escape closes it, focus returns to the button;
- a top bar: the hospital's name, the user's name and roles, the theme choice and "Sign out",
  which stays a visible button;
- a footer: the ashline logo with "BillingRecon by ashline";
- a "Skip to content" link; every page sets the document title ("Bills · BillingRecon"), and
  after a navigation its heading takes the focus;
- where the browser has view transitions, the content cross-fades between pages and the shell
  does not move.

The shared components, each from the tokens and each with its states (rest, hover, focus,
pressed, disabled, pending, invalid, read-only):
- buttons: primary, secondary, danger, link, and icon-only with a name;
- fields: a label with an optional icon, a hint, an error with an icon, a required mark, a prefix
  or suffix such as the rupee sign or %, select, date, search, textarea;
- the list controls (search, the status filter, pages) and the data table: a header that stays
  in view, a row that answers the pointer, numbers right-aligned in tabular figures, a status
  badge with an icon and a word as well as a colour;
- the page header (title, subtitle, status, actions, the way back) and the details list;
- ProblemAlert, the conflict notice, the inline confirmation, the reason form, the record picker,
  and the audit trail drawn as a timeline;
- loading as a skeleton of the layout that is coming (aria-busy), never a bare spinner; an empty
  state with a small drawing of our own and the next action the role may take; the no-access and
  not-found pages and the "Users and settings" placeholder.
One mapping in one file gives every status of every document its colour token, its icon and its
word; a test holds it complete against the statuses of the API's types.

Then the screens of 7.2 as the proof of the pattern: patients, payers and service items, their
lists, records and forms.

A specimen page, in Development builds only (not in the production bundle, not in the
navigation, the one new route): every shared component in every state, in both themes.

No change of behaviour: no change of the API, of a policy or of what a screen sends, and no other
change of a route. A list keeps its search, status and page in the address. The names and roles
the tests and the journey of 8.2 use stay: the navigation's labels, "Sign out", the fields'
labels, the headings.
Tests: the navigation still follows the matrix for each role; the drawer by keyboard; the rail's
choice remembered; the document title and the focus after a navigation; a skeleton is busy and
then gone; a table at 390 px; the status mapping complete; every existing test unweakened. In the
browser: each of these screens in both themes at 1440 and 390 px with no sideways scroll, no
console error and no content security policy refusal; nothing animates with reduced motion.
Add screenshots (the shell, a list, a record, a form, the specimen page; both themes) to
docs/ui/. Write ADR 0048.
Done when: I walk patients, payers and service items by keyboard alone in both themes, at a
desktop and a phone width, and every list, record and form is of a piece with the sign-in page.
```

#### 9.3 The money screens (shared components, bills, credit notes and adjustments)

As approved on 2026-11-24 and then split by the owner: this prompt builds the shared components and the screens of bills,
credit notes and adjustments; 9.3b, below, the payments, refunds, the payer statement and the inbox. The wording below is
the prompt as given; its "screens" list is shared by the two.

```
Read ADRs 0047 and 0048 and "The bar of Phase 9" in docs/build-prompts.md; ADRs 0040 to 0042 for
what the bill, cashier, credit note, adjustment and inbox screens do today.

Show me the plan first: for each screen below, its layout at 1440 and 390 px and what the eye
meets first; how money is set; any new token or shared component; any package you want (ask).

Money, on every screen: tabular figures, the rupee sign, Indian grouping, right-aligned in
tables. A total stands out from its parts. A credit or a negative amount carries a sign and a
word, never a colour alone. An estimate is marked as one until the server's figure replaces it.

The screens:
- bills: the list (number, patient, payer, date, total, balance, status); the bill's page (its
  number and status at the top, the parties, the lines, the totals and the balance in a summary
  card, the banner while it waits for approval, its credit notes and adjustments, its history as
  the timeline); the draft editor (the pickers, the lines as a grid with each line's total, a
  totals card that stays in view while the lines scroll, post and void with their
  confirmations);
- payments and refunds: the lists; recording a payment; the payment's page with its allocations
  and reversal requests; allocating (the payer's open bills oldest first, "Apply oldest first",
  a meter of what is left to apply that warns before a cap is passed); a refund from its source;
- the payer statement: what is owed, the unapplied cash, the credit and the net as four figures
  at the top, then the bills, payments and credit notes with their actions;
- credit notes and adjustments: the lists, the pages, the editors;
- the checker's inbox: each request a card (its kind with an icon, the document, the maker, the
  amount, how long it has waited), Review opening in place with the document's facts and the
  decision; the maker's own request shown as not theirs to decide.

Motion: a line that is added or removed eases in or out; a total that changes is marked for a
moment (a figure itself is never animated); every action button shows that it is pending; a
decided request leaves the inbox; the first field the API refused comes into view.

No change of behaviour: no change of the API, of a route, of a policy or of what a screen sends,
and no figure is computed differently: lineMath.ts, money.ts and their shared cases are not
touched.
Tests: every existing test unweakened; the totals card and the meter read the figures the
screens show today; every status through the one mapping. In the browser: each of these screens
in both themes at 1440 and 390 px with no sideways scroll, no console error and no content
security policy refusal; nothing animates with reduced motion; the journey of 8.2 passes.
Add screenshots (the bill editor, a posted bill, allocating, the statement, the inbox; both
themes) to docs/ui/. Write ADR 0049.
Done when: the scenario of Phase 4, by a clerk, a cashier and a supervisor in the browser, reads
as one product from the sign-in to the approved refund, in both themes.
```

#### 9.3b The cashier's screens and the inbox

```
Read ADRs 0047 to 0049 and "The bar of Phase 9" in docs/build-prompts.md; ADRs 0041 and 0042 for
what the payment, allocation, refund, statement and inbox screens do today.

Show me the plan first: for each screen below, its layout at 1440 and 390 px and what the eye
meets first; anything new beyond the components of 9.3 (Money, the status mapping, TotalsCard,
Banner, the timeline, fields with a unit and a required mark); any package (ask).

The screens, with the components of 9.3:
- payments and refunds: the lists; recording a payment (the amount with its unit, the payer
  beside it); the payment's page with its amount and unapplied cash as figures, its allocations
  and its reversal requests as banners; a refund from its source with the source's available
  amount as a figure;
- allocating, and applying a credit note's credit (the shared ApplyToBills): the payer's open
  bills oldest first, "Apply oldest first", and a meter of what is left to apply that warns in
  words before a cap is passed, reading the figures money.ts already computes;
- the payer statement: what is owed, the unapplied cash, the credit and the net as four figures
  at the top (the net the strongest), then the bills, payments and credit notes with their
  actions;
- the checker's inbox: each request a card (its kind with an icon, the document, the maker, the
  amount, how long it has waited), Review opening in place with the document's facts and the
  decision; the maker's own request shown as not theirs to decide; a decided request leaves.

No change of behaviour: no change of the API, of a route, of a policy or of what a screen sends,
and money.ts and lineMath.ts are not touched. If the inbox's test must find cards as list items
instead of rows, the assertions stay the same and the change is called out.
Tests: every existing test unweakened; the meter's figures and warning; the statement's four
figures against the API's. In the browser: each of these screens in both themes at 1440 and
390 px under nginx's policy, with no sideways scroll, no console error, no layout shift and
nothing animated with reduced motion; the journey of 8.2 passes.
Add screenshots (allocating, the statement, the inbox; both themes) to docs/ui/. Amend ADR 0049.
Done when: the scenario of Phase 4, by a clerk, a cashier and a supervisor in the browser, reads
as one product from the sign-in to the approved refund, in both themes.
```

#### 9.4 The dashboard, the reports and the finishing pass (split: 9.4 the dashboard, 9.4b the rest)

As written on 2026-11-24 and then split at the owner's word: 9.4 built the dashboard with its tooltips (ADR 0050); 9.4b,
below, takes the reports, print, the finishing pass, the budgets and the release. The prompt below is kept as given.

```
Read ADRs 0043, 0044 and 0047 to 0049 and "The bar of Phase 9" in docs/build-prompts.md;
docs/ui/dashboard.png (the dashboard's layout reference, ADR 0044); docs/backlog.md, row B21.

Show me the plan first: the dashboard and the report screens at 1440 and 390 px; the budgets
(the JavaScript and the CSS a first visit to the sign-in page loads, compressed) and how the
routes are split; what a printed page looks like; the accessibility checks, and any package for
them (ask).

The dashboard, laid out as docs/ui/dashboard.png and set in the tokens of 9.1: the period picker
as a segmented control; six cards (an icon, the label, the figure in compact money with the exact
amount in the tooltip and for screen readers, the change with an arrow and a word); billing
against collections with a themed grid, tooltip and legend, and the table of its figures; the
ageing donut with its total at the centre; the four panels, each with its own skeleton, empty
state and error. A chart draws itself once.

Tooltips on every figure and chart, one shared component from the tokens: two new ones,
--surface-popover and --border-strong, in both themes and held by the contrast test (neither
exists after 9.2), with the existing --shadow-raised and the motion tokens:
- KPI card figure: the exact amount, the previous period's figure, and the change with its arrow
  and word (the summary's value, previous and changePercent);
- sparkline: the business date (or week) and that point's figure from the server's daily series
  (the trend's daily);
- billing against collections: a vertical crosshair with a dot on each line, the date, billed and
  collected to date, and that day's or week's billed and collected (the trend's points: billed,
  collected, cumulativeBilled, cumulativeCollected);
- ageing donut: the hovered slice thickens and the centre shows that bucket's exact amount and
  name; the tooltip shows the bucket, the amount and the share as the API answers it;
- top payers bars: the payer's outstanding amount, their unapplied cash and their available
  credit (the top-payers answer already carries receivable, unappliedCash, creditAvailable and
  netOutstanding, checked 2026-11-24); never send a request for a tooltip.
Every amount in a tooltip is exact, in Indian grouping with tabular figures; the client computes
nothing. Tooltips open on hover, on keyboard focus and on tap, and close on Escape, on blur and
on a second tap. On the line charts the arrow keys move the crosshair point by point. A tooltip
stays inside the viewport and never covers the pointer. Recharts' Tooltip uses our own content
component, not its default styling. Tooltips fade in on opacity only and appear at once under
reduced motion. Nothing is shown only in a tooltip: each chart has a "Show the figures as a
table" holding the same figures (billing against collections has one since 7.6; the sparklines,
the donut and the top payers get theirs).

The reports: the index as cards, an icon and a line each; on each report a filter bar, the table
with its totals row always in view, and Export CSV showing its progress and its end.

Print: a bill, a payer statement and each report print cleanly on A4: no shell, dark text on
white, the hospital's name and the date at the top, no row cut in two.

The finishing pass, over every screen:
- from 360 to 1,920 px wide, and at 200% zoom: no sideways scroll, nothing clipped, nothing
  overlapping;
- by keyboard alone and by a screen reader's outline: one h1, headings in order, a name on every
  control, focus never lost, a live region for what arrives later; Windows forced colours and
  prefers-contrast still readable;
- routes load on demand (backlog B21), within the budgets, which a test of the build holds;
- a chosen set of screenshots in docs/ui/ (each kind of screen, both themes), and the full set,
  every route in both themes at two widths, as an artifact of the browser suite, not in git.

No change of behaviour: no change of the API, of a route, of a policy or of what a screen sends;
the dashboard's figures and the bytes of a CSV stay as they are.
Tests: every existing test unweakened; the budgets; print (the shell is not printed); each
tooltip's content against the API's figures, open and close by pointer, keyboard, tap and Escape,
the arrow keys on the line chart, both themes, reduced motion. In the
browser: every route in both themes with no console error, no content security policy refusal
and no layout shift, and nothing animated with reduced motion.
Update the README (a screenshot, "a product of ashline"), docs/testing-strategy.md, the runbook
and CLAUDE.md; close backlog row B21. Write ADR 0050 and propose the tag v1.1.0.
Done when: I open every screen in both themes at a desktop and a phone width and none looks
older than the sign-in page; the budgets hold; and the journey of 8.2 passes through nginx on my
personal machine with no content security policy refusal.
```

#### 9.4b The reports, print, the finishing pass and the release

```
Read ADRs 0043, 0044 and 0047 to 0050 and "The bar of Phase 9" in docs/build-prompts.md;
docs/backlog.md, rows B21 and B35.

Show me the plan first: the report screens at 1440 and 390 px; the budgets (the JavaScript and
the CSS a first visit to the sign-in page loads, compressed; the largest chunk of any other
route) and how the routes are split; what a printed page looks like; the accessibility checks,
and whether to add @axe-core/playwright for them (ask, with its version, licence and size).

The reports: the index as cards, an icon and a line each; on each report a filter bar, the table
with its totals row always in view, and Export CSV showing its progress and its end.

Print: a bill, a payer statement and each report print cleanly on A4: no shell, dark text on
white, the hospital's name and the date at the top, no row cut in two.

The finishing pass, over every screen:
- from 360 to 1,920 px wide, and at 200% zoom: no sideways scroll, nothing clipped, nothing
  overlapping;
- by keyboard alone and by a screen reader's outline: one h1, headings in order, a name on every
  control, focus never lost, a live region for what arrives later; Windows forced colours and
  prefers-contrast still readable;
- routes load on demand (backlog B21), within the budgets, which a test of the build holds;
- a chosen set of screenshots in docs/ui/ (each kind of screen, both themes), and the full set,
  every route in both themes at two widths, as an artifact of the browser suite, not in git.

No change of behaviour: no change of the API, of a route, of a policy or of what a screen sends;
the dashboard's figures and the bytes of a CSV stay as they are.
Tests: every existing test unweakened; the budgets; print (the shell is not printed). In the
browser: every route in both themes with no console error, no content security policy refusal
and no layout shift, and nothing animated with reduced motion.
Update the README (a screenshot, "a product of ashline"), docs/testing-strategy.md, the runbook
and CLAUDE.md; close backlog row B21. Amend ADR 0050 and propose the tag v1.1.0.
Done when: I open every screen in both themes at a desktop and a phone width and none looks
older than the sign-in page; the budgets hold; and the journey of 8.2 passes through nginx on my
personal machine with no content security policy refusal.
```

Merge to `main`; the owner tags `v1.1.0`.

### Phase 10. Go-live (branch `phase-10-golive`)

R1 and the interface are written, but BillingRecon has only run as a demo on the owner's machines. Phase 10 takes it to a
live hospital: it pays the verification debt, closes the gaps a real client meets on the first day (staff set-up, the
hospital's own records and opening balances, printed documents, production hosting, operations), and ends with a go-live
plan. Eight prompts, one per session, in order. Each begins with a plan and waits for approval. Written as prompts on
2026-11-24 from the owner's draft, with the owner's leave to improve it.

**Amended 2026-11-25 (ADR 0058): there is no client yet.** The owner asked for a hospital to be decided, so the six
inputs below are given by `docs/reference-hospital.md`, a fictional self-pay hospital with a synthetic, generated
export: 1, 2, 4, 5 and 6 are decided there, and 3 (hosting) stays the owner's. Where a prompt below says "the client's
export" or "the client's formats", read the reference hospital's; where it says to stop without them, it no longer
stops. A real hospital's inputs replace that document when one exists.

**Before 10.0, from the client** (10.3 and 10.4 stop without them; 10.1 and 10.2 use 5 and 6):

1. An export, or a sample of one, from the client's current billing system: patients and their payers, unpaid bills,
   advances, credit balances, and the trial balance at a date. **Not received yet.**
2. The client's current bill, receipt and refund formats, and their chartered accountant's requirements for what a
   hospital's bill must show (a tax invoice or a bill of supply, which is the accountant's call). **Not received yet.**
3. Where production runs, the domain, and who holds the production secrets. **Not decided yet.**
4. Confirmation that the client can go live with self-pay only (R1 has no insurer or TPA billing). **Not confirmed yet.**
5. The list of staff who will use it, each with a role of the permission matrix. **Not received yet.**
6. The letterhead: the hospital's legal name, address, GSTIN if any, logo and signatory. **Not received yet.**

**The bar of Phase 10.** Every prompt of the phase is held to all of it:

1. **The bar of Phase 9 still holds** for any screen this phase adds.
2. **The twelve financial rules hold** for any new document: gapless numbering, balanced postings, maker-checker,
   idempotency, zero drift after every database test.
3. **No secret in git.** A key, a password or a connection string with credentials appears only in the host's secret
   store or in a file that is ignored.
4. **Verified where it runs.** From 10.0 on, a prompt is done only when its tests pass on SQL Server 2022 and CI is green,
   not on LocalDB alone.
5. **What is outside the code is written down** in `docs/go-live.md` with an owner and a date. The DPDP review, the data
   processing agreement, the penetration test, the client's tax sign-off and their acceptance are not decided by Claude.
6. **No real patient's data outside production.** The client's export lives outside git; staging and tests use the
   synthetic records of `TestData` or an export with names, phones and addresses replaced.

**The eight stages of 2026-11-26.** On 2026-11-26 the owner gave one prompt of eight stages, to be worked in order in
one session, each stage planned, approved, built, committed and stopped before the next. That one prompt sets aside
"one prompt per session" for these eight only; everything else of "How to use these prompts" holds. Their numbers here,
in the order they run: **10.0-close** (stage 1), **10.4c** (stage 2), **10.4d** (stage 3), **10.8** (stage 4), then
Phase 11 on a new branch, **11.1** to **11.4** (stages 5 to 8). 10.6 and 10.7 come after them. Each is written below at
its number. The rules the owner set for all eight:

```
This prompt has eight stages. Work through them in order, in this one session. For every stage:
show me a short plan (files, steps, tests, risks, migration, any package, any change of the API
or of nginx's policy) and wait for my approval; build it; tell me plainly what was and was not
verified; update the docs and ADRs it touches; propose one commit message; then STOP and wait
for me to say "next". Never start a stage before the one before it is committed. If a stage's
plan is too large for one commit, propose a split. "The bar of Phase 9" and "The bar of
Phase 10" in docs/build-prompts.md hold for every stage. Migrations and ADRs take the next free
number. No new package without asking. Never weaken or skip a test.
Before stage 1, add these eight stages to docs/build-prompts.md with their numbers, so the
status table can follow them.
```

The next free numbers on 2026-11-26: migration `0025`; ADR 0059 (0056 stays with Prompt 10.6).

#### 10.0 Pay the verification debt

```
Read docs/runbook.md section 8 in full, CLAUDE.md section 9 and docs/backlog.md (B31 and B35 in particular).
Build nothing new. Give me, in order, every command section 8 owes for my personal machine: SQL Server
2022 in Docker, dotnet tool restore, dotnet build, dotnet format --verify-no-changes, dotnet test (the
Database collection included), the frontend's npm ci, format, lint, types, tests, build and budgets, the
10,000-bill load run of 8.2, the 8.2 journey through nginx, and the 9.1 to 9.4b checks through nginx,
with what to look for in Seq (backlog B35). Keep them exact and few: I copy the project there and send
the output back. Wait for my output.
From my output: fix real defects only, one commit each, and never weaken a test to make it pass. Then
give me the merge order of phase-2-billing, phase-8-delivery and phase-9-interface into main (say which
are ancestors of which), with the commands, and the tags v1.0.0 and v1.1.0 kept on their commits. I push
and watch the first CI run; we fix it until it is green, including the image smoke run and the frontend
job, neither of which has run.
Decide with me what is left of 9.1 (the photograph): ship without it, or a date for it.
Update runbook section 8, CLAUDE.md sections 2 and 9, the status table at the top of
docs/build-prompts.md, and backlog rows B31 and B35.
Done when: runbook section 8 is empty, every test has passed on SQL Server 2022, CI is green on main, and
nothing is left unmerged.
```

#### 10.0-close Record the verification, merge, push, CI

Stage 1 of the eight stages of 2026-11-26. It finishes 10.0 from the output of the second run on the personal machine.
The prompt ended with a place for that output, which arrived empty; the stage waits for it.

```
I ran deploy/verify/personal-machine.ps1 on my personal machine with SQL Server 2022 in Docker
and Node 22.22 or later; the output is at the end of this prompt. Build nothing new.
1. Read the output against every row of docs/runbook.md section 8. Close each row it proves and
   list plainly any row it does not prove (through nginx, on paper, the 10,000-bill load run,
   the rows of 10.1 to 10.5). Do not close a row on my word alone.
2. Fix real defects only, one commit each. If the timing failures of PiiMaskerTests recur, fix
   their cause, not their limit.
3. Give me the merge order into main with the exact commands, the tags v1.0.0 and v1.1.0 kept
   on their commits, and the push commands for the branches and the tags.
4. After I push, I send you the CI run. We fix it until it is green, the frontend job and the
   image smoke run included.
5. Update runbook section 8, CLAUDE.md sections 2 and 9, the status table of
   docs/build-prompts.md (the early rows that still say "not committed" and "not built on
   .NET 10" too), and backlog rows B31 and B35. Move the decisions recorded under each built
   prompt to docs/build-history.md.
Done when: section 8 has no row without a result or a named reason, main holds everything, the
tags are pushed, and CI is green.
```

#### 10.1 Users and settings screens

```
Read docs/backlog.md rows B18 and B24, ADRs 0030 to 0033, ADRs 0047 to 0050 and "The bar of Phase 9".
Show me the plan first: each screen at 1440 and 390 px, what the eye meets first, and any change of the API.
Build for the TenantAdmin, through the API that exists, in the Phase 9 design:
- users: the list (paged, searchable, by status and role), create a user with roles, edit roles, enable
  and disable (the last active administrator and oneself refused, as the API refuses them today), and
  reset a password;
- settings: the approval threshold, the back-date window and the default due days with If-Match; the
  prefix change as a maker-checker request, shown in the checker's inbox.
Then propose, before coding, a forced password change at the first sign-in and after an administrator's
reset. This amends ADR 0030's point on "no forced change": state the flag, the migration if any, what the
token carries while a change is due, and which endpoints stay open until the password is changed.
Close backlog B24 on the way: no request record that carries a password prints it.
Tests: every form, every refusal through the ProblemDetails component, the matrix test naming any new
endpoint, access by role in the browser, both themes, the routes suite of 9.4b with the new routes.
Close backlog rows B18 and B24. Write ADR 0051.
Done when: a new hospital's administrator sets up every member of staff of the client's list without
touching the API, and a user with a reset password must change it before reaching any screen.
```

#### 10.2 Tenant onboarding

```
Read ADR 0045 (the demo bootstrap), ADR 0030, ADR 0033, ADR 0051 and docs/runbook.md section 10.
Propose first how a real hospital is created in production. Recommend a command run by the operator
on the server (not an endpoint reachable from the internet) that creates the tenant, its first
TenantAdmin with a one-time password and a forced change (10.1), its prefixes, threshold, back-date
window and due days, all audited. Say how it is kept apart from the demo bootstrap, which must refuse to
run outside the demo. Propose with it a second command that suspends a hospital (no one signs in, nothing
is deleted) and lifts the suspension.
Tests: the command on a migrated database as the application login; a second run with the same code
refused; the first admin signs in and must change the password; a suspended hospital's users are refused
and nothing of it is deleted; the demo bootstrap refused in Production.
Add both procedures to the runbook. Write ADR 0052.
Done when: the operator onboards a hospital in one command and its administrator signs in.
```

#### 10.2b Live search and autocomplete

Added on 2026-11-25 at the owner's request, with the plan approved that day: the owner does not want to press Search or
Find before a list or a picker answers. It reverses ADR 0039 point 1 ("a search is sent when the user submits it") and
the Find button of ADR 0040. Text search on the document lists (bills, payments, credit notes, adjustments, refunds),
which have none today, is left for a later prompt: it needs new parameters in the API.

```
Read ADR 0018 (the search of patients), ADR 0039 point 1 and its rejected alternative "a search on every
key", ADR 0040 (the record picker), ADR 0048 and "The bar of Phase 9".
Show me the plan first: files, steps, tests, risks. No new package.
Build, wherever a search box exists today (the lists of patients, payers, service items and users, the
outstanding report, and every record picker inside a form):
- lists: the search is sent as the user types, after a pause of 300 ms and from 2 characters (an empty box
  shows the whole list again). The rows on screen stay until the new ones arrive, with no layout shift; a
  request that a later key made stale is cancelled and never overwrites a newer answer. The address still
  carries ?search= (replaced, not pushed, on each key, so Back leaves the list), and a search returns to
  page 1. The Search button goes; Enter sends at once.
- pickers: an autocomplete. The results open under the box as the user types, with the same pause and
  minimum; the arrow keys move, Enter chooses, Escape closes, and Enter never submits the form around it.
  The combobox pattern of WAI-ARIA, announced to a screen reader, passing axe. The Find button goes.
- the server: every search matches a part of the text, as LIKE '%text%' with the wildcards of the search
  escaped, as the service items do today. Two do not yet: the MRN of a patient (the whole MRN only,
  ADR 0018) and the email of a user (its beginning only). Propose how the MRN is compared in part through
  its converter before coding it, and say what each search costs without an index.
Tests: one key sends nothing before the pause and one request after it; a stale answer is dropped; under 2
characters nothing is sent; the address and Back; page 1 after a search; the picker by keyboard alone and
by pointer; a part of an MRN and a part of an email found on SQL Server, a search holding % or _ taken as
text; the browser suites that press Search or Find changed to type and wait, none removed.
Amend ADR 0039, ADR 0040 and ADR 0018 ("Amended 2026-11-25"); update docs/api.md where a search is
described.
Done when: no screen has a Search or Find button, and every search box answers while the user types.
```

#### 10.2c Sign-out on the server

Added on 2026-11-25 at the owner's request, who found that a user who had signed out could still work: the token stayed
valid for up to an hour, and other tabs stayed signed in. No client input is needed. ADR 0057.

> Make a sign-out hold on the server. Give each user a number of sessions that every token carries, and refuse, with
> 401 `session_ended`, a token whose number is no longer its user's, reading the user's row on every request with a
> token and keeping nothing. Raise the number at a sign-out (a new `POST /api/v1/auth/logout`, audited), at a change or
> a reset of the password, at a disabling and at a change of the roles. In the browser, Sign out tells the API and ends
> the same user's session in the other tabs. Leave Development's access without a token as it is. Tests for each; no
> new package; ADR 0057 amending ADR 0031 point 8 and ADR 0032 point 8, and rows S4 and E2 of the threat model.

**Done when:** a token copied before a sign-out is refused after it; so is the token of a disabled user and of one
whose roles changed; another user's token is untouched; and the load test still meets its targets.

#### 10.3 The hospital's records and opening balances

```
Read ADR 0005, ADR 0008, docs/database.md sections 4 and 5, docs/requirements.md BR-101 to BR-106 and
BR-401 to BR-406, and the client's export in <path the owner gives, outside git>. If the export is not
there, stop and tell me.
Raise with me first, before any design: the cut-over date; what the export holds and what it lacks; how
its patients map to MRNs and payers (duplicates, guarantors, missing fields); and whether opening
balances are kept per bill (each unpaid bill of the old system becomes one opening receivable with its
old number and date) or per payer (one figure each). Recommend one.
Then propose, in this order: the import of patients and their payers (matched by MRN, duplicates
reported, never merged silently); then opening-balance documents (an opening receivable, opening unapplied
cash, opening credit) with their own number series, ledger postings against a new OpeningBalances account
(an ADR amending docs/database.md section 5 and the reconciliation identities), maker-checker approval of
the import as one batch, and an import from CSV with a dry run that reports every rejected row and why. The
opening documents can be allocated, credited, adjusted and refunded like the documents they stand for.
The import runs on staging with the whole export first, then on production at cut-over.
Tests: the dry run changes nothing; a rejected row names its line and reason; a duplicate patient is
reported; an import reconciles to zero drift; the imported totals equal the export's trial balance; a
payment allocated to an opening receivable settles it; a second import of the same batch is refused.
Write the import procedure in the runbook and ADR 0053.
Done when: the client's sample export imports, reconciles to zero drift, and each payer's statement
matches the old system's balance for that payer.
```

#### 10.4 Printed bills, receipts, refund vouchers and the tax document

```
Read the print styles of 9.4b, ADR 0040, ADR 0041, and the client's formats and their chartered
accountant's requirements in <path the owner gives>. If they are not there, stop and tell me.
Show me first, as a list, every field the formats and the requirements ask for and whether the model has it
(the hospital's legal name, address and GSTIN, the SAC code of a service, the tax split into CGST, SGST or
IGST, the place of supply, the exempt-service wording, whether the document is a tax invoice or a bill of
supply, amounts in words in the Indian system of lakh and crore, the signatory). Propose each missing one
with its home (tenant settings, the service item, the bill) and its migration, as an ADR, before coding. No
figure is computed differently from BR-202 and BR-203; a split of tax is derived from the stored line tax.
Then build, on A4 with the hospital's letterhead from tenant settings: the printed bill, the payment receipt
and the refund voucher, each reachable from its document's page, each showing a reprint as a duplicate.
Tests: each printed document's fields against its document; amounts in words for awkward values (0.50,
1,00,000.01, 12,34,56,789.99); the duplicate mark; print in both themes yields dark text on white; no shell
is printed; the routes suite of 9.4b with the new routes.
Write ADR 0054.
Done when: the client's accountant signs off a printed sample of each, recorded in docs/go-live.md.
```

#### 10.4c The logo, the signature and print settings

Stage 2 of the eight stages of 2026-11-26.

```
Read ADR 0054 and its amendment, ADR 0047, frontend/deploy/nginx.conf, docs/security.md
section 10.
A. The letterhead of 10.4 holds text only. Add the hospital's logo, the signatory's name and
   designation, and an image of the signature. Propose before coding: where the images are kept
   (the database, within the 1 MB a request carries) and the formats (PNG, JPEG, WebP; SVG
   refused; content checked by its bytes); how the browser shows them, since an <img> cannot
   send the token (a blob, with img-src gaining blob: in nginx.conf and its test in the same
   change, or a better way; nothing from another origin); who uploads (TenantAdmin, If-Match,
   audited with a hash of the image, never the image); whether a duplicate carries the
   signature. Whether a scanned signature is enough on a Tax Invoice is the accountant's call:
   an open item in docs/go-live.md.
   Build: upload, replace and remove with a preview on the Settings screen; the logo at the top
   of the three sheets of 10.4b and the signature block ("Authorised signatory", image, name,
   designation) at their foot, with fixed sizes so nothing shifts. Print waits for the images.
   A sheet without them prints as today.
B. Print settings per hospital and per document (bill, receipt, refund voucher), for the
   TenantAdmin, with If-Match, audited, and a live preview drawn by the components that print:
   paper A4 or A5 (and an 80 mm roll for the receipt and voucher if you can hold it in a test);
   margins in mm within limits; the logo left, centre or right and small, medium or large, or
   "pre-printed stationery" with a top space in mm; each of these on or off: the patient's
   phone and address, the payer, the SAC column, the tax split, the amount in words, the
   signature block, the cashier's name (a field ADR 0054 makes mandatory cannot be switched
   off, and the form says why); a footer line and terms as plain text per document. The default
   reproduces today's sheet exactly. Settings change layout only: no figure differs, the
   Original and Duplicate rule and the print record stay, no inline style is written.
C. Then propose and wait: a "Download PDF" beside Print. Give the ways (headless Chromium
   rendering the built sheet; a .NET PDF library drawing it a second time), each with package,
   version, licence, image size, the cost of keeping two layouts equal, and how a download is
   recorded as a print. Recommend one. Crystal Reports is not an option: it does not run on
   .NET 10 or in our Linux images.
Tests: uploads refused for a wrong type, an oversized file and an SVG; another tenant never
reads the image or the settings; the audit records; each sheet at each paper with and without
logo, signature and each switch; a mandatory field cannot be switched off; the default equal to
today's sheet; the preview equal to the print; dark text on white from both themes; the matrix
test; the routes suite under nginx's policy.
Update docs/api.md, docs/security.md, runbook section 8, docs/go-live.md, docs/ui/. An ADR
amending ADR 0054.
Done when: I set the logo, signature, paper and footer, see them in the preview, and a bill, a
receipt and a refund voucher print that way.
```

#### 10.4d The search lens, and Tab in a search

Stage 3 of the eight stages of 2026-11-26.

```
Read ADR 0048, the 2026-11-25 amendments of ADR 0039 and ADR 0040, and useLiveSearch. No
package, no change of the API.
A. The lens: a decorative icon at the start of every search field (the lists of patients,
   payers, service items and users, the outstanding report, every record picker): our own
   inline SVG in currentColor, aria-hidden, not a button. While a search is in flight the small
   spinner takes its place, with no shift. A "Clear search" button at the end when the box
   holds text. No Search or Find button returns; live search stays as it is.
B. Leaving a search box (blur). A list's box: on Tab, Shift+Tab or a click elsewhere the search
   is sent at once, without the 300 ms, if the text changed and has 2 characters or more. A
   record picker: the first result is the active option while typing; Tab chooses it, closes
   the list and moves to the next field in one press (Shift+Tab the same, backwards); if the
   answer has not arrived, the search is sent at once and the first result chosen when it
   arrives, unless the user came back and typed again; with no result the box keeps its text,
   is marked invalid with "No record chosen", and focus still moves on. Leaving by the pointer
   closes the list and chooses nothing. Enter chooses and stays. The chosen record shows its
   name and code and is announced. Say where choosing the first result is unsafe (the payer of
   a payment or a refund) and propose choosing only on a single match there.
Tests: the lens everywhere and absent from the accessibility tree; Clear empties the box and
the address; the search sent on blur before the pause, not sent when unchanged; the picker by
Tab with one result, several, none and a late answer; focus on the next field every time; the
10.2b tests unweakened; axe; the routes suite in both themes.
Amend ADR 0039 and ADR 0040 ("Amended 2026-11-26"). Update docs/ui/.
Done when: every search box shows the lens, and I fill the bill, payment and refund forms by
typing a few letters and pressing Tab.
```

#### 10.5 Production deployment

```
Read ADR 0017, ADR 0035, ADR 0045, docs/security.md and docs/runbook.md. Ask me first where it runs, the
domain, and who holds the secrets; do not assume a cloud. Say where the data is held (in India, for the
DPDP review) and how it is encrypted at rest (TDE where the edition has it, the disk otherwise).
Propose, then build: the production compose file or manifests; a real certificate and its renewal; the
JWT signing key, the database logins and every connection string injected from the host's secret store,
never from git, and refused at startup when missing; SQL Server 2022 (the edition and its licence named),
with READ_COMMITTED_SNAPSHOT ON checked at startup; migrations run by the release pipeline as the migrator
login, from a tagged image; scheduled, encrypted backups with their retention and an off-site copy, and a
restore drill each month; a staging environment identical to production.
Write in the runbook: deploy, roll back a release, roll back a migration, restore a backup, and rotate each
secret.
Tests: the startup refusals; the pipeline deploys a tagged image to staging; a backup of staging is restored
to a new database and reconciles to zero drift.
Write ADR 0055.
Done when: staging is deployed by the pipeline from a tag, a backup is restored from it, and I have walked
the deploy and rollback procedures myself.
```

#### 10.6 Operations

```
Read ADR 0006, ADR 0035, docs/security.md section 10 and the reconciliation of 6.2.
Build: the scheduled clean-up of idempotency keys older than 30 days; the reconciliation of every tenant
every night, with an alert on any drift row; uptime and error alerting on the health endpoints and the
error rate; alerts on a database or disk near full and a certificate near expiry; the production log store
with its retention and the PII masking of 1.2 checked in it; the retention of the audit trail written down.
Record the limits of a single instance (the sign-in limiter in memory, the clean-up running once) as
accepted risks in docs/security.md, or propose how to lift them.
Write an on-call page in the runbook: who is called, for what, and the first steps for drift, a failed
deploy, a database that is full and a certificate that is about to expire.
Tests: an expired key removed and a fresh one kept; a seeded drift on staging sends an alert; the
alerts named in the runbook each fire once on staging.
Write ADR 0056.
Done when: a drift seeded on staging reaches me as an alert, and the clean-up has run on its schedule.
```

#### 10.7 Go-live readiness

```
Read every ADR of Phase 10, docs/runbook.md and docs/security.md.
Write docs/go-live.md:
- the user acceptance script: the Phase 4 scenario and a day of the client's own front desk, cash counter
  and supervisor, each step with its expected result and a column for the client's tester;
- training notes for each role, one page each, with screenshots from docs/ui/;
- the cut-over plan: freeze the old system, export, import (10.3), reconcile, sign-off, switch, with times;
- the go/no-go criteria (acceptance signed, zero drift after the import, backups restored, alerts firing,
  the accountant's sign-off) and the rollback decision point, with who makes each call;
- two weeks of hypercare: who answers, how fast, how a defect is reported, and what is watched each day
  (drift, errors, sign-in refusals);
- a checklist of what is outside the code: the penetration test, the DPDP Act review with consent wording,
  retention and a breach procedure, the data processing agreement, the accountant's sign-off of 10.4, the
  client's acceptance, the support contract. Each item with an owner and a date; none is marked done by
  Claude.
Update the README, CLAUDE.md and the status table of docs/build-prompts.md. Propose the tag v1.2.0.
Done when: every checklist item has an owner and a date, and the client has a date for acceptance testing.
```

#### 10.8 An independent review

Stage 4 of the eight stages of 2026-11-26. It runs before 10.6 and 10.7, after 10.4d.

```
Run this as a separate agent that has not seen this session's work, if you can; if you cannot,
say so. It changes no file but docs/review-2026-11.md. For each finding: file and line, what
goes wrong as a concrete sequence of requests, severity, the smallest fix. In this order:
1. money: every path that changes a balance (post, allocate, credit, adjust, reverse, refund,
   the opening batch): breaking a balance identity, passing a cap by two requests at once, a
   number without a document, posting twice under two idempotency keys;
2. tenancy: any read or write of another tenant's row, every IgnoreQueryFilters and raw SQL,
   the image and import endpoints;
3. authorisation: every endpoint against the matrix, maker equal to checker by any route, what
   a disabled, suspended or signed-out user's token can still do;
4. input: uploads, CSV import, search text, the letterhead's text on a sheet, the CSV formula
   guard;
5. tests: those that assert too little, pass by timing, or were loosened.
No style findings. End with the ten to fix first. Then, in the main session, propose the fixes
for those ten, one commit each, and wait for my approval before each.
Done when: the review exists, I have read it, and the fixes I approved are committed.
```

Merge to `main`; the owner tags `v1.2.0`; go-live follows the plan in `docs/go-live.md`.

### Phase 11. The counter (branch `phase-11-counter`)

Stages 5 to 8 of the eight stages of 2026-11-26: what a hospital's cash counter and its accountant ask for beyond R1.
The rules of those stages (under "The eight stages of 2026-11-26", Phase 10) and both bars hold. They run before 10.6
and 10.7.

#### 11.1 Search on the document lists

```
Read ADR 0018 with its amendment (LikePattern), ADR 0036, ADRs 0039 to 0042, docs/api.md.
A search parameter on the lists of bills, payments, credit notes, adjustments and refunds: a
part of the document's number, the patient's name or MRN, the payer's name, an exact amount;
tenant-filtered, paged, combined with the existing filters. Say what each costs on 100,000
documents and the index you propose. In the browser a search box on each list, with the lens,
live search and the blur rule of stage 3, kept in the address.
Tests: each field found in part on SQL Server; % and _ taken as text; another tenant's document
never found; the matrix test; the lists' browser tests. A migration for the indexes. Update
docs/api.md; amend ADR 0036.
Done when: a cashier finds a payment by a patient's name or a part of its number in one box.
```

#### 11.2 The cashier's shift and the day-end closing

```
Read BR-301 to BR-309, ADR 0036, ADR 0041, the permission matrix. Propose the requirements
first (user stories, BR ids, the matrix row) and wait. Raise before any design: whether a cash
payment or refund needs an open shift; whether a difference between counted and expected cash
is only recorded or posted to a new ledger account (and what that means for the reconciliation
identities); who approves a closing with a difference.
Recommended: a CashierShift a Cashier opens with a float and closes with a count by
denomination; expected cash from that cashier's Cash entries in the shift, by method; the
difference stored with a required reason; a closing with a difference approved by a Supervisor
(maker-checker); a day-end report of every shift, tied to the daily collection of 6.1; a closed
shift is not reopened. Screens for both roles and a printed closing slip through stage 2's
print settings.
Tests: expected cash equals the ledger's entries of the shift; a closing with a difference
waits and its cashier cannot approve it; a second open shift for one cashier refused; zero
drift; the day-end total equal to the daily collection. A migration and an ADR.
Done when: a cashier closes a shift, a supervisor approves its difference, and the day-end
report equals the daily collection.
```

#### 11.3 Two-step sign-in and a forgotten password

To be weighed in its plan (review finding R5, accepted by the owner on 2026-11-26; `docs/security.md`, row R3): an
administrator who resets a Supervisor's password, or their second step, can still act as that Supervisor. The plan
says how far the second step narrows this and what stays accepted.

```
Read ADR 0030, 0031, 0035, 0051, 0057 and docs/security.md section 10. Propose first:
- a second step at sign-in with a time-based code (TOTP, RFC 6238) on the BCL with no package:
  enrolment with a QR code drawn in the page, recovery codes shown once and stored hashed, the
  secret encrypted at rest (say with which key and where it lives), required for TenantAdmin
  and Supervisor and offered to the rest, an administrator's reset of it, audited; what the
  token carries between the two steps; the limits on wrong codes;
- a forgotten password. The product sends no e-mail today: propose SMTP from the host's
  secrets, a single-use link good for 30 minutes, the same answer whether or not the address
  exists, the limits, and that with no mail server the administrator's reset stays the only way.
Tests: codes of the neighbouring time steps accepted or refused as the RFC says; a code used
twice refused; a recovery code good once; a checker without a second step cannot approve; the
link good once and expired after 30 minutes; every session ended by a reset. Update the threat
model. A migration and an ADR.
Done when: a Supervisor approves nothing without the second step, and a user who forgot a
password is back in without the administrator.
```

#### 11.4 The accountant's export

```
Read docs/database.md section 5, ADR 0036, ADR 0043. Raise first: what the accountant uses
(Tally is likely) and whether they want vouchers per document or a summary per day. Do not
decide the hospital's chart of accounts: propose a mapping in tenant settings from our seven
ledger accounts and the payment methods to the accountant's ledger names.
Build in Reporting (it never writes): an export of a period of business dates as a file the
package imports (for Tally its XML voucher format: confirm it against Tally's own
documentation before coding and say what you could not confirm), every voucher balanced and
naming our document number, with a control total equal to the ledger's movements of the
period. A missing mapping refuses the export and names the account.
Tests: the vouchers of the Phase 4 scenario sum to the ledger's movements; each balances; a
reversed payment appears as its mirror; the file is well formed; another tenant's postings
never appear. An ADR. Add to docs/go-live.md: the accountant imports a sample and signs it off.
Done when: the accountant imports a month and its totals equal ours.
```

After stage 8: the status table, CLAUDE.md and the README are updated, and the owner is told what is left before 10.6
and 10.7.
