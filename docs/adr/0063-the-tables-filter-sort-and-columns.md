# ADR 0063: The tables: filters, orders and columns, the server doing the work

- **Status:** Accepted. On 2026-11-27 the owner gave Prompt 12.2 with its library decided (TanStack Table, headless)
  and approved the plan "as recommended", with its six points: a business date from the server (there was no 12.1);
  TanStack Table 9 with its three packages pinned; the seventeen tables of the plan; no fold under 720 px; three
  commits on the branch `phase-12-tables`; the maker filter only where users may be read. This record is 12.2a, the
  API; 12.2b (the shared table and the document lists) and 12.2c (the rest of the screens) will amend it.
- **Date:** 2026-11-27
- **Deciders:** project owner, Claude Code
- **Amends:** ADR 0036 (the reports filter and order on request, and their totals follow), ADR 0018 (a list's order
  may be asked for), ADR 0048 (with 12.2b: the table component).

## Context

Every list answered in one fixed order with a few filters. On a long table a user could not narrow the rows by an
amount, a date range on every document, who made it or where it came from, nor sort by the column they care about.
The prompt requires the server to filter, sort and page over every row, the browser never to filter or sort the page
it holds, and an order never to be a column name passed through to SQL.

## Decision

1. **An order is a named column of the list.** `sort=total` or `sort=-total`: the name is compared, without regard to
   case, with the list's own `Sorts` (in its query, Application) and becomes a `ListSort`; the query service maps each
   name to an expression written in code (`switch` on the name). Anything else is 400 `sort_not_supported`, whose
   message lists the columns. The name never reaches the database.
2. **Every order ends on a key that is one of a kind:** the document's id, or for a number its fiscal year, sequence
   number and id, in the direction asked. Two pages never share or skip a row (tested across four pages of equal
   totals and dates).
3. **The new filters are read by the API in its own words.** They are bound as text and read by `FilterReader`: an
   amount from 0 with at most two decimals, a date `yyyy-MM-dd`, the id of a user, the name of an enum (never its
   number). One that cannot be read, or a range whose end is before its start, is 400 `filter_invalid`, with each such
   parameter in `errors`. A filter left empty filters nothing. The filters a list had before keep their binding and
   their refusals.
4. **Every filter combines** with the others, with the search of 11.1, with the order and with the paging; the count
   and the totals of a report are of the rows they leave. Tenant isolation is the context's filter, as everywhere.
5. **A list with no new parameter answers exactly as before**: same filters, same order, same envelope.
6. **The business date comes from the server.** `GET /api/v1/system/business-date` answers today in India by the
   server's clock (BR-003), never cached; the screens count a date filter's shortcuts from it, never from the
   browser's clock (there was no Prompt 12.1 to give it).
7. **Indexes where an order or a filter needs one**, in migration `0034_ListIndexes`: the balance due of bills, the
   unapplied amount of payments, and the date of credit notes, adjustments and refunds. Amounts already had theirs
   (11.1). The maker and the origin ride on the date index; measured, they need none of their own.
8. **The cost, measured** on 100,000 bills of one hospital on LocalDB 2019 (`ListFiltersApiTests`), after a warm-up
   call: the list with no filter 160 ms; every order by total, date or balance 15 to 17 ms; by number 102 ms; an
   amount range 3 to 47 ms; a month of dates 4 ms; the maker 21 ms; the search 104 ms; page 2,500 114 ms; the slowest,
   `balanceDueFrom=1` (most rows match) 179 ms. The test holds every one under 1.5 seconds.

## What the screens will do (12.2b and 12.2c)

- TanStack Table (`@tanstack/react-table` 9.3.1, MIT; with `@tanstack/table-core`, `@tanstack/react-store` and
  `@tanstack/store`, the last two pinned to 0.11.2) inside one shared table; `manualFiltering`, `manualSorting` and
  `manualPagination`, no row model: it holds the columns' visibility and order only.
- Filters, order and page in the address, replaced, back to page 1 on a change; the columns shown and their order in
  this browser, per hospital, user and table, with "Reset columns". The first column (number or name) and the status
  are never hidden.
- The CSV keeps every column in today's order and bytes; print follows the columns shown.

## Consequences

- Every list can be narrowed and ordered on the server, at a cost measured, without any SQL built from text.
- Each list declares its orders in two places that must agree: the names in its query and the `switch` of its query
  service. A name in one and not the other is a test's failure (`InvalidOperationException` on an unknown name).
- The payment register's `order` stays for the dashboard and applies only when no `sort` is given.
- **Not measured:** the other lists on 100,000 rows (their indexes are the same shape as the bills'), and SQL Server
  2022 (runbook section 8, row 12.2a).

## Alternatives considered

- **A column name passed through** (`ORDER BY {sort}`) or dynamic LINQ by name: refused by the prompt, and an
  injection or a slow order waiting to happen.
- **Filtering and sorting the page in the browser:** wrong on any list longer than a page, and the totals would not
  agree with what is shown.
- **Binding the new filters as typed parameters:** a malformed one would be the framework's 400, the same for every
  parameter and without our code; reading them as text gives `filter_invalid` with each parameter named.

## Built note (2026-11-27, Prompt 12.2a)

- `ListSort` and `ListSortRules` (Application, Common), `FilterReader` (Api, Common), `SortedQueries` (Infrastructure),
  the two codes in `CommonErrors`. No package.
- Tested on LocalDB 2019 in a throwaway copy: `ListFiltersApiTests` (each filter alone and together, each order both
  ways, the tie-breaker across pages, the refusals, tenant isolation, a register's totals against the ledger, the day
  turning at midnight in India, the master lists, the 100,000 bills).
