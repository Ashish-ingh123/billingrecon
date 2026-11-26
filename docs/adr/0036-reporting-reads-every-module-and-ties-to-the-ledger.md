# ADR 0036: Reporting reads every module, writes none, and ties every figure to the ledger

- **Status:** Accepted. The owner approved the plan of Prompt 6.1 on 2026-11-23 "as recommended", with its seven
  answers, which are points 1 to 7 below. The built note at the end records what the build found.
- **Date:** 2026-11-23
- **Deciders:** project owner, Claude Code
- **Amends:** ADR 0003 (five more allowed exceptions to the module rule, amendment of 2026-11-23), ADR 0018 (the list
  answer of a report carries its totals).
- **Implements:** BR-901 (every R1 report but the reconciliation, which is Prompt 6.2), US-40, US-42; BR-003 (business
  dates in India), BR-404 (cached balances equal the ledger).

## Context

BR-901 asks for a bill register, a payment register with the daily collection by method, outstanding by payer, unapplied
cash and available credit, and the pending approvals, each figure tying back to ledger entries. A report joins what
several modules own: a bill (Billing) is owed by a payer (Payers) and posted to the ledger (Receivables); a payment
(Payments) is collected by a method and may be refunded; a request (Common) waits on a document of any of them. The
module rule (ADR 0003) lets a module use another only through its Application contracts, which answer one document or
one module's list, never a join of several, and `docs/domain-model.md` section 2 has always said that Reporting "reads
any module's tables through query services; never writes".

## Decision

1. **Reporting's query services read the tables of every business module.** Five allowed exceptions in
   `ModuleDependencyTests`: `BillingRecon.Infrastructure.Reporting` may depend on the Domain of Payers, Billing, Payments,
   Receivables and Adjustments, to name their entities and enums in Entity Framework queries. The reads go through the
   context, so the tenant filter applies to every one of them (ADR 0009). The Application and Api layers of Reporting
   get no exception: they use its own ports, queries and DTOs, and three words of their own (point 7). In exchange,
   **Reporting never writes**, and `ReportingConventionTests` keeps it so: no port that writes (`IUnitOfWork`, the
   idempotency store, the number series, the ledger, the payer accounts) and no repository; no call that adds, updates,
   removes, attaches or saves an entity, or sends SQL of its own, in any file of a Reporting folder; every set read
   with `AsNoTracking()`; only GET actions. Alternatives: a sanctioned raw-SQL file (a new one needs an ADR, and every
   filter would be hand-written with the tenant typed in), or composing the other modules' read services (no join and
   no paging across modules).
