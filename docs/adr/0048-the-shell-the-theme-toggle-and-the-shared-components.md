# ADR 0048: The shell, the theme toggle and the shared components

- **Status:** Accepted. The owner approved the plan of Prompt 9.2 on 2026-11-24 with two answers: tables scroll inside
  their own frame on a narrow window, and the theme control becomes a modern toggle, a pill of three (asked for by the
  owner during the plan). The built note records what was built, what was left for 9.3, and what is owed.
- **Date:** 2026-11-24
- **Deciders:** project owner, Claude Code
- **Builds on:** ADR 0047 (the tokens, the typeface, the icons, the motion), ADR 0038 (the shell, the theme), ADR 0039
  (lists and records).

## Context

9.1 gave the sign-in page the reference's look and the whole app the new palette, typeface, buttons and fields. The shell
was still R1's: a plain top bar, a list of words for navigation, a drop-down for the theme. Prompt 9.2 carries the brand
into the shell, restyles the shared components every screen is made of, and proves the pattern on patients, payers and
service items, with no change of what any screen does.

## Decision

1. **The sidebar** holds the BillingRecon mark and name, the sections of the permission matrix each with its own icon
   (`Section.icon`), and at its foot "BillingRecon by" with the ashline logo and name. The current section is marked by a
   bar at its edge and bold text, not by colour alone.
2. **The rail.** "Collapse the navigation" (a toggle button with `aria-pressed`) shrinks the sidebar to icons with
   tooltips; the names stay for assistive technology. The choice is kept in `localStorage` (`billingrecon.navigation`).
3. **The drawer.** Under 900 px the sidebar is a drawer behind "Menu" (`aria-expanded`, `aria-controls`): it takes the
   focus, Tab goes round inside it, Escape and the backdrop close it and the focus returns to "Menu", a link closes it.
4. **The top bar**: the hospital, the user's initial, address and roles, the theme pill, and "Sign out", a visible button
   that keeps its name.
5. **The theme toggle** replaces the drop-down: a pill with a sun, a moon and a monitor, and a thumb that slides to the
   choice. Underneath it is a radio group named "Theme" with Light, Dark and System, so the keyboard and screen readers
   work as they do for any radio group. The setting, its storage and the inline script are unchanged (ADR 0038 point 7);
   the slide is driven by a data attribute, not an inline style.
6. **A page change**: the tab's title names the section ("Bills · BillingRecon"); after a navigation, not on the first
   page, the page's heading takes the focus; the page fades in, the shell stays still. The View Transitions API was not
   needed for that.
7. **Shared components from the tokens**: tables (a soft frame, a quiet header, row hover, links in bold, amounts right-
   aligned in tabular figures, the first column pinned while the table scrolls inside its frame), list controls, the page
   header ("Back to …" with an arrow), badges (a word, an icon and a colour: one table of tone to icon,
   `components/tones.ts`), details, the error alert, the conflict notice, the record picker, fields (disabled and read-only
   marked by a sunken ground and a dashed edge). Loading is a skeleton of lines, `aria-busy`, with "Loading …" said once
   and not shown. An empty list shows a small drawing of our own (`EmptyState`).
8. **A specimen page**, `/design`, in Development builds only (left out of the production bundle): every shared
   component in its states. It is not in the navigation.

## Alternatives

- **Rows that fold into cards on a phone.** Offered; not chosen (the table's markup, its tests and screen readers' table
  navigation stay as they are).
- **A two-way theme switch.** Offered with the pill; it would have lost "System".
- **A single mapping of every document status to its tone, icon and word.** Each module keeps its labels (`billStatusLabels`
  and the like) and chooses a tone; the icon comes from the tone. The full mapping comes with the money screens of 9.3.

## Consequences

- The tests of the theme now choose by radio button, not by select: the same three settings, the same assertions on the
  page's theme and on what is stored (a changed expectation, not a weaker one). The sign-in page's keyboard test stops on
  the chosen theme.
- The back link of a page reads "Back to …": its text no longer repeats the page's own words exactly, which a refund test
  relies on.
- Backlog B35 is closed: the shell test that waited the default second for the dashboard, which loads on demand, now
  waits five; it expects the same.

## Built (2026-11-24)

- **Verified on the office machine, installed Edge, built app:** 392 component tests (7 new), twice in a row; 16 browser
  tests: the patients list in both themes at 1440 and 390 px under nginx's policy applied in the test, with nothing
  refused, no console error, no sideways scroll and no layout shift; the drawer by keyboard; nothing animated with reduced
  motion; `/design` absent from the production build; the sign-in tests; and the three steps of the 8.2 journey through
  the new shell, against the API on LocalDB. Lint, format, types, audit and the build are clean. Screenshots:
  `docs/ui/shell-desktop.png`, `shell-desktop-dark.png`, `shell-phone.png`, `shell-phone-dark.png`.
- **Left for 9.3, where the money screens need them:** fields with a prefix or suffix (₹, %) and a required mark; the
  audit trail drawn as a timeline; the restyle of the no-access, not-found and placeholder pages; the one mapping of every
  document status. A header that stays in view inside a table that scrolls sideways was not done: the frame that scrolls
  is what a sticky header would stick to.
- **Not verified:** the shell through nginx on the personal machine (runbook section 8, row 9.2).
