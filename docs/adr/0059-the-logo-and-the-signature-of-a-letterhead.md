# ADR 0059: The logo and the signature of a letterhead

- **Status:** Accepted. The owner approved the plan of Prompt 10.4c on 2026-11-26 "as recommended", with its split
  into 10.4c-1 (this record: the images) and 10.4c-2 (the print settings, which will amend this record), a removal by a
  `POST` action and not a `DELETE`, and a duplicate that carries the signature.
- **Date:** 2026-11-26
- **Deciders:** project owner, Claude Code
- **Amends:** ADR 0054 point 2 ("No logo yet") and its amendment of 2026-11-25, point 1 (the sheet's head and foot).
- **Builds on:** ADR 0018 (the HTTP contract), ADR 0020 (audit by events), ADR 0034 (grants), ADR 0035 and ADR 0045
  (the headers and nginx's policy), ADR 0047 (the tokens), ADR 0053 as amended (what one request through nginx carries).
- **Number:** 0056 stays with Prompt 10.6, so this one is 0059.

## Context

The letterhead of ADR 0054 is text. A hospital's printed bill carries its logo, and is signed: the signatory's
signature, name and designation. ADR 0054 put the logo off because "an uploaded image needs somewhere to keep files".
Since then production is one server with SQL Server and no object store (ADR 0055), nginx lets a request carry 1 MB,
and the page's content security policy admits images from the app's own origin and from `data:` URLs only.

## Decision

1. **The images are kept in the database**, in a table of their own, `TenantImages`: the tenant, the kind (`Logo` or
   `Signature`), the format, the bytes as they were uploaded, their SHA-256, the width and height their header states,
   and when they were stored. Not columns of `Tenants`: that row is read on many requests and its audit images are the
   whole row, and neither may drag an image along. A backup of the database is then a backup of the images, and there
   is no second place to secure, restore or forget.
2. **A row is written once and never changed or deleted** (`IAppendOnly`; `DENY UPDATE, DELETE` for the application's
   role, written by hand at the end of migration `0025_LetterheadImages` as ADR 0034 asks). **The tenant names the
   image it prints now by its hash**: `Tenants.LogoSha256` and `Tenants.SignatureSha256`. Replacing an image stores a
   new row and changes the name; removing one clears the name; the same bytes uploaded again are the row there is
   (`UX_TenantImages_TenantId_Kind_Sha256`). So no fifth entity may be hard-deleted (rule 10), and a hash in the audit
   trail always finds the bytes it stood for.
3. **What an image may be.** PNG, JPEG or WebP, at most 256 KB for the logo and 128 KB for the signature, at most 2000
   pixels a side. The format and the size in pixels are read from the file's own bytes (`LetterheadImageInspector`: the
   signature at its start, then the header of its format); what the client called the file is never believed, and the
   `Content-Type` of the request must be what the bytes are. **SVG is refused**, like any file that is not one of the
   three: it is a document that can carry script. No image package: the pixels are not decoded on the server.
4. **The upload is the file itself**, as the body of `PUT /api/v1/settings/letterhead/logo` (or `/signature`), not
   JSON: base64 would add a third to it inside nginx's 1 MB for nothing. The API reads the body up to one byte past the
   limit of the kind and no further. A TenantAdmin's (`ManageTenant`), with `If-Match` on the version of the settings,
   which the letterhead and its images share; it answers the settings. A refusal is 400 `validation_failed` with its
   reason under `content`.
5. **A removal is `POST .../logo/remove`** (or `/signature/remove`), under `If-Match`. The API has no `DELETE`
   (ADR 0018) and this adds none.
6. **Audited by the hash, never the image.** `Tenant.ChangeLetterheadImage` raises `TenantLetterheadImageChanged` with
   the kind and the hash before and after, only when the hash changes; the audit images are the tenant's row, which
   holds the two hashes and no bytes. The image row raises no event: it is what the hash names.
7. **Reading.** `GET /api/v1/settings/letterhead/logo` (and `/signature`) answers the bytes with their media type, for
   anyone signed in, as the letterhead itself is read (`SignedIn`): whoever prints a document needs them. The path
   names no hospital; the tenant of the token decides whose image it is, and a hospital with none is answered 404
   `letterhead_image_not_found`. The letterhead answers `logoSha256` and `signatureSha256`, so a page knows whether to
   ask, and asks again only when the hash has changed.
8. **How the browser shows them: a `data:` URL, and no change of nginx's policy.** An `<img>` cannot send the token, so
   the page reads the image with `fetch`, as it reads everything, and gives the `<img>` the bytes as a `data:` URL of
   their type. The policy of `frontend/deploy/nginx.conf` has admitted `img-src 'self' data:` since ADR 0045; `blob:`
   is not added and nothing comes from another origin. The page builds a `data:` URL only for `image/png`, `image/jpeg`
   and `image/webp`.
9. **On the sheet.** With a logo, it stands at the head of the bill, the receipt and the refund voucher in place of the
   BillingRecon mark, in a box of 9 rem by 4.5 rem. With a signature, the foot says "Authorised signatory", then the
   image in a box 3.5 rem high, the line, the signatory's name and the designation (`Tenants.SignatoryDesignation`, a
   new part of the letterhead). The boxes are taken before the images arrive, so nothing on the sheet moves. **Print is
   offered once the images the letterhead names are here or have failed**, and the dialog opens when the browser has
   decoded them; an image that cannot be read never holds a print back. **A sheet with no image prints as it did.**
