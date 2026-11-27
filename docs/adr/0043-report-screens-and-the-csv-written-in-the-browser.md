# ADR 0043: The report screens, and the CSV written in the browser and held to the server's totals

- **Status:** Accepted. The owner approved the plan of Prompt 7.5b on 2026-11-23 "as recommended" (points 1 to 3 below).
  The built note at the end records what the build found.
- **Date:** 2026-11-23
- **Deciders:** project owner, Claude Code
- **Builds on:** ADR 0036 (reports), ADR 0039 (screens), ADR 0041 (the payer statement), ADR 0042 (the inbox).

## Context

Prompt 7.5b builds the six reports of BR-901 with date filters and a CSV export of every row of the filter, with its
totals and period. The API answers each paged report a page of at most 100 rows at a time, with the totals of every page
and the period it covers (`docs/api.md` section 8.15); it has no CSV of its own.

## Decision

1. **The CSV is written in the browser from every page and held to the server's totals.** The export reads every page
   of the filter, 100 rows a request, one after another; the rows must be as many as the report counts and must come to
   its totals to the paisa, and the totals must not change between the first and the last page; otherwise it reads once
   more, and refuses with a message if the report changed again. At most 10,000 rows (100 requests); above that it asks
   for a narrower filter. The file is UTF-8 with a byte order mark and CRLF lines (RFC 4180): the title, the period, the
   rows under their headings, then the totals. Amounts are plain decimals with two places from the server's figure,
   dates `yyyy-MM-dd`; a field with a comma, a quote or a line break is quoted; a text field that starts with `=`, `+`,
   `-`, `@`, a tab or a carriage return is prefixed with `'` (CSV injection). One definition of a report's columns serves
   the screen and the CSV (`src/reports/columns.tsx`).
2. **The six reports are eight screens**, every role's (the policy `Reports`): the bill register; the payment register
   and the daily collection; outstanding by payer, each payer linked to the statement of 7.4; unapplied cash and
   available credit, each row linked to its payment or credit note; pending approvals, read-only, with a link to the
   inbox for a checker; the reconciliation, "No drift" or an alert with the rows by identity.
3. **`/reports` is an index**; each screen has its own address and keeps its filters there; a register without dates
   shows the period the API answered, the current month in India; the totals sit in the table's footer as the server
   answered them, never added up on the screen.

## Alternatives

- **A CSV answered by the API** (`format=csv`, point 1): one consistent read and no cap, at the cost of server code, tests,
  documentation and OpenAPI for nine endpoints.

## Consequences

- An export of thousands of rows is as many requests as pages; the check against the totals catches a report that changed
  meanwhile, but cannot stop it changing.
- The dashboard of 7.6 links its "view all" to these screens.

## Built (2026-11-23)

Built as decided, with no package and no change of the API. What the build found:

- **A day's net collection can be below zero** (a refund on a day without receipts): the CSV writes signed amounts.
- **The reconciliation's totals carry the moment of the check** (`checkedAtUtc`), which differs on every page: its export
  compares the rows by identity instead.
- **Lint wants component files to export components only**: the columns, totals and checks are in `columns.tsx`, the
  helpers in `view.ts`.
- **`usePayer` takes `enabled`**, for a filter with no payer chosen.
- **The bundle grew to 616 kB** minified (176 kB gzip).
- **Tests:** 15 new: the CSV (exact and signed amounts, quoting, the formula guard, the layout with its BOM and CRLF);
  the export (every page, the cap, a second reading, the refusal); the screens (the index, a register's page, period and
  totals, its filters in the address, an export of 120 rows over two pages with its file's bytes, the refusal of rows that
  do not come to the totals, the daily collection with a negative net, the reconciliation clean and with drift, the
  payer's statement link). With only the first page exported, amounts written with grouping, the formula guard dropped
  and the totals check skipped, one at a time, each is caught.
- **In the browser**, against the API in Development on a throwaway LocalDB database with a posted bill of 590.00, a
  payment of 600.00 of which 400.00 allocated, and a refund of 50.00: the bill register showed the bill owing 190.00 and
  exported it with the payer `=Kumar, Ravi` written as `"'=Kumar, Ravi"`; the daily collection showed 600.00 received,
  50.00 refunded, 550.00 net; outstanding by payer 190.00 owed less 150.00 unapplied, 40.00 net; the reconciliation no
  drift. The export was read in the page, not saved to disk.

## Amended 2026-11-27 (Prompt 12.2): the CSV follows the filters and the order

ADR 0063 adds filters and a `sort` to the reports (ADR 0036, amended). The CSV written in the browser is read with the
same filters and order as the screen, every page, and **keeps every column in today's order and bytes, whatever is
hidden on screen**; its totals are still the server's, of the filtered rows. Print follows the columns shown. Built in
12.2c, with a browser test that the CSV equals the filtered and sorted rows.
