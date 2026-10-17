# ADR 0008: Gapless document numbering

- **Status:** Proposed (accept on review of the R1 design documents)
- **Date:** 2026-09-29
- **Deciders:** project owner, Claude Code

## Context

Indian tax-invoice rules (CGST Rules, rule 46(b)) require a **consecutive serial number, unique for a
financial year, of at most sixteen characters**, allowing letters, digits, `-` and `/`. Auditors
treat a missing number as a missing document. Hospitals bill in the Indian fiscal year
(1 April – 31 March, BR-004) and restart series each year. CLAUDE.md rule 7 requires gapless,
strictly sequential numbers per (tenant, document type, fiscal year), taken inside the posting
transaction (BR-801).

Gaplessness is hard because the obvious tools are not transactional: SQL Server `IDENTITY` and
`SEQUENCE` values are consumed even when the transaction rolls back, so a failed posting leaves a
hole. Several API instances may post concurrently for the same tenant, and the first posting of a
new fiscal year must create the series row without a race.

## Decision

We will allocate numbers from a **`NumberSeries` table row per (tenant, document type, fiscal
year), locked with `UPDLOCK, HOLDLOCK` inside the posting transaction**, as the last step before the
document is written.

- **Table** `NumberSeries(TenantId, DocumentType, FiscalYear, Prefix, NextNumber)` with a unique
  index on the first three (`docs/database.md` §3.6). Document types: `Bill`, `CreditNote`,
  `Payment`, `Adjustment`, `Refund`.
- **Allocation** (`docs/database.md` §4.1): on the posting transaction's connection,
  `SELECT NextNumber FROM NumberSeries WITH (UPDLOCK, HOLDLOCK) WHERE …`; if no row, `INSERT` it
  with `NextNumber = 1` and the tenant's default prefix; then `UPDATE … SET NextNumber += 1`.
  `UPDLOCK` serialises concurrent posters on the row; `HOLDLOCK` holds the key-range lock so that
  two first-of-year postings cannot both insert. The locks release at commit or rollback, and a
  rollback leaves `NextNumber` untouched, so a failed posting consumes nothing.
- **Ordering.** `INumberSeries.NextAsync` is called **after all validation** and immediately before
  the document, ledger and audit writes, so the series row is locked for the shortest time. A use
  case takes at most one number (credit note posting takes only the credit-note number; the
  auto-application is an allocation row, not a numbered document), so no two series are ever locked
  in one transaction and series locks cannot deadlock each other.
- **Fiscal year** comes from the document's business date (`FiscalYear.FromDate(BillDate)`), not
  from the clock: a bill dated 31 March 2027 posted on 2 April 2027 takes the next `2026-27` number.
- **Format** `PREFIX/YY-YY/NNNNNN`, for example `BIL/26-27/000123`: prefix 1–3 characters `[A-Z0-9]`,
  short fiscal-year label, six-digit zero-padded sequence. At most **16 characters** for sequences
  up to 999 999, satisfying rule 46(b). Beyond 999 999 in one year the sequence widens to seven
  digits (accepted: no R1 tenant approaches a million bills a year). Default prefixes `BIL`, `CN`,
  `PAY`, `ADJ`, `REF` are tenant settings (BR-802).
- **Storage.** Each document table stores `Number`, `FiscalYear` (`2026-27`) and `SequenceNumber`,
  with filtered unique indexes on `(TenantId, Number)` and `(TenantId, FiscalYear, SequenceNumber)`.
  The gapless check `COUNT(*) = MAX(SequenceNumber)` per (tenant, type, year) runs in integration
  tests and the reconciliation report (`docs/database.md` §4.3).
- **Prefix changes** are maker-checker actions (BR-604) and apply to future numbers only;
  `NextNumber` never decreases.

## Alternatives

- **`IDENTITY` column.** Values are not transactional (a rollback burns one) and there is one series
  per table, not per tenant and fiscal year.
- **`CREATE SEQUENCE`.** Same non-transactional behaviour, plus caching that loses values on
  restart; also not partitioned by tenant or year.
- **`MAX(SequenceNumber) + 1` with a unique index and retry.** Without a range lock two posters read
  the same max; the loser fails on the unique index and retries, which under load becomes a
  livelock and still needs the serialisable range lock to be safe.
- **Assign the number at draft creation.** Voided drafts would leave gaps (BR-204 allows voiding).
- **Assign the number after commit by a background job.** The client's response would lack the
  number, and the window between commit and numbering violates "posted implies numbered" (BR-206).
- **`sp_getapplock` per series.** Works and is explicit, but the row lock gives the same
  serialisation, is scoped to the transaction automatically and needs no lock-name convention.
- **A process-wide `SemaphoreSlim`.** Safe only for a single API instance; two instances would
  duplicate.
- **Serializable isolation for the whole posting transaction.** Broader locking than needed
  (bills, payers, balances), higher deadlock risk, for no extra guarantee on the series row.
- **Long format `BILL/2026-27/000123`.** More readable, but 19 characters exceeds the 16-character
  limit of rule 46(b) for tax invoices; not worth a per-tenant exception.

## Consequences

- Postings of one document type in one tenant and year serialise on one row; throughput per series
  is bounded by transaction length. Keeping the locked section short (validation first, number last)
  keeps this well above hospital volumes (NFR: < 20 ms inside the transaction).
- A posting's number is known only inside the transaction; the API returns it in the response after
  commit.
- Correctness depends on the business date being right (ADR 0010) and on the use case taking the
  number inside the same transaction as the document, which the integration test for a failed
  posting (US-25) verifies.
- The prefix limit (3 characters) and the short year label are visible to users on printed bills;
  `docs/api.md` and the front end use the stored `Number` string as-is.