10. **A duplicate carries the signature.** It is a copy of the signed document and already says "Duplicate (copy n)";
    a copy without the signature would look like another document.
11. **Whether a scanned signature is enough on a Tax Invoice is the accountant's call**, not this record's: an open
    line of `docs/go-live.md` (G7).

## Consequences

- Migration `0025_LetterheadImages`: `SignatoryDesignation`, `LogoSha256` and `SignatureSha256` on `Tenants`; the
  table `TenantImages` with its CHECK constraints, its unique index and its `DENY`.
- **A removed or replaced image stays in the database.** It is small, and it is what makes the audit's hash mean
  something; but a file uploaded by mistake is not erased by the application. An operator with the database's
  administrator login removes such a row by hand; nothing names it once the tenant does not.
- A file whose header is sound and whose data is damaged is stored, and shows as a broken image in the preview, where
  the administrator sees it at once.
- The preview on the Settings screen stands on the white of paper in both themes (two new tokens, `paper` and
  `paper-ink`), because a signature is dark ink on nothing and would vanish on a dark card.
- The image endpoints are the first of this API whose body is not JSON. The OpenAPI document describes no body for
  them, so the frontend gives the body past the generated client's types, in one function.
- The label of the letterhead's field changes from "Signatory line" to "Signatory": it is the name under the signature.
  A hospital that had typed "Authorised signatory" there keeps a sheet that reads as before.

## Alternatives considered

- **Files on the server's disk or an object store.** A second thing to back up, restore, encrypt and keep per tenant,
  for at most 384 KB a hospital.
- **Columns of `Tenants`.** Read with every request that reads the tenant, and written whole into every audit image.
- **`blob:` URLs, with `img-src` gaining `blob:`.** It works, and widens the policy for what `data:` already does;
  a `blob:` URL must also be revoked by hand or it leaks.
- **The token in the image's address, or a cookie for images.** A token in an address is logged and kept in history;
  a cookie is a second way to be signed in, with its own forgery questions (the API has none today, ADR 0031).
- **JSON with base64.** A third larger, and the fingerprint of nothing: the request is not idempotent by key.
- **Deleting the row on removal.** A fifth hard-deletable entity and a `GRANT DELETE`, to save a few kilobytes and lose
  what an audited hash referred to.
