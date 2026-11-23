# ADR 0037: The dashboard: figures from the ledger by business date, no history of drift, and a sixth raw-SQL file

- **Status:** Accepted. The owner approved the proposal of Prompt 6.3 on 2026-11-23 "as recommended": the additions to
  `docs/requirements.md` (US-44, BR-902, the matrix's "Dashboard" row) and the design, points 1 to 8 below. The built
  note at the end records what the build found.
- **Date:** 2026-11-23
- **Deciders:** project owner, Claude Code
- **Amends:** ADR 0021 (a sixth file may send SQL of its own; ADR 0015 and ADR 0021 asked for an ADR for it), ADR 0036
  (the payment register can be ordered newest first).
- **Implements:** US-44 and BR-902 (new R1 scope, added with this prompt); BR-003 (business dates in India), BR-404
  (every figure ties to the ledger).

## Context

Prompt 6.3 asks for a dashboard: six summary cards for a period, each against the previous period of the same length;
a trend of billed against collected, with the daily series each card needs for its sparkline; the ageing of what is
owed; and four short lists. The dashboard was not in R1's requirements, so the prompt asked for a user story and a row
of the permission matrix first, and for a decision about the one card that has no history, reconciliation drift.

Three of the cards are balances "as of" a day in the past: outstanding dues and unapplied cash at the end of a period's
last day, and the requests still waiting then. The cached balances of ADR 0005 are balances of now; the ledger is the
record of every movement with its business date, so a balance of any past day is the sum of the entries dated up to it.
The requests carry the instants they were asked for and decided. Drift rows are found by comparing caches with the
ledger now; nothing keeps what they were yesterday.

Entity Framework cannot sum or group an amount stored through a value converter (`Money` behind `MoneyConverter`), and
raw SQL is banned outside the files ADR 0015 and ADR 0021 sanction; the reports of 6.1 add their totals in memory
(ADR 0036, built note), which works for a register capped at 366 days but not for a balance, which sums every entry
since the tenant began.

## Decision

1. **New R1 scope, recorded in `docs/requirements.md`:** US-44 (a Supervisor, a TenantAdmin or a Viewer sees the
   dashboard of a period, compared with the previous one, with its acceptance criteria), BR-902 (the period, the
   comparison and the definition of every figure) and the matrix's row "Dashboard": TenantAdmin, Supervisor, Viewer.
   The dashboard is the overview of whoever runs or audits the hospital; a BillingClerk and a Cashier keep every report
   of 6.1 and 6.2 for their daily work.
2. **No trend for the drift card.** It shows the count of drift rows now, with `previous` and `changePercent` null. The
   alternative, a snapshot table written each day, would need a migration, a scheduled job (R1 has none), and a table
   of its own to keep tenant-isolated and audited, to draw a line that should be flat at zero: any drift is a defect of
   the highest severity, fixed when found (`docs/database.md` section 5.6), and a history would mostly show when it was
   found. Can be added later without changing the endpoint: `previous` would stop being null.
3. **Ledger sums by business date, in SQL, through a sixth sanctioned file.** The Receivables module gains a read-only
   contract, `ILedgerMovements` (Application): the balances of Receivable, UnappliedCash and CreditAvailable before a
   date, and per business date of a range the amount billed (debits to Receivable of `BillPosted` postings), collected
   (debits to Cash of `PaymentRecorded` postings) and the change of Receivable and UnappliedCash. Its implementation,
   `LedgerMovementsQuery` (Infrastructure.Receivables), sends two constant statements with the tenant as a parameter in
   the WHERE clause and in the join, the dates as parameters of type `date`, and reads only; `BannedSymbolsTests` pins
   the file, its two suppressions, the two constants and the tenant in both places. It lives in Receivables, the owner
   of the ledger and of the reconciliation's raw SQL, because Reporting may send no SQL of its own (ADR 0036 point 1,
   `ReportingConventionTests`); Reporting calls the contract like any module. Alternatives: summing every ledger entry
   in memory (reads the whole ledger of the tenant on every load, growing for ever); a daily snapshot of balances (a
   migration and a job; the R5 answer if the sum ever proves slow).
