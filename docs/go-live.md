# Go-live

What stands between BillingRecon and a first live hospital that is **outside the code**: each line with who decides it
and by when, and how the hospital moves onto it. Completed by Prompt 10.7 (2026-11-27): the open sign-offs of Phase 10
and 11, the checklist with an owner and a date for each item, the acceptance script, the cut-over plan, go or no-go,
and the first two weeks. No line here is decided by Claude Code (the bar of Phase 10, point 5).

There is no client yet (ADR 0058). Phase 10 is built against the reference hospital of `docs/reference-hospital.md`,
so every line below is open until a real hospital exists, and its date is "before that hospital's go-live".

## Open sign-offs

| # | What | Why it is open | Who decides | When |
|---|---|---|---|---|
| G1 | **The tax document and its wording.** Whether a hospital's bill is a tax invoice or a bill of supply, the exempt-service wording, the split into CGST and SGST with no IGST, the place of supply, and what the receipt and the refund voucher must show | ADR 0054 builds Claude Code's reading for the reference hospital. It is not tax advice | The hospital's chartered accountant, on a printed sample of each document | Before go-live |
| G2 | **The hospital's own formats.** Its current bill, receipt and refund layouts, its letterhead and logo | None was received; the printed pages follow the reference hospital | The hospital | Before go-live |
| G3 | **The hospital's export and its trial balance.** The opening balances are approved against the old system's closing trial balance, signed by the hospital's accounts (runbook section 18) | The import is proved only against the reference export (ADR 0053, ADR 0058) | The hospital's accounts | At the cut-over |
| G4 | **Where production runs, the domain, and who holds the secrets** | The shape is decided and built (ADR 0055): one Linux server with Docker Compose, SQL Server 2022 Standard with TDE, Let's Encrypt, secrets as files held by the operator. The provider, its Indian region, the domain, the SQL Server licence and the off-site remote are not chosen | The owner | Before the first deploy |
| G5 | **The deployment has never run.** Staging deployed by the pipeline from a tag, a backup restored from it, and the deploy and rollback procedures walked (runbook section 19; section 8, row 10.5) | Written and checked by tests of its files only | The owner, on a staging server | Before go-live |
| G6 | **The DPDP review.** Where personal data is held and how it is encrypted is written in `docs/security.md` section 8; whether that meets the Act for this hospital is a legal reading | Not reviewed | The hospital's counsel or data protection officer | Before go-live |
| G7 | **A scanned signature on a tax invoice.** Whether the image of the signatory's signature, printed by the application on a Tax Invoice, a receipt and a refund voucher (and on their duplicates), is a signature in law for this hospital, or whether the documents must be signed by hand or digitally | ADR 0059 prints the uploaded image; it decides nothing about what it is worth | The hospital's chartered accountant, with G1 | Before go-live |
| G8 | **Two administrators, each with a second step, and the key in the vault.** A hospital goes live with at least two TenantAdmins who have set their second step up and kept their recovery codes, so that one can reset the other's (ADR 0061); and `two_step_encryption_key` is in the operator's vault beside the backups' key, because a restore without it leaves every user to enrol again | The requirement is in the code; who the two are, and the vault, are not | The operator and the hospital | Before the first sign-in in production |
| G9 | **Mail for a forgotten password, or the decision to go without.** Either a mail server is set (`MAIL_HOST` and the rest, `mail_password`), a link was asked for and arrived in an inbox of the hospital, and the sender's domain is set up so that the mail is not spam (SPF and DKIM are the mail provider's); or the hospital goes live without it and its administrators reset passwords (ADR 0061, amended; runbook section 8, row 11.3b) | Built; never sent to a real server | The owner with the hospital's IT | Before go-live |
| G10 | **The accountant imports a sample and signs it off.** The hospital's accountant names the twelve ledgers, imports the export of one month into a Tally company made for the test, twice, and confirms in writing that every voucher arrived, that the totals of each ledger equal the summary's, and what the second import did (ADR 0062; runbook section 8, row 11.4a) | Built, with its screens; the format confirmed against Tally's documentation in part; never imported | The hospital's accountant with the owner | Before the first month-end in production |

## The checklist: what is outside the code, with an owner and a date

Written by Prompt 10.7 on 2026-11-27, on the owner's approval of its plan. There is no client yet, so every owner is a
role and every date is counted back from the reference cut-over, **Thursday 1 April 2027** (`docs/reference-hospital.md`).
With a real hospital the owner replaces the roles with names and moves the dates with its cut-over. **None of these is
marked done by Claude Code**; the "Done" column is filled by the owner with the evidence.

