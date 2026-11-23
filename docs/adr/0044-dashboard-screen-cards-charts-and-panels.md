# ADR 0044: The dashboard screen: cards by colour token, Recharts loaded with its route, and panels that fail alone

- **Status:** Accepted. The owner approved the plan of Prompt 7.6 on 2026-11-23 "as recommended", with the layout
  reference taken from the prototype and Recharts 3.10.1. The built note at the end records what the build found.
- **Date:** 2026-11-23
- **Deciders:** project owner, Claude Code
- **Builds on:** ADR 0037 (the dashboard's API), ADR 0038 (frontend stack, Recharts, tokens), ADR 0042 (the inbox), ADR
  0043 (the reports).

## Context

Prompt 7.6 builds the dashboard of US-44 after the layout reference `docs/ui/dashboard.png`: a period picker, six summary
cards with sparklines, billing against collections, the ageing donut and four panels with "View all", every figure the
server's, each panel loading and failing on its own, in both themes, for the roles the permission matrix allows.

## Decision

1. **The layout reference** is the owner's prototype screenshot, copied into the repository as `docs/ui/dashboard.png`,
   with its dark and phone views beside it. Icons are drawn inline; no icon package.
2. **One card component, one colour token per card** (`--series-1` to `--series-6`): the background is the token mixed
   into the card's surface with `color-mix` (8% in light, 20% in dark), the icon tile the solid token, the sparkline a
   2px line over a fill fading to transparent, flush to the card's bottom edge. The change is coloured by meaning: a rise
   in billing or collections is good, a rise in outstanding dues, unapplied cash, approvals or issues is bad; no previous
   figure says so, no change is neutral. The drift card says it has no trend.
3. **Money on a card is compact** (₹99,999, ₹12.8L, ₹1.3Cr, exact arithmetic on paise, half away from zero), with the
   exact amount in its tooltip and for screen readers; tables show exact amounts with Indian grouping. The client computes
   nothing else but the proportional bars of the top payers.
4. **Each panel has its own skeleton, empty state and error** (the API's ProblemDetails); the approvals panel follows the
   inbox's rule (no Review on one's own request); each "View all" opens the report or the inbox it summarises.
5. **Recharts 3.10.1, pinned** (3.11.0 was six days old), loaded with the dashboard's route only, so the main bundle does
   not carry the chart library.

## Consequences

- The dashboard is the landing page of a TenantAdmin, a Supervisor and a Viewer; a BillingClerk and a Cashier have no
  access (as the API).

## Built (2026-11-23)

Built as decided. What the build found:

- **Recharts brings 38 packages with it** (Redux Toolkit, Immer and others); `npm audit` stays clean.
- **The bundle** reached 1,034 kB with Recharts in it; with the dashboard loaded with its route, the main bundle is
  619 kB and the dashboard's chunk 417 kB.
- **The shell's test that read the dashboard's placeholder** now reads the placeholder of "Users and settings", the one
  section no prompt builds (a changed expectation).
- **Tests:** 12 new: the compact money (99,999, 1,00,000, 12,80,000, 1,00,00,000 and the roundings at the edges), the
  colour of a change by meaning, a card with its exact figure and with no previous figure or no change, the page from
  every panel's answer, the review of another's request only, one panel failing and one empty while the rest show, the
  period in the address, every colour of the page a token in both themes, and no access for a clerk or a cashier. With a
  change coloured by direction, crores shown as lakhs, a Review on one's own request and a raw colour on a card, one at a
  time, each is caught.
- **In the browser**, against the API in Development on a throwaway LocalDB database, for a Supervisor: total billing
  ₹12,50,590.00, outstanding dues ₹12,50,190.00 and the top payer's ₹12,50,040.00, the same figures as the bill register
  and outstanding by payer of 6.1; collected ₹600.00 gross of a ₹50.00 refund (BR-902); unapplied ₹150.00; one approval
  waiting with its Review; no drift; in the light and the dark theme. A clerk's refusal is shown by the test, not seen in
  the browser.