2. **Where each figure comes from, and how it ties to the ledger.**
   - The **registers** list documents with their own amounts. Their totals are what the ledger says: the total of the
     bill register equals the debits to Receivable of the bills' postings of the period (the balances due, the
     Receivable left on them); the total of the payment register equals the debits to Cash of the payments' postings,
     its unapplied total the balance of UnappliedCash.
   - The **balances** (outstanding by payer, a payer's balance, unapplied cash, available credit) are the cached
     balances of `PayerBalances`, `Payments` and `CreditNotes`, which the posting that changes the ledger changes in its
     transaction (BR-404, rule 4 of CLAUDE.md); the reconciliation report of Prompt 6.2 finds any drift.
   - The **daily collection** is read from the ledger itself: the Cash entries of the postings of that business date.
   The tests compute every one of these sums in SQL from `LedgerEntries` and hold the report against them.
3. **The daily collection (US-42)**, for an IST business date, today when none is given: one row per payment method,
   all five always listed. *Received*: the payments recorded with that date (their `PaymentRecorded` postings), and how
   many. *Reversed*: the payments whose reversal was posted that day (`PaymentReversed`, dated the day of the approval),
   under the payment's method. *Refunded*: the refunds of that date (`RefundPosted`), under the refund's method. *Net*:
   received less reversed less refunded. The totals' net is the movement of the Cash account on that date.
4. **Eight GET endpoints under `/api/v1/reports`:** `bill-register` (posted bills by bill date: `from`, `to`, `payerId`,
   `status`), `payment-register` (payments by payment date, reversed ones included: `from`, `to`, `payerId`, `method`,
   `status`), `daily-collection?date=`, `outstanding-by-payer` (payers with a balance that is not zero, the largest net
   outstanding first: `search`), `payers/{payerId}/balance` (US-40; 404 `payer_not_found`, found through the Payers
   module's own query), `unapplied-cash` and `available-credit` (the oldest first: `payerId`), `pending-approvals` (the
   oldest first: `documentType`; with each document's payer and amount where it has them, and the days it has waited
   in India). Chronological registers: by date, then by number. A register covers `from` to `to`, both given or neither,
   at most 366 days, the current IST month when neither is.
5. **The answer of a paged report** is `{ period, items, page, pageSize, totalCount, totals }`: the list contract of
   ADR 0018 with the totals of every page, so that a page can be held against the ledger, and the period a register
   covers (null for the other reports).
6. **One new policy, `Reports`,** admits all five roles, as the matrix's "Reports" row says. The pending-approvals report
   is a report, read by everyone; the checkers' inbox, `GET /api/v1/approval-requests`, stays theirs (ADR 0032).
7. **Reporting's own words.** The Application and Api layers of Reporting may not name the Billing and Payments enums,
   so the module has three of its own, the same names and numbers: `ReportBillStatus` (the three states of a posted
   bill), `ReportPaymentStatus` and `ReportPaymentMethod`. The Infrastructure maps them explicitly, both ways, and
   `ReportVocabularyTests` fails when Billing or Payments gains a word Reporting lacks.

## Consequences

- No migration: the indexes the reports use were planned with the tables (`IX_Bills_TenantId_BillDate`,
  `IX_Payments_TenantId_PaymentDate`, `IX_LedgerPostings_TenantId_BusinessDate`, `IX_ApprovalRequests_TenantId_Status_RequestedAtUtc`
  and the payer indexes).
- A report never takes a lock and reads committed rows (`READ_COMMITTED_SNAPSHOT`, ADR 0034), so it waits for no
  posting and sees each posting whole.
- The dashboard of Prompt 6.3 reuses these query services rather than copying their queries, as its prompt asks.
- A new business module's tables reach the reports only through a new exception in the test, with its reason.

## Built (2026-11-23)

Built as decided. What the build found, none of it a change of a decision:

- **Totals are added in memory.** Entity Framework cannot sum a property stored through a value converter (`Money` is a
  `decimal(18,2)` column behind `MoneyConverter`), and raw SQL is banned. So each paged report reads the amounts of every
  matching row as one or two columns and adds them in memory, then reads the page itself. A register is capped at 366
  days, the balance reports at the documents that hold something; at the volumes of the NFR (10,000 bills a month per
  tenant) that is at most some 120,000 decimals for a year's register.
- **The payer of a balance is found through `IQueryHandler<GetPayerQuery, PayerDto>`**, the Payers module's own use case:
  an id nobody has, or another hospital's, answers exactly what `GET /api/v1/payers/{id}` answers.
- **Comparisons of a converted value with `Money.Zero`** (`!=`) translate, as the comparisons of typed ids do; ordering by
  a `Money` column translates to ordering by the column.
- **A pending request's document** is read from its own table, one query per kind of document on the page; an allocation
  and an application of credit take the payer of their payment and credit note. A prefix change has neither payer nor
  amount.

## Amended 2026-11-23 (Prompt 6.2): the reconciliation report, gapless numbering, and drift checked after every test

The owner approved the plan of Prompt 6.2 "as recommended". What it decided and built:

1. **The reconciliation report (US-41): `GET /api/v1/reports/reconciliation`**, under the policy `Reports`. The
   Reporting module calls the Receivables module's `IReconciliationQuery` through its Application contract (no new
   exception to the module rule) and answers a paged report: the drift rows by identity, reference and subject, with
   `totals` of `checkedAtUtc`, `rows` and the count of each identity. When it finds drift, the API writes an error to the
   log with the count and the identities (rule 4 of CLAUDE.md: "fails loudly"). It corrects nothing.
2. **Identity 11, gapless numbering, joins the reconciliation** (CLAUDE.md section 9, item 12, left it for this prompt):
   `NumberSeriesGap`, one row per number series whose documents are not as many as its highest number, or whose highest
   number is not the one before its next. Its cached figure is the documents numbered, its ledger figure the numbers the
   series has given. One more branch of the constant statement of `ReconciliationQuery`, a file already sanctioned for
   raw SQL: no new one.
3. **Every database test ends with the reconciliation of every tenant it left** (`DatabaseTest.DisposeAsync`), so a
   financial test that leaves a cache out of line with the ledger, or a series with a gap, fails whether or not it
   checked itself, now and for every test written later. A test that leaves a defect on purpose names the identities it
   leaves with `ExpectDrift`, and a class that tests one part on its own names them once (`DriftLeftOnPurpose`): the
   ledger through its port without documents (`ReceivablesOnSqlServerTests`), the number series without documents
   (`NumberSeriesOnSqlServerTests`, and `IdempotencyApiTests`, whose probe takes a number for a probe record). No other
   drift is ignored. The first run with the check found exactly the 41 tests that seed defects or take numbers without
   documents, and none that leaves drift by mistake.