| # | Item | Owner | By | Evidence when done | Done |
|---|---|---|---|---|---|
| C1 | **The hospital's acceptance date.** The window of acceptance testing (proposed: 8 to 19 March 2027), agreed with the hospital | The owner, with the hospital's administrator | 1 February 2027 | A mail of the hospital naming the dates and its testers | |
| C2 | **Where production runs** (G4): provider, Indian region, domain, SQL Server licence, off-site remote | The owner | 15 January 2027 | The env file filled in (no secret in it), the domain resolving | |
| C3 | **Staging deployed and walked** (G5): a tag through the pipeline, a backup restored, a deploy and a rollback | The owner | 1 February 2027 | Runbook section 8, rows 10.5 and 10.6a/b, with their output | |
| C4 | **The hospital's formats and letterhead** (G2) | The hospital's administrator | 1 February 2027 | Its letterhead, logo and signature set; a printed bill, receipt and refund voucher it accepts | |
| C5 | **The tax document and its wording** (G1) | The hospital's chartered accountant | 15 February 2027 | A signed note on a printed sample bill | |
| C6 | **A scanned signature on a tax invoice** (G7) | The hospital's chartered accountant | 15 February 2027 | A signed note: the image, or signing by hand | |
| C7 | **The penetration test** of staging: the API, the frontend through nginx, the server's open ports | An independent tester the owner engages | 1 March 2027 | The tester's report; every High and Critical finding fixed and retested | |
| C8 | **The DPDP review** (G6), with the **consent wording** the front desk uses when it registers a patient | The hospital's counsel or data protection officer | 1 March 2027 | A signed review; the wording printed at the front desk | |
| C9 | **Retention**: the audit trail at least eight years (ADR 0056, Companies Act section 128), Seq 30 days, backups 35 days, and what happens to a patient's record on request | The hospital's counsel, with the owner | 1 March 2027 | A retention note signed by the hospital | |
| C10 | **The breach procedure**: who notices, who decides, who tells the Data Protection Board and the patients, within what time | The hospital's data protection officer, with the owner | 1 March 2027 | A one-page procedure, with names and numbers | |
| C11 | **The data processing agreement** between the hospital and the operator of BillingRecon | The owner and the hospital's counsel | 1 March 2027 | The signed agreement | |
| C12 | **The support contract**: hours, response times, how a defect is reported, what it costs (the hypercare below is its first two weeks) | The owner and the hospital | 15 March 2027 | The signed contract | |
| C13 | **Mail for a forgotten password and for alerts** (G9): a server set, a link received, an alert received | The owner | 15 March 2027 | The two mails, in the hospital's and the operator's inboxes | |
| C14 | **The accountant's sign-off of the Tally export** (G10) | The hospital's accountant | 15 March 2027 | The accountant's signed note: a month imported twice, its totals | |
| C15 | **The uptime monitor and the alerts proven** (runbook section 20.1 and 20.2) | The owner | 25 March 2027 | A "health" mail and the monitor's alert, both received on staging | |
| C16 | **A restore drill on production's server** (runbook section 19.6) | The owner | 25 March 2027 | The drill's output, with the time it took | |
| C17 | **Two administrators, each with a second step, and the keys in the vault** (G8) | The hospital's two TenantAdmins, and the owner for the vault | 25 March 2027 | Both enrolled; the vault holds `two_step_encryption_key`, the TDE certificate's backup and its password | |
| C18 | **The client's acceptance**: every step of the script below passed or accepted with a note | The hospital's administrator | 26 March 2027 | The script, signed, with the testers' results | |
| C19 | **The closing trial balance of the old system** (G3), signed by the hospital's accounts | The hospital's accounts | 31 March 2027, 20:30 IST | The signed trial balance, kept with the go-live record | |
| C20 | **Go or no-go** (below) | The owner and the hospital's administrator | 31 March 2027, 15:00 IST | The minutes of the decision | |

**What is still open after 10.7:** C1 cannot be met without a client. Prompt 10.7's "done when" (every item with an
owner and a date, and a date for acceptance testing) is met for the reference hospital only.

## The user acceptance script

The hospital's testers work on **staging**, with the hospital onboarded there (runbook section 17) and its own
letterhead. Each step is done by the role named, on the screens; the expected result is what the screens must show.
The tester writes **Pass**, or **Fail** with what was seen, in the last column, and signs the script at the end. A
failure is reported as hypercare's defects are (below).

### Part A: one payer's month (the Phase 4 scenario)

The scenario `EndToEndScenarioTests` proves against the ledger, done by hand. One patient who pays for themselves.
Amounts in rupees; GST as the catalogue or the line has it. A Supervisor who is not the maker approves.

