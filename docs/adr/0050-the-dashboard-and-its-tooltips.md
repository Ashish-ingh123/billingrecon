# ADR 0050: The dashboard and its tooltips

- **Status:** Accepted. The owner approved the plan of Prompt 9.4 on 2026-11-24 with its recommended split: 9.4 is the
  dashboard with the owner's tooltip specification (added to the prompt the same day); 9.4b, the reports, print, the
  finishing pass, the budgets of the build and the tag v1.1.0, amends this ADR.
- **Date:** 2026-11-24
- **Deciders:** project owner, Claude Code
- **Builds on:** ADR 0044 (the dashboard's layout reference and its panels), ADR 0047 (tokens, icons, motion), ADR 0048
  and ADR 0049 (the shared components).

## Context

The dashboard of 7.6 followed the layout of `docs/ui/dashboard.png` in R1's style: its own icons, a drop-down for the
period, Recharts' default tooltip on the line chart only, a native `title` for a card's exact amount, inline `style`
attributes for the cards' colour, the donut's legend dots and the top payers' bars, and a loading skeleton animated by
its background. The owner asked for tooltips on every figure and chart, one component, opened and closed by pointer,
keyboard and tap, with nothing shown only in a tooltip.

## Decision

1. **One tooltip, from the tokens.** `TipBox` is the content of every tooltip: a title, the figures exact (`₹` and Indian
   grouping, tabular figures, as the server answered them) and a note, on `--surface-popover` with a `--border-strong`
   edge (two new tokens, held by the contrast test against every surface) and `--shadow-raised`. It fades in by opacity
   with `--duration-fast`, and at once with reduced motion. Recharts' tooltips render it as their `content`.
2. **A figure's tooltip**, `Tip`: the figure is a button, so the tooltip opens on hover (a mouse), on keyboard focus and on
   a tap (the pointer's release), and closes when the mouse leaves, on blur, on Escape and on a second tap; it describes
   the button (`aria-describedby`). It opens under the figure, kept to the side of the card nearer the middle of the
   screen, so it never covers the pointer and stays inside the window. A card's figure is named by its exact amount; its
   tooltip gives this period, the previous period's figure and the change with its arrow and its word.
3. **The charts.** The line chart and the sparklines use Recharts' own keyboard layer (the arrow keys go point to point
   once the chart has the focus) and `useChartTip`, which adds Escape, blur and the second tap. The line chart shows a
   vertical crosshair, a dot on each line, and the date, billed and collected to date, and that day's or week's billed
   and collected. A sparkline's tooltip gives the business date and that day's figure from the trend's daily series.
4. **The donut is drawn here** (SVG paths, no Recharts): pointing at a slice, tapping it, or focusing the donut (a slider
   over the buckets) and using the arrow keys picks a bucket; its slice thickens, the others dim, the centre shows its
   exact amount and name, and a tooltip under the donut gives the bucket, the amount, the share and the bills as the API
   answers them.
5. **The top payers' bars** are native `<meter>` elements (no inline style), each the button of a tooltip with the payer's
   owed, unapplied cash, available credit and net, all in the top payers' answer: no request is sent for a tooltip.
6. **Nothing only in a tooltip.** Every chart has "Show the figures as a table": the line chart's was there; the
   sparklines' days are one table under the cards; the donut's legend is its table; the top payers have a table of their
   four figures.
7. **The page.** The period is a segmented control (a radio group named "Period": the last 7, 30 or 90 days, or custom
   dates), the cards take their icons from the shared set and their colour from a class, the panels and cards follow
   the tokens of 9.1 to 9.3, the skeleton pulses by opacity, and no element carries an inline style. Nothing moves after
   the first paint: the cards' placeholders are the cards' height, the period's line keeps the height of its dates, the
   daily table's line is there from the start, the two chart panels keep their loaded height, and a page header's title
   takes the row first so that its actions never jump when a subtitle arrives (a change of `PageHeader` for every
   screen, with no visible difference on the others).

## Alternatives

- **Recharts' Pie for the donut.** Its active slice and keyboard handling are not under our control in 3.10.1; the SVG of
  four arcs is short and does exactly what the owner asked.
- **Floating positioning by script.** Not needed: each tooltip is placed by CSS on the side of its figure that faces the
  middle of the screen; no inline style, no package.

## Consequences

- Two dashboard tests changed their way in, not what they check: the card's figure is found as the button of its
  tooltip (named by its exact amount) instead of by its `title`, and the period is chosen by its radio button instead of
  a select option.
- The shared browser helper (`e2e/stubbed.ts`) now says what moved, by how much and when, when a layout shift fails a test.

## Built (2026-11-24)

- **Verified on the office machine, installed Edge:** 448 component tests (26 new: the tooltips, the daily table and
  the contrast of the tooltip's two tokens), twice; lint, format, types, audit and the build clean. In the browser, `e2e/dashboard.spec.ts` (7 tests) under
  nginx's policy: the dashboard in both themes at 1440 and 390 px with nothing refused, no console error, no request the
  test did not expect, no sideways scroll and no layout shift (three runs in a row); the crosshair by the arrow keys with a
  dot on each line and Escape; the tooltips of a card, a sparkline, the donut and a bar inside the window and off the
  pointer; nothing animated with reduced motion. With the other suites, 60 browser tests. The three steps of the 8.2
  journey passed against the API on LocalDB 2019. Screenshots: `docs/ui/dashboard-built*.png` (the reference,
  `docs/ui/dashboard.png`, is unchanged).
- **Found and fixed while building:** the page was 40 px too wide (the period control could not shrink, the ageing table
  was outside a scrolling frame, a screen-reader copy of an amount sat past the right-hand cards); four sources of layout
  shift (above).
- **Seen again (backlog B35):** LocalDB answered the dashboard's summary and drift queries with "a severe error occurred"
  during the journey; the page read them again and the step passed.
- **Not verified:** through nginx on the personal machine (runbook section 8, row 9.4).

## Amended 2026-11-24 (Prompt 9.4b): the reports, print, the finishing pass and the budgets

Approved by the owner with `@axe-core/playwright` 4.14.0 (MPL-2.0, a development package of the browser suite; its
`axe-core` 4.14.0 was already installed through the lint's accessibility plugin).

1. **Routes on demand** (backlog B21 closed). Every page of `routes.tsx` is a React Router `lazy` route behind its
   policy, and so is the shell: a first visit to the sign-in page reads none of the screens. **Budgets**, compressed,
   held by `npm run budgets` (`frontend/scripts/budgets.test.mjs`, Node's own test runner, run in CI after the build):
   the first visit's JavaScript at most 145 kB (138 kB built; 186 kB before), its CSS at most 15 kB (5.5 kB), any route's
   chunk at most 20 kB (the largest 4 kB), the dashboard's at most 130 kB (114 kB). The plan had proposed 130 kB for the
   first visit: React DOM, React Router, TanStack Query and React Hook Form, which the sign-in page uses, make about
   135 kB, so the budget was set just above what the split reached.
2. **The reports.** The index is cards in their groups (an icon, the name as the link, a line; the card takes the
   click). A report's filters and export sit on one bar; its table scrolls inside a frame no taller than the window, with
   its header stuck to the top and its totals row to the bottom, so both stay in view. Export CSV says which page it is
   reading ("Reading page 3 of 12…") through a callback of `readWholeReport`, and how many rows it wrote at the end; the
   CSV's bytes and checks are unchanged.
3. **Print on A4.** `@media print`: the tokens of the light theme on white whatever the theme, no shadow; no sidebar,
   top bar, button, filter, tooltip, meter or closed table; links as text; a row never cut in two and the table's head
   repeated on each page; at the top, printed only, the hospital, "BillingRecon" and the day in India.
4. **The finishing pass**, `e2e/routes.spec.ts`: the 48 routes of the shell in both themes at 1440 and 390 px, each with
   nothing refused by nginx's policy, no console error, no request the test did not expect, no layout shift and one h1;
   at 1440 px, axe's WCAG 2.2 A and AA rules with no serious or critical finding, and no sideways scroll at 1,920, 720
   (200% zoom of a 1440 px screen) and 360 px. Every route's screenshot is kept in the run's `test-results`, not in git.
   `e2e/print.spec.ts`: a bill, a statement and a report printed to A4 PDF in both themes; forced colours and
   prefers-contrast; the keyboard's first stop leading to the content.
5. **What the pass found and fixed:** the ageing table's frame could not be reached by the keyboard (now a named region
   that takes the focus; the lint's `no-noninteractive-tabindex` allows `region` for it); a drop-down was as wide as its
   longest option and widened the page at 360 px (a field's control now keeps to the field's width); the typeface was
   not preloaded, as the bar of Phase 9 asks, so text reflowed when Inter replaced its fallback (the build now adds a
   preload of its Latin face to index.html); a report's filters wrapped differently once the typeface arrived on a
   phone (each now takes its own row there); the line of a report's answered period appeared with the data (it now
   stands from the first paint).

**Built and verified (office machine, installed Edge):** 449 component tests, twice; the budgets; lint, format,
types, audit and the build clean; 261 browser tests against the built app (the routes suite, print, and the suites of
9.1 to 9.4); the three steps of the 8.2 journey against the API on LocalDB 2019. Screenshots: `docs/ui/reports*.png`,
`docs/ui/report-bills*.png` and the printed bill `docs/ui/bill-printed.png`.
**Not verified:** through nginx on the personal machine (runbook section 8, row 9.4b); the tag `v1.1.0` is the owner's.