- **An image library to decode and re-encode every upload.** It would strip what a file hides and prove it decodes;
  it is a package with its own advisories, and the image is only ever given to an `<img>` of the hospital's own staff,
  under `nosniff` and a policy that runs no script from it. Left for the review of Prompt 10.8 to argue if it wishes.
- **No signature on a duplicate.** Considered and refused in point 10.

## Built note (2026-11-26)

- Verified on the office machine: the strict build and the format gate; Domain 696, Application 470, Architecture 138
  and Integration 798 tests without a database in the repository; the `Database` collection, 530 tests, in a throwaway
  copy on LocalDB 2019 (the four of `LetterheadImagesApiTests` and the permission walk with the six new rows among
  them). The frontend's format, lint, types, 560 component tests, build and budgets. In the installed Edge against the
  built app, under the policy of `nginx.conf`: the 13 tests of `e2e/letterhead.spec.ts`, and the other stubbed suites
  (334 tests) before the preview's ground was made paper white, with the settings and print routes (105) again after.
- **Not verified:** SQL Server 2022, nginx itself, a real printer, and real logo and signature files of a hospital
  (runbook section 8, row 10.4c). The test images are drawn by the tests.
- Tests changed because behaviour changed, none loosened: the letterhead a test expects now carries
  `signatoryDesignation`, `logoSha256` and `signatureSha256`; the lists of append-only and tenant-scoped entities, of
  the tenant's columns, of the migrations, of the endpoints and of the controllers with a non-financial POST name the
  new ones.
- Two of the new tests were corrected while they were written, before they had ever passed: a JPEG's size is stated
  36 bytes in, not 37; and the paper's edge is asked for only where the paper's own ground does not already tell it
  from the card (the dark theme).

## Amended 2026-11-26 (Prompt 10.4c-2): print settings for each kind of document

