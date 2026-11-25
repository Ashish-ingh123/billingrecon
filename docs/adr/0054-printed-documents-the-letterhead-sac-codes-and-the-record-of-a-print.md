# ADR 0054: Printed documents: the letterhead, SAC codes and the record of a print

- **Status:** Accepted. The owner approved the plan of Prompt 10.4 on 2026-11-25 with its five choices: printing
  through the browser, no logo upload yet, a recorded print for the duplicate mark, CGST and SGST and never IGST, and
  the prompt split into 10.4 (this record's data) and 10.4b (the printed pages).
- **Date:** 2026-11-25
- **Deciders:** project owner, Claude Code
- **Builds on:** ADR 0004 (money), ADR 0020 (audit), ADR 0033 (tenant settings), ADR 0034 (grants), ADR 0050 (print
  styles), ADR 0058 (the reference hospital, whose formats stand in for a client's).
- **Implements:** nothing of BR-202 or BR-203 changes: no figure is computed differently.

## Context

A hospital hands a patient a bill, a receipt for a payment and a voucher for a refund, on its own letterhead. R1 left
printable templates out. The model lacked what such a page needs beyond the document itself: who the hospital is in
law and where, its GST registration, how a service is classified, and whether a page in the hand is the first print or
a copy. There is no client's format and no accountant (ADR 0058); what follows is built for the reference hospital,
and what only an accountant decides is said to be open.

## Decision

1. **The letterhead is ten optional parts of the tenant** (`Tenants`): `LegalName`, `AddressLine1`, `AddressLine2`,
   `City`, `State`, `PinCode`, `Phone`, `Email`, `Gstin`, `Signatory`. A TenantAdmin sets them with
   `PUT /api/v1/settings/letterhead` under the version of the settings, which they share as columns of one row;
   `Tenant.ChangeLetterhead` raises `TenantLetterheadChanged` only when something changed. Anyone signed in reads them
   with `GET /api/v1/settings/letterhead`: whoever may print needs them, and they say nothing a printed page does not.
   A GSTIN is fifteen capital letters and digits; whether it is a registration the application cannot know.
2. **No logo yet.** An uploaded image needs somewhere to keep files, which is a decision of the production prompt
   (10.5). The printed page carries the names in type.
3. **The SAC code** (six digits, optional) is a field of the service item. A bill line keeps the code its item had when
   the line was written (`BillLines.SacCode`), taken from the catalogue by the resolver and never from the request, so
   a posted bill prints the code it was raised under whatever the catalogue says later. A free-text line has none.
4. **Derived at print, never stored:** whether a bill is a *tax invoice* (it carries tax) or a *bill of supply* (it
   carries none, with the exempt-service wording); the split of a line's stored tax into CGST and SGST, CGST being half
   of it rounded as BR-001 rounds and SGST the rest, so that the two always sum to the stored tax; the place of supply,
   which is the hospital's state; and the amount in words. **No IGST:** a hospital's services are supplied where the
   hospital is. This is Claude Code's reading for the reference hospital, not tax advice; a real hospital's accountant
   decides the document and its wording (`docs/go-live.md`).
5. **A print is recorded, and that is how a duplicate is known.** `DocumentPrints` holds one row a print: the kind of
   document (`Bill`, `Payment`, `Refund`), its id, a copy number gapless from 1 by a unique index, when and by whom.
   `POST /api/v1/{bills|payments|refunds}/{id}/prints` records one before the page is printed and answers the copy
   number; the first is the original, every later one a duplicate. It needs an `Idempotency-Key`, so a request sent
   twice is one print, and the read policy of its document, since whoever may read a document may print it.
6. **The record is its own trail.** `DocumentPrint` is append-only (`IAppendOnly`; `DENY UPDATE, DELETE` for the
   application's role, written by hand at the end of the migration as ADR 0034 asks) and raises no event: rule 11
   audits a change of state, and a print changes none; who printed what and when is the row itself.
7. **Only what took financial effect is printed:** a posted bill (an opening receivable included), a payment that was
   not reversed, a posted refund. Anything else is 422 `document_not_printable`. The one use case serves the three
   kinds; whether a document can be printed is read by `PrintableDocuments` in Reporting's Infrastructure, the folder
   that may name every module's entities and writes nothing (ADR 0036).
8. **Printing is the browser's.** Prompt 10.4b builds three pages laid out for A4 on the print styles of ADR 0050; the
   user prints or saves as PDF. No PDF library: it would be a new package for what the browser does.

## Consequences

- A hospital with no letterhead set prints its name alone; nothing forces the parts.
- Two prints of one document at the same instant cannot both be the original: the second meets the unique index and is
  answered 409, and the client sends it again.
- A print that is recorded and then not printed (the dialog was cancelled) still counts: the next is a duplicate. The
  mark errs towards "Duplicate", which is the safe side.
- Migration `0024_PrintedDocuments`: ten columns on `Tenants`, `SacCode` on `ServiceItems` and `BillLines`, the table
  `DocumentPrints`.

## Alternatives considered

- **A count of prints on the document.** A posted document never changes (rule 2), and a count is a change.
- **An audit record per print with no table.** The audit trail records changes of state of an entity; a print is not
  one, and the copy number would have to be counted from audit rows.
- **IGST by the patient's state.** The patient's state is free text, and the service is supplied at the hospital.

## Built note (2026-11-25)

- The settings screen has two forms on one row and one version. When one saves, the page makes the other anew so that
  it holds the new version; text typed into the other and not saved is lost then.
- Tests changed because behaviour changed, none loosened: the bodies a service item form sends now carry `sacCode`,
  and the lists of columns, keys, append-only entities, migrations and endpoints name the new ones.
- Verified on the office machine on LocalDB 2019 and in Edge against the built app; SQL Server 2022 and nginx are owed
  (runbook section 8, row 10.4). The accountant's sign-off of a printed sample cannot be had without a client and is
  an open line of `docs/go-live.md`.

## Amended 2026-11-25 (Prompt 10.4b): the printed pages

Built as the plan of Prompt 10.4 described its second half; no endpoint changed.

1. **Three pages, `frontend/src/printing`:** `/bills/:id/print`, `/payments/:id/print`, `/refunds/:id/print`, each
   under the read policy of its document and reached by a link on the document's page, which is shown only for what
   can be printed. Each is a sheet (`PrintSheet`) with the letterhead, the title, which print it is, the document, and
   the place of a signature. On paper the application around the sheet and the bar above it are gone, by the print
   styles of ADR 0050, and the text is dark on white in either theme.
2. **Print records first, then opens the dialog.** The button sends `POST .../prints` with a key of its own for each
   press and, once the answer says which print it is, calls the browser's print. The sheet then says "Original" or
   "Duplicate (copy n)" with the time. A page printed some other way, before the button was pressed, says "Unrecorded
   copy": it is honest about not having been counted.
3. **The bill** is titled by what it carries (`tax.ts`): "Tax Invoice" with each line's stored tax in its CGST and SGST
   halves and their rates, and the two totals; "Bill of Supply" with the exempt wording when it carries no tax;
   "Statement of Opening Balance" for an opening receivable, with its earlier number and no tax fields, since that
   bill was not issued here. The place of supply is the letterhead's state. Every figure is the stored one.
4. **The receipt** shows the active allocations by the number of their bills and what is not yet applied as of the
   print; an opening advance is titled "Statement of Opening Advance". A reversed payment has no receipt. **The refund
   voucher** names its source by number, its reason, and a line for the receiver's signature.
5. **The amount in words** (`words.ts`) is exact on whole paise, in lakh and crore, with the crores themselves counted
   the same way above a thousand crore; one rupee and one paisa are singular.
6. **Verified** in Edge against the built app with a stubbed API: each document in both themes recorded, marked and
   printed to an A4 PDF, a phone, axe; the three routes in the every-route suite. Through nginx on a real printer it is
   owed (runbook section 8, row 10.4b), and so is the accountant's sign-off of a printed sample (`docs/go-live.md`, G1).
