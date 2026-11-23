# ADR 0008: Gapless document numbering

- **Status:** Accepted (built with Prompt 3.1 on 2026-11-20, with the owner's approval of its plan; see "Built 2026-11-20" below for what the build added and found)
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

## Built 2026-11-20 (Prompt 3.1)

`FiscalYear`, `DocumentType` and `DocumentNumber` (Domain, Common), the `NumberSeries` table, the port
`INumberSeries` (Application, Common) and its implementation `NumberSeriesAllocator` (Infrastructure,
Common/Numbering). No document takes a number yet; bill posting is the first (Prompt 3.5). What the build
decided beyond the text above, each with the owner's approval of the plan unless marked:

1. **Who opens the transaction.** A use case had no explicit transaction before: its unit of work was one
   `SaveChanges`. `INumberSeries.NextAsync` now opens a transaction on the request's context when none is open and
   takes the number on it; `UnitOfWork.CommitAsync` saves and commits that transaction, and rolls it back when the
   save loses a race or fails. A use case that takes a number and then returns a failure without committing ends
   with its request: the context is disposed, the transaction rolls back, and the number is free again. A use case
   that takes no number runs exactly as before.
2. **A number longer than sixteen characters is never issued.** The Decision above accepted that the sequence
   widens to seven digits after 999 999. With a three-character prefix that is seventeen characters, which rule
   46(b) does not allow. `DocumentNumber.Create` refuses to build such a number and the allocator turns that into a
   fault ("the series is exhausted"); the increment rolls back with the transaction. A two-character prefix such as
   `CN` still has room for seven digits. This replaces "accepted" in the Format paragraph.
3. **Raw SQL is banned, and this is the one sanctioned use.** The entry points of EF Core for raw SQL
   (`ExecuteSql*`, `SqlQuery*`, `FromSql*`, `GetDbConnection`) are now in `src/BannedSymbols.txt` (build error
   RS0030), which answers the open question ADR 0015 left. `NumberSeriesAllocator.cs` is the one file allowed to
   suppress the rule, for one call, listed in `BannedSymbolsTests` with its reason. Its statement is a constant;
   every value is a parameter; the tenant comes from the current scope and is in every WHERE clause; the instants
   come from the clock. A test checks that the file has one suppression, one call and no spliced text.
   (Since then three more files may suppress RS0030, ADR 0021 and ADR 0031; and since 2026-11-23 this file sends a second
   statement, the locked read alone, with which an approved prefix change holds its series, ADR 0033 "Amended
   2026-11-23".)
4. **The tenant's default prefix is read inside the same statement**, from the tenant's row, only when the series
   is created and under the same range lock. A tenant without a row gets no number and no series (a fault).
5. **The typed wrappers** (`BillNumber`, `PaymentNumber` and the others) are not built: each arrives with the
   document that uses it.
6. **`NumberSeries` is in the model as a read-only entity.** Nothing creates or changes a series through it; it
   gives the table its tenant filter, its foreign key and its migration, and lets a series be read. The prefix
   change (BR-802, maker-checker) will add the first method. *Done with Prompt 5.3b (ADR 0033):* `NumberSeries` is
   an aggregate with one method, `ChangePrefix`, which the approval of a prefix change calls for the series of the
   current fiscal year, through the model and its row version; numbers are still taken only by the allocator's
   statement, and a series is still created only there.

**Found while testing, and not in the text above (accepted by the owner on 2026-11-20):** the first number of a *new*
series can wait for another series, even another tenant's. `HOLDLOCK` locks the gap in the unique index where the
new row will go, and that lock reaches the next row in the index. When that row belongs to a series whose posting
is in flight, the first posting waits until that posting commits or rolls back, which is milliseconds. It happens
once per tenant, document type and fiscal year, it cannot deadlock (a use case locks one series), and nobody waits
for ever: a test with eight tenants shows it. A series that exists locks its own row and nothing else, so two
tenants with existing series never wait for each other, which another test shows. The Consequences paragraph
above ("postings of one document type in one tenant and year serialise on one row") is therefore true for
existing series and slightly too narrow for the first posting of each. Removing the wait would mean giving up
the range lock for an insert-and-retry on the unique index; not done, because the wait is short and the lock is
what this ADR chose. The owner accepted the wait on 2026-11-20.

**Verification.** Pre-checked on 2026-11-20 in the throwaway .NET 9 copy against SQL Server LocalDB 2019: fifty
parallel takers of a new series get 1 to 50 in three rounds, fifty and fifty more continue an existing series, a
number that is taken and not committed, or whose save fails, is handed out again, and the gapless verification
query of `docs/database.md` section 4.3 finds nothing after each. The number series tests ran eighteen times in a
row without a failure. Still to do on the machine with .NET 10 and Docker: scaffold `0005_NumberSeries` with
EF Core 10 and run the same tests against SQL Server 2022, repeatedly.