The owner approved the plan of 10.4c-2 on 2026-11-26 "with name" (the cashier's name on a receipt and a voucher). It
amends ADR 0054 point 8 and its amendment's point 1: a sheet is no longer one fixed layout on A4.

1. **One row for each hospital and kind of document**, table `PrintSettings` (`Bill`, `Payment` for its receipt,
   `Refund` for its voucher), with its own row version, made by the first save of that kind. **A kind with no row
   prints by the defaults, which are the sheet as it was** (`PrintLayout.Default`): A4, margins 14 and 12 mm, the logo
   at the left, medium; the payer, the SAC column, the tax split, the amount in words and the signature block shown;
   the patient's contact and the cashier's name not shown; no footer line, no terms. Migration `0026_PrintSettings`.
2. **What can be set:** the paper (A4, A5; an 80 mm roll for a receipt or a voucher); four margins of 5 to 25 mm; the
   logo at the left, centre or right and small, medium or large, or **pre-printed stationery** with 0 to 80 mm left
   empty at the top and no head printed; seven switches; a footer line (200 characters) and terms (1000), plain text.
3. **What a bill may never switch off.** ADR 0054 names no field "mandatory"; the owner approved this list on
   2026-11-26, pending an accountant (`docs/go-live.md`, G1): for a bill, the SAC column and the tax split are always
   on (the validator refuses false with the reason, two CHECK constraints refuse it for whatever writes the table, and
   the sheet shows both on a bill that carries tax whatever a row says); a bill is not printed on a roll. The number,
   the date, the totals and the mark of the original or a duplicate have no switch at all.
4. **Settings change the layout only.** No figure is computed or shown differently; the print is recorded and marked as
   before. A test prints each sheet by the defaults and by a changed layout and holds every figure and the mark equal.
5. **The API.** `GET /api/v1/settings/print` for anyone signed in (three answers, the defaults for a kind never saved,
   whose version is eight zero bytes); `PUT /api/v1/settings/print/{documentType}` for the TenantAdmin with `If-Match`
   (the version of none for a first save), audited as `PrintSettings` `Changed` with what the settings were and are.
6. **No inline style, and no change of nginx's policy.** Paper, logo place and size are fixed classes. The margins,
   the paper's size for the printer and the stationery's space are numbers: the sheet writes them into one stylesheet
   object of the page (`adoptedStyleSheets`, `settings.ts`), built from numbers rounded and held within their limits
   and from three fixed words, never from text a user typed. Verified in Edge under the policy of `nginx.conf`
   (`style-src 'self'`): the rules apply, nothing is refused, and no element of the page carries a `style` attribute.
7. **The preview is the sheet.** `SheetFrame` and the three bodies (`BillBody`, `ReceiptBody`, `RefundBody`) are pure:
   the printed page gives them the document, the preview a sample. A test renders both from the same settings and
   holds the markup equal but for the mark ("Preview"). The preview stands on the tokens of paper in either theme
   (`[data-paper]` in `tokens.css`, held equal to the print block by a test).
8. **The cashier's name is a read of its own**, `GET /api/v1/payments/{id}/recorder` and
   `GET /api/v1/refunds/{id}/recorder`, under the read policy of its document, answering the display name alone; asked
   of Identity through a new contract, `IUserNames`, so Payments names no user entity. **Not a field of the payment and
   refund answers, as the plan first said:** built that way, nine existing tests failed, which hold that a document
   reads the same as the change that made it answered; the plan was wrong and the tests were right. The sheet reads the
   name only when its settings ask, and Print waits for it.
9. **The roll** is given to the printer as 80 mm by 297 mm: CSS cannot say "as long as the content"; the printer's own
   driver cuts the paper. Held by a test of the PDF's page box.

### Consequences of the amendment

- A tax invoice has nine columns. On A5 its table is set at 10 px with close padding and only the description wraps;
  it fits with side margins of 12 mm (tested) and may not with the widest margins. The form says so. A figure is never
  broken inside itself.
- Text typed into a form and not saved is kept while the document is switched only if it is saved first: each kind has
  a form of its own.
- A closing slip (Prompt 11.2) will be a fourth kind of document: a value of `PrintedDocumentType` and of two CHECK
  constraints.

### Built note of the amendment

- Verified on the office machine: the strict build and the format gate; Domain 713, Application 478, Architecture 138
  and Integration 798 tests without a database; the `Database` collection, 534 tests, on LocalDB 2019 in a copy; the
  frontend's gates and 614 component tests; in Edge against the built app under nginx's policy, 359 browser tests
  (351 before the A5 table and the form's note were changed, then the 30 of the three print suites and the 52 of the
  settings and print routes again).
- **Not verified:** SQL Server 2022, nginx itself, and paper: A5 and an 80 mm roll on real printers, and real
  pre-printed stationery (runbook section 8, row 10.4c).
- Existing tests: `Printing.test.tsx` and two browser suites gained one route in their faked API (the settings read,
  answering the defaults); no assertion changed. Defects the new tests found and the code then fixed: a measure that
  was no number written into a rule; the form rebuilt after a save, so that "Saved" never showed; the preview's quiet
  text in the dark theme; the tabs and the preview wider than a phone; figures broken mid-number on A5.

## Decided 2026-11-26 (Prompt 10.4c, part C): no "Download PDF" now

The owner chose, on the proposal of part C: **no server-made PDF for now.** The browser's print dialog saves a PDF of
the sheet, laid out by the hospital's settings and carrying the recorded mark; tests hold that PDF's page size for A4,
A5 and the roll. ADR 0054 point 8 stands.

- **Headless Chromium rendering the built sheet** (a container beside the API) keeps one layout but puts a browser on
  the server that must load the app as the user. It is the way to take if something must make a PDF with no person at
  a browser (a bill sent by e-mail, a patient portal): backlog B37.
- **A .NET PDF library drawing the sheet a second time** was refused: every paper, margin, switch and the nine columns
  of a tax invoice would be built twice, with no test that holds the two equal part for part.
- Either way a download would be recorded by the existing `POST .../prints` before the PDF is made, and carry its mark.
- The versions, licences and image sizes of the candidates were not looked up: nothing was chosen that needs them.
