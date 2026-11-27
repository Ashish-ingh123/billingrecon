# ADR 0062: The accountant's export

- **Status:** Accepted. On 2026-11-27 the owner answered the four questions Prompt 11.4 raises first (Tally; one
  voucher for each document; one control ledger for what payers owe; the tax of a bill split to a ledger of its own)
  and then approved the plan "as recommended", with its three points: a TenantAdmin, a Supervisor and a Viewer may
  export; the split into 11.4a (this record: the names, the API and the file) and 11.4b (the two screens, which will
  amend it); and the three voucher types Receipt, Payment and Journal.
- **Date:** 2026-11-27
- **Deciders:** project owner, Claude Code
- **Amends:** ADR 0036 (a tenth report, which is a file and not every role's), ADR 0032 (one policy more), ADR 0033
  (a fourth thing an administrator sets: the names of the accountant's ledgers).
- **Builds on:** ADR 0005 (the ledger), ADR 0003 (the module rule), ADR 0007 (row versions), ADR 0020 (audit by
  events), ADR 0053 (opening balances), ADR 0060 (the money of a shift by its method).

## Context

A hospital's accountant keeps the books in an accounting package, in India most often Tally. Until now the figures of
BillingRecon reached those books by hand from the reports. Prompt 11.4 asks for a file of a period that the package
imports, every voucher balanced and naming our document, with a control total equal to the ledger's movements of the
period; for a mapping, set by the hospital, from our ledger to the accountant's ledger names, because the chart of
accounts is the hospital's and not ours; and for the file format to be confirmed against Tally's own documentation
before any code, saying what could not be confirmed.

## Decision

1. **One posting of our ledger is one voucher, and its lines are the posting's entries.** A voucher balances because
   the posting does (ADR 0005), and the vouchers of a period add up to the ledger's entries of the period by
   construction. Nothing is summarised: a figure in the accountant's books is traced to one document of ours. A
   reversal is a posting of its own and so the mirror voucher of what it reverses.
2. **Three voucher types.** A payment recorded is a **Receipt**; a refund and the reversal of a payment are a
   **Payment**; everything else (a bill, a credit note, an allocation, an application of credit, an adjustment, an
   opening balance, and the reversals of the two middle ones) is a **Journal**. So no cash or bank ledger is ever in a
   Journal, which Tally hides by default and whose import is not documented, and the types Sales and Credit Note,
   the second of which a company must switch on, are not needed.
3. **What payers owe is one control ledger.** Every entry to `Receivable` goes to one ledger of the accountant's
   books, whoever the payer; so do `UnappliedCash` and `CreditAvailable`, each to its own. BillingRecon stays the
   sub-ledger of patients and insurers, and the accountant's company does not grow by a ledger for each patient. The
   payer's name is in the narration.
4. **Two things are said in the accountant's words instead of ours.**
   - An entry to our `Cash` goes to the ledger of **the method the money came by**: the payment's, or the refund's.
     Five ledgers, which a hospital may name alike (card and UPI into one bank account).
   - The credit to `Revenue` of a bill, and the debit of a credit note, is **split into its taxable value and its
     tax**, the tax read from the document's own total (`TaxTotal`). One tax ledger: the split into CGST, SGST and
     IGST is the accountant's, from the bill register. The two parts add up to the entry, so the control total is
     untouched.
5. **Twelve names, set by the hospital.** `AccountingLedgers` (Identity) is at most one row for a tenant: a name for
   each of `Receivable`, `UnappliedCash`, `CreditAvailable`, `Revenue`, `TaxOutput`, `Adjustments`, `OpeningBalances`,
   `Cash`, `Card`, `Upi`, `BankTransfer` and `Cheque`. A TenantAdmin saves them under `If-Match`; each change is
   audited. A name is at most 100 characters on one line. We propose no names: they are the ledgers that already
   exist in the accountant's company.
6. **A missing name refuses the file and names the ledger.** 422 `accounting_ledger_not_mapped` lists every ledger
   the period uses and the hospital has not named. Only the ledgers the period uses are needed: a hospital that
   takes no cheques need not name that ledger. The summary answers the same list without refusing, so the screen can
   say so first.
7. **The control total, twice.** The summary answers the debits of the vouchers (`controlTotal`) beside the debits of
   the ledger's entries of the period read on their own (`ledgerTotal`), and what each ledger of the accountant's
   receives. A file whose vouchers do not add up to the ledger, or with a voucher that does not balance, is never
   written: the request fails. The accountant compares the control total with what the package says it imported.
8. **The voucher's number is our document's number.** The document's own posting carries it as it is. A posting that
   follows from a document (an allocation `AL`, its reversal `AR`, a payment's reversal `RV`, an application of
   credit `CA`, its reversal `CR`) carries the number with that mark and eight characters of a hash of the posting's
   id, so that no two vouchers of a hospital share a number. The posting's id is the voucher's `REMOTEID`.
9. **The file is Tally's XML for an import of vouchers**, written with the BCL's `XmlWriter` and no package: the
   envelope of an "Import Data" request for the report "Vouchers"; a `TALLYMESSAGE` for each voucher; in a voucher
   `DATE` as `YYYYMMDD`, `VOUCHERTYPENAME`, `VOUCHERNUMBER`, `NARRATION` and an `ALLLEDGERENTRIES.LIST` for each
   line, where a debit is `ISDEEMEDPOSITIVE` Yes with a negative `AMOUNT` and a credit No with a positive one.
   UTF-8 without a byte order mark, a tag to a line, the five entities of XML and no CDATA.
10. **It is a report: it never writes.** `GET /api/v1/reports/accounting-export` and its `/summary` are in Reporting,
    under the conventions of ADR 0036. A period is two business dates, both required, at most 92 days. The policy
    `AccountingExport` admits a TenantAdmin, a Supervisor and a Viewer; an accountant is given the role Viewer.

## What was confirmed against Tally's documentation, and what was not

Read on help.tallysolutions.com on 2026-11-27 (the pages "XML Integration", "Sample XML", "Understanding Tally XML
tags", the developer reference's case studies and FAQ, and the import FAQ), through a tool that summarises pages:

- **Confirmed:** the envelope of point 9; `VCHTYPE` and `ACTION="Create"`; the date format; `VOUCHERTYPENAME`,
  `VOUCHERNUMBER`, `NARRATION`; the ledger line with `LEDGERNAME`, `ISDEEMEDPOSITIVE` (Yes is a debit) and `AMOUNT`;
  that every ledger must exist in the company before the import and that errors are written to `tally.imp`; the five
  entities, no CDATA, and line breaks between tags; that the voucher types Receipt, Payment and Journal are
  predefined.
- **Shown only by example, never stated:** that a debit's amount is negative and a credit's positive. Every sample
  of Tally's does it, and its Excel import says the same.
- **Not confirmed:**
  - whether `ALLLEDGERENTRIES.LIST` or `LEDGERENTRIES.LIST` is right for our three types (both appear in samples);
  - whether a `REMOTEID` of ours stops a second import of the same file from doubling the vouchers (Tally warns
    that it can double them);
  - whether our `VOUCHERNUMBER` is kept, which depends on the numbering method of the company's voucher types;
  - the encoding a file imported through the menu must have (UTF-8 is documented for requests over HTTP);
  - a second header shape (`VERSION`, `TALLYREQUEST` Import, `TYPE` Data, `ID` Vouchers) is documented beside ours,
    and nothing says which the menu's import prefers.
- **No file of ours has been imported into a Tally.** That is `docs/go-live.md` G10: the accountant imports a sample
  month into a company made for the test, twice, and signs the totals off. The writer is one class
  (`TallyVoucherWriter`), so a tag that must change is a change in one place.

## Consequences

- An accountant imports a month instead of typing it, and each line of the books names our document.
- The names are the hospital's work before the first export, and the ledgers must exist in the package first.
- **The file holds payers' names** (in the narrations), so it is personal data leaving the application, like the
  CSV of a report: whoever may read every report may make it, and the hospital keeps it as it keeps its books.
- **An export is not audited:** Reporting never writes (ADR 0036). Who downloaded which period is in the request log
  only. In the backlog.
- **A period exported twice is imported twice** unless the package stops it, which is not confirmed (above). The
  accountant imports each period once; the screen will say so (11.4b).
- A document posted late into a period already exported (a backdated bill, within the hospital's backdating limit)
  is not in the file made before it. The summary of a period is the check: its voucher count and control total
  change. In the backlog.
- Tax is one ledger. A hospital that needs CGST, SGST and IGST apart in its books splits it there.

## Alternatives considered

- **One summary voucher for a day and a kind.** Small files, but no trace from the books to a document, and the
  prompt asks for our document number on every voucher. The owner chose vouchers per document.
- **A ledger for each payer**, or for each insurer. Tally's usual shape for a Sales voucher, and tens of thousands of
  ledgers for a hospital. The owner chose one control ledger.
- **Sales and Credit Note vouchers** for bills and credit notes. They want a party ledger and, for credit notes, a
  feature switched on; a Journal carries the same lines with neither.
- **The names as columns of `Tenants`.** Twelve more columns on a row every request reads; a table of its own keeps
  the audit trail of the names apart from the hospital's other settings.
- **A generic CSV of journal lines.** No package imports it without work on the accountant's side. It can be added
  beside the writer if a hospital uses another package: the vouchers are read once and written by one class.

## Built note (2026-11-27, Prompt 11.4a)

- Migration `0033_AccountingLedgers`: the table of the twelve names, one row for a tenant.
- The ledger's total of point 7 is added up in the application from the entries read on their own, not by the
  database: a sum of `Money` is not one the database is asked for anywhere in Reporting.
- The mark of point 8 is a hash of the posting's id and not its last characters, as first written: ids made one after
  the other end alike, and the test of the month found two vouchers with one number.
- Verified on .NET 10 with LocalDB 2019 in a throwaway copy. The file was read back by an XML parser in the tests,
  never by Tally. The two screens are Prompt 11.4b.