4. **docs/api.md is complete** and stays so: `ApiDocumentationTests` fails for an endpoint whose method and path are not
   there, and for an error code (of a Domain `Errors` class or the API's `ErrorCodes`) not in its catalogue, section 9.
5. **US-43 for a bill:** a Viewer reads one bill's trail through its whole life, and each record's "before" equals the
   previous record's "after" (compared by value), so no state change of the bill goes unrecorded.

**Found (not changed):** the "after" image of a record that creates a row holds its decimals as the application wrote
them (`1`, `200`), while every later image, read from the stored row, holds them at the column's scale (`1.000`,
`200.0000`). The values are equal; a reader comparing the JSON as text sees a difference. `EntitySnapshot` could read
the creation's image back after the insert, at the cost of a read per created row; left as it is for the owner to decide.

## Amended 2026-11-26 (Prompt 11.1): one search box on the lists of documents

The lists of bills, payments, credit notes, adjustments and refunds had filters (payer, status, dates) and no search:
a cashier with a name or the last digits of a receipt had to go through the patient or the payer first. Decided with
the owner on 2026-11-26:

1. **One parameter, `search`, on the five lists**, for four things: a part of the number (and of the old number of a
   document brought forward), a part of the patient's name or MRN, a part of the payer's name, and an amount, exactly,
   when the text reads as one. Any of them finds the document. The text is data, never a pattern (ADR 0018,
   `LikePattern`); at most 100 characters; fewer than two filter nothing (`DocumentSearch`, Application, Common).
2. **A list reads the parties of another module through a contract**, as the reports do: `IPartySearch` (Application,
   Payers) gives the patients and the payers a text names as queries, which the list of documents puts into its own.
   The modules of the documents still know no table of the parties.
3. **Each part of the search is a query of its own, joined by the ids** (`UNION`), not one `WHERE` with `OR`. Measured
   on LocalDB 2019 with 100,000 payments, 100,000 bills and 5,000 patients of one hospital (2026-11-26): with `OR`,
   SQL Server read every row for every search, 750 to 1,500 ms; joined, each part is answered by its own index.
4. **A search that finds at most 500 documents reads them by their ids** (`DocumentSearchFilter.NarrowAsync`). The page
   of a search that finds few documents took over a second when SQL Server walked the list in its order looking at
   each document for a match; given the ids it takes milliseconds. A search that finds more is dense enough to be found
   in the order of the list.
5. **What it costs, as measured through the API** (the same data; the best of three, the whole request):

   | Search | Payments | Bills |
   | --- | --- | --- |
   | none (the list as it was) | 253 ms | 109 ms |
   | a part of a number, one document found | 66 ms | 74 ms |
   | a patient's name, 20 documents found | 62 ms | 72 ms |
   | an amount, about 40 found | 64 ms | 73 ms |
   | nothing found | 61 ms | 69 ms |
   | two characters of a number, 5,009 found | 206 ms | 218 ms |
   | a first name, 12,500 found | 350 ms | 239 ms |
   | the prefix of every number, 100,001 found | 383 ms | 217 ms |

   A part of a number cannot seek (the text may be anywhere in it): it scans the hospital's numbers in the narrow
   unique index of the number, about 90 ms of the above for 100,000. A name scans the hospital's patients and payers,
   not its documents. An amount seeks.
6. **Indexes** (migration `0028_DocumentSearch`): one per table for the amount, `IX_Bills_TenantId_Total`,
   `IX_CreditNotes_TenantId_Total`, `IX_Payments_TenantId_Amount`, `IX_Adjustments_TenantId_Amount`,
   `IX_Refunds_TenantId_Amount`. The number, the payer and the patient of each table had their indexes. **No
   full-text index:** it is a feature of SQL Server that is installed apart, and the numbers above do not ask for it.
7. **Not measured:** credit notes, adjustments and refunds at volume (they take the same shape and are fewer than
   bills in a hospital); SQL Server 2022; more than one hospital in the database.

Tests: `DocumentSearchApiTests` (each field in part on each list; `%`, `_` and `[` as text; with the other filters and
the paging; 501 documents found; another hospital's documents never), `DocumentSearchTests` (the text and the
amount). The browser's search box is Prompt 11.1b.

**Built 2026-11-26, Prompt 11.1b: the box on the five lists.** `DocumentSearchField` (frontend, components) is the
box of every search (the lens, the spinner in its place, Clear; ADR 0039 and ADR 0040 as amended) with the label
"Number, patient, payer or amount", first among the filters of the lists of bills, payments, credit notes, adjustments
and refunds. It is sent while it is typed, after the pause and from two characters, at once on Enter and when the box
is left; the address holds it as `search`, beside the other filters, and a search replaces the entry of the address so
that Back leaves the list. Another filter keeps the search and goes to the first page. No screen was added and no
package. Tests: `e2e/docsearch.spec.ts` (each of the five lists; one character not sent; Clear; Back; the address
opening a list with its search; Tab; both themes; a phone; axe).
