# Go-live

What stands between BillingRecon and a first live hospital that is **outside the code**: each line with who decides it
and by when. Prompt 10.7 completes this document; until then it holds what earlier prompts of Phase 10 leave open. No
line here is decided by Claude Code (the bar of Phase 10, point 5).

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

## Still to be written by Prompt 10.7

The DPDP review, the data processing agreement, the penetration test, the hospital's acceptance, the support and
on-call arrangement, and the cut-over plan with its rollback.
