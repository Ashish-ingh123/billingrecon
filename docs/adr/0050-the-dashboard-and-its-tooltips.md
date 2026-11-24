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