| # | Who | Do | Expect | Tester's result |
|---|---|---|---|---|
| A1 | BillingClerk | Register a patient, who is their own payer | The patient and the payer exist; the payer owes 0.00 | |
| A2 | BillingClerk | A bill: an OPD consultation of 500.00 at 18% GST and a dressing, 2 × 60.00 at 5%. Post it | Posted with a number `BIL/...`; total 716.00 (590.00 and 126.00); owes 716.00 | |
| A3 | BillingClerk | A second bill: the dressing alone, 2 × 60.00 at 5%. Post it | Total 126.00; the payer's statement owes 842.00 | |
| A4 | Cashier | Record a payment of 500.00 by UPI with its reference; apply 400.00 to the first bill and 100.00 to the second | The first bill owes 316.00, the second 26.00; the payer owes 342.00 and holds nothing unapplied | |
| A5 | BillingClerk, then Supervisor | A credit note for the dressing of the first bill (126.00); submit; the Supervisor approves it in the inbox | The credit note is posted and all of it applied: the first bill owes 190.00; the payer owes 216.00 | |
| A6 | BillingClerk, then Supervisor | A discount of 90.00 on the first bill, reason "Staff discount"; the Supervisor approves | The first bill owes 100.00; the payer owes 126.00 | |
| A7 | Cashier, then Supervisor | The 100.00 on the second bill was the wrong bill: ask to reverse that allocation; the Supervisor approves | The second bill owes 126.00 again; the payment holds 100.00 unapplied; the payer owes 226.00 and holds 100.00 | |
| A8 | Cashier, then Supervisor | Refund the 100.00 by bank transfer with its UTR, reason "Paid in error"; the Supervisor approves | The refund is posted; the payment holds nothing; the payer owes 226.00 | |
| A9 | Viewer | Reports: the bill register, the payment register, outstanding by payer, unapplied cash, available credit | Every figure as above; the payer owes 226.00 | |
| A10 | Viewer | Reports, Reconciliation | No row: no drift, no gap in any series of numbers | |
| A11 | The same Supervisor | Try to approve a document they made themselves | Refused: a maker never approves their own document | |

### Part B: a day at the hospital

The front desk, the cash counter, the supervisor and the accountant of the reference hospital on an ordinary day.

| # | Who | Do | Expect | Tester's result |
|---|---|---|---|---|
| B1 | TenantAdmin | Create a Cashier with a one-time password | The Cashier signs in and must choose a password of their own before anything else | |
| B2 | Supervisor | Sign in | The code of the authenticator app is asked after the password; without it nothing can be approved | |
| B3 | Cashier | Open a shift with a float of 2,000.00 | The shift is open; the counter shows it | |
| B4 | BillingClerk | Register a new patient with a guarantor; bill two items of the catalogue; post | Posted; the guarantor is the payer; the bill prints on the hospital's letterhead with its SAC codes and CGST and SGST | |
| B5 | Cashier | Take cash for that bill in full; print the receipt | The bill is settled; the receipt prints with the amount in words | |
| B6 | Cashier | Take a payment by card for another payer's bill, in part | That bill is partly paid; the card's money is not in the drawer | |
| B7 | BillingClerk | A bill of 1,00,000.00 or more; post it | It waits for approval and takes no number yet; a Supervisor approves it and it is posted | |
| B8 | Cashier, then Supervisor | A cash refund from an advance; the Supervisor approves | The refund is in the shift's money paid out | |
| B9 | Cashier | Close the shift: count the notes and coins, with 10.00 short | The closing waits for a Supervisor, because of the difference | |
| B10 | Supervisor | Approve the closing; print the closing slip | The shift is closed with its difference of −10.00 | |
| B11 | Supervisor | Reports, Day-end report of the day | Every shift of the day; the cash of the day equals the Cash row of the daily collection | |
| B12 | Viewer | Search the bills by the patient's name, then by an amount | The bills found are the patient's; the amount finds the bill | |
| B13 | TenantAdmin | Users and settings, Accounts: the twelve ledger names of the hospital's Tally company. Reports, Accountant's export of the day | The summary's control total equals the ledger's; the file downloads | |
| B14 | Any user | Sign out, then use the browser's back button | The screens ask for a sign-in again | |
| B15 | Viewer | Reports, Reconciliation, at the end of the day | No row | |

**Signed:** for the hospital ______________________ (name, role, date) · for BillingRecon ______________________

## The cut-over plan

For the reference hospital's cut-over on Thursday 1 April 2027 (runbook sections 17 to 19). Times are IST. A real
hospital moves the dates, not the order.