4. **What each figure is** (BR-902). A period is `from` to `to`, business dates in India, both included, both or
   neither, at most 366 days, ending today at the latest; neither is the last 30 days ending today. The previous period
   has the same length and ends the day before.
   - *Billed* and *collected*: the sums of the period's dates from point 3. Collected is **gross**: a reversal or a
     refund is a movement of its own day and does not lower the day the money came in (the daily collection of ADR 0036
     point 3 nets them).
   - *Outstanding dues* and *unapplied cash*: the balance before the window plus every change up to the end of the
     period's last day.
   - *Pending approvals*: the requests whose request instant falls on or before the day, in India, and whose decision,
     if any, falls after it.
   - *Reconciliation drift*: point 2.
   - *Ageing*: the balance due of every posted bill that owes something (`Posted`, `PartiallyPaid`), by the days from its
     due date to today in India: `0-30` (not yet due and due today included), `31-60`, `61-90`, `90+`. Its total is the
     outstanding dues of today; the tests hold it against the Receivable account.
5. **Seven GET endpoints under `/api/v1/dashboard`, one per panel**, so a slow or failing panel does not take the others
   with it: `summary`, `trend` (`interval` `Day` or `Week`; without it, per day up to 31 days and per week beyond, weeks
   from Monday), `ageing`, and four lists that call the handlers of the existing reports, never a copy of their queries:
   `top-payers` (outstanding by payer, page 1 of 5), `recent-payments` (the payment register of the period, newest first,
   page 1 of 5), `pending-approvals` (page 1 of 3) and `drift` (the reconciliation, page 1 of 3). Each list keeps its
   report's totals of every page.
6. **The payment register takes `order`**, `OldestFirst` (the default, as before) or `NewestFirst`, by date and then by
   number, so the recent payments are the register's own query.
7. **One new policy, `Dashboard`,** admitting TenantAdmin, Supervisor and Viewer; `PermissionMatrixApiTests` walks the
   seven endpoints with every role.
8. **Percentages are rounded, amounts are not.** A change and a share are rounded to two places, half away from zero
   (rule 1 of CLAUDE.md); a change from zero is null; the four shares may add up to 99.99 or 100.01. Amounts leave the
   API as exact decimals, sums of amounts already rounded at their documents; formatting in lakh and crore is the
   frontend's (7.6).

## Consequences

- No migration and no package. The ledger sums filter on the columns of `IX_LedgerPostings_TenantId_BusinessDate`;
  the ageing and the approvals read what the reports of 6.1 read. No query plan has been looked at yet.
- The summary runs the reconciliation (its drift count), as the drift panel and the report of 6.2 do: three reads of the
  same statement when a page loads all three. Accepted for R1; a cached result would need an invalidation rule.
- The balances before a window sum every entry of the tenant dated before it: one row comes back, the work grows with
  the ledger. At the volumes of the NFR (10,000 bills a month per tenant) that is some hundreds of thousands of rows a
  year, summed in the database; R5 can add a snapshot of balances if a measurement asks for it.
- Six files may suppress RS0030 now. A seventh needs an ADR, as before.
- The frontend's dashboard (7.6) reads these endpoints and formats the amounts.

## Built (2026-11-23)

Built as decided. What the build found, none of it a change of a decision:

- **Why SQL, shown:** a probe in a throwaway copy summed `EF.Property<decimal>` over a `Money` column and failed with "No
  coercion operator is defined between types 'Money' and 'Nullable<decimal>'"; grouping fails the same way.
- **The series are built without adding to a collection.** `ReportingConventionTests` refuses `.Add(` in any Reporting
  file (it cannot tell a list from an entity set); the daily series fills an array by index and the trend's points come
  from an iterator. The test was not loosened.
- **Pending approvals by day** read the requests whose instants may matter, with generous bounds in UTC (from the
  midnight before the window to two midnights after it), and test each request by its dates in India in memory, so a
  request asked for at 23:59 in India counts on that day.
- **A posting's business date** is its document's: the bill date for a bill, the payment date for a payment, the day of
  the approval for a reversal. So "billed in the period" is "bills dated in the period", as BR-902 says.
- **Tests:** `DashboardHandlerTests` (Application, 14: the period, the previous period, percentages, the IST boundary of
  a pending request, the summary, the trend per day and per week, the ageing buckets, the recent payments' query),
  `DashboardApiTests` (database, 7: a month of one hospital with the clock walked through it, every card against ledger
  sums in SQL, the ageing total against the Receivable account, a bill due today in `0-30`, payments either side of
  midnight in India, the trend's daily series, another tenant, an empty tenant, refused periods), seven rows of
  `PermissionMatrixApiTests`, the register newest first and an unknown order refused (`ReportsApiTests`), and the pins
  of the new file in `BannedSymbolsTests`.