| When | Step | Who | Done when |
|---|---|---|---|
| 18 March, 14:00 | **The rehearsal on staging**: the whole of this plan with the old system's export of the end of February, timed | The owner, the hospital's administrator and accounts | Every step done; the times written down; the findings of the dry runs fixed in the files |
| 31 March, 15:00 | **Go or no-go** (the criteria below) | The owner and the hospital's administrator | A "go" in the minutes |
| 31 March, 20:00 | **Freeze the old system** after the last shift has closed: nothing more is entered in it | The hospital's administrator | The old system read-only, or its users told and its last document noted |
| 31 March, 20:30 | **Export** the patients, guarantors, unpaid bills and advances (runbook 18.1), and **sign the closing trial balance** (C19) | The hospital's IT lead and accounts | The files and the signed trial balance with the owner |
| 31 March, 21:00 | **A full backup of production** before the import (runbook 15) | The owner | The backup verified; its name written down |
| 31 March, 21:15 | **Patients and guarantors**: dry run, corrections, import (runbook 18.2, steps 2 and 3) | The hospital's TenantAdmin, with the owner | `written: true`; no rejected row |
| 31 March, 22:00 | **Opening balances**: dry run against the trial balance, submit (steps 4 and 5) | The hospital's TenantAdmin | `accepted: true`; the three totals equal the trial balance to the paisa |
| 31 March, 22:30 | **The second person approves** the batch (step 6) | The hospital's second TenantAdmin or finance Supervisor | The batch posted |
| 31 March, 23:00 | **Reconcile** (step 7): no drift; outstanding by payer equals the trial balance; ten payers compared with the old system | The hospital's accounts, with the owner | All three true |
| 31 March, 23:30 | **A full backup of production** after the import: the known good point | The owner | The backup verified; its name written down |
| 1 April, 07:30 | **The last look**: `/health/ready`, the watchdog's last round, the night's reconciliation sent no alert | The owner | All green |
| 1 April, 08:00 | **The switch**: the first shift opens; the first bill | The hospital's cashiers and clerks | The first bill posted and printed |

**The rollback decision point is 1 April, 08:00, before the first live bill.** Until then a rollback is simple: the old
system is unfrozen and used as before; production is restored to the backup of 21:00 or left unused. **After the
first live bill there is no going back to the old system**: what goes wrong is fixed forward, with the hypercare
below. Who decides a rollback: the owner and the hospital's administrator together; if they disagree, the rollback.

## Go or no-go

On 31 March 2027 at 15:00 IST. Every criterion is "go", or it is "no-go" and the cut-over moves.

| Criterion | Evidence | Who calls it |
|---|---|---|
| The client's acceptance is signed (C18) | The signed script | The hospital's administrator |
| The rehearsal of 18 March ended with zero drift and with the totals of the trial balance | Its notes | The owner |
| A backup was restored on production's server (C16) and the alerts were received (C15) | The drill's output; the two mails | The owner |
| The accountant signed the Tally export off (C14) | The signed note | The hospital's accountant |
| The penetration test has no open High or Critical finding (C7) | The retest | The owner |
| The DPDP review, the data processing agreement and the support contract are signed (C8, C11, C12) | The documents | The owner |
| Two administrators have a second step; the keys are in the vault (C17) | Both enrolled; the vault's list | The owner |
| Runbook section 8 has no open row that touches money, sign-in or the backups | Section 8 | The owner |

## Hypercare: the first two weeks

From 1 April to 14 April 2027.

**Who answers.** The owner, first line, 08:00 to 22:00 IST every day, by phone and mail, and the alerts of runbook
section 20 at any hour.

| Severity | Examples | Response | Fixed by |
|---|---|---|---|
| Sev-1 | Drift; the hospital cannot bill or take money; a wrong amount on a printed document; data seen by the wrong hospital | Within 15 minutes | A release the same day, through the pipeline (runbook 19.2), or the hospital suspended (17.2) until it is |
| Sev-2 | A screen fails for one role; a report is wrong but the ledger is right; a job did not run | Within 4 hours | The next release |
| Sev-3 | Wording, layout, a slow screen | The next working day | When planned |

**How a defect is reported.** By mail to the owner, with: the hospital's code, the user, the time, the screen's
address, the document's number, what was done and what was seen, and a screenshot. Never a password, a code or a
patient's phone number. The owner finds the requests of that user and time in Seq (runbook 19.8) and answers with
what will happen and when.

**What is watched each day,** at 09:00 and at 18:00:

- the night's reconciliation: no "Reconciliation drift" mail; the report shows no row;
- the errors in Seq since the last look, grouped by message;
- refused sign-ins, and users locked out by the sign-in limits or by wrong codes;
- the day-end report of the day before: every shift closed, and every difference explained by its supervisor;
- the pending approvals: nothing older than a day;
- the watchdog's last rounds and the backup of the night.

In the first week a call of 15 minutes at 18:00 with the hospital's administrator and a supervisor: what went wrong,
what was asked, what is fixed. At the end of the two weeks, a written summary, and the support contract (C12) takes
over.
