# ADR 0047: The brand, the design tokens and the sign-in page

- **Status:** Accepted. The owner approved the plan of Prompt 9.1 on 2026-11-24 "as recommended" (points 1 to 7); the
  built note records what the build found and what is still owed.
- **Date:** 2026-11-24
- **Deciders:** project owner, Claude Code
- **Builds on:** ADR 0038 (the frontend: tokens, the raw-colour rule, the pinned theme script), ADR 0045 (nginx and its
  headers), ADR 0046 (the browser suite). First ADR of Phase 9 (`docs/build-prompts.md`, "The bar of Phase 9").

## Context

R1 works and looks plain. The owner gave a reference for the sign-in page, `docs/ui/sign-in.png` (1672 x 941), asked for
every screen at that level, and for the company, ashline, to be named in the product. Prompt 9.1 lays the ground: the
brand, one set of tokens for both themes, the typeface, the icons, the motion, and the sign-in page itself. Nothing may
change what a screen does, load anything from another origin, or loosen nginx's content security policy.

## Decision

1. **The page is laid out in the reference's own pixels.** `--u` is one pixel of the 1672 x 941 frame at the size of the
   window (the smaller of width / 1672 and height / 941, never under 0.62 px), and every measure of the page is the
   number measured on the reference times `--u`. At 1672 x 941 the page is the reference; elsewhere it scales, wide or
   tall. Under 900 px the three lines and the photograph go, the ground becomes one blue from top to bottom (white text
   must keep its ground), and the card stays. Measured: the card 864 to 1552 across and 84 to 875 down (41.1% of the
   width); the fields 923 to 1498, 56 high, at 392, 513 and 631; the button at 708, 57 high; the mark 123 square at
   (91, 152).
2. **Tokens.** `tokens.css` gains, in a light and a dark set: the brand's blues, a placeholder colour, a soft border for
   hairlines that carry no meaning, the pressed accent, the sign-in page's ground, tint, mask, waves and curve, shadows,
   and one scale each for radii, spacing, type sizes, durations and easings. The surfaces, text and accent moved to the
   reference's blues; the chart series kept their colours. Every pair in use is held by the contrast test (95 cases).
   A token's name may not contain a CSS colour word (`mark`, `violet`): the colour scan reads it as one.
3. **Field borders are darker than the reference's** (`#6b7a99`, 4.3:1 on the card, against the reference's `#c6d1e3`,
   about 1.5:1): the edge of a field is how it is found, so it meets 3:1 (WCAG 1.4.11). The owner chose this over
   matching the pale border.
4. **The typeface is Inter**, self-hosted through `@fontsource-variable/inter` 5.3.0 (OFL-1.1, no dependency): its
   files are bundled and served from our origin (`font-src 'self'`). One variable file of 48 kB serves Latin text; the
   Latin Extended file (85 kB), which has the rupee sign, is fetched when a page shows one. A fallback face, Arial with
   Inter's metrics (`Inter Fallback`), stands in until it arrives, so nothing moves.
5. **Icons are our own inline SVG** on a 24 px grid, drawn with `currentColor` (`src/brand/Icon.tsx`); no package. The
   BillingRecon mark (a cross with rounded arms and a darker cross inside) is drawn from tokens (`BrandMark`), and is the
   favicon as a file with the light colours.
6. **The company.** ashline's mark is the file `src/assets/brand/ashline-logo.svg`, copied path for path from the
   company's site and used as it is; its name is set beside it as on the site ("ash" at medium weight, "line" at regular
   weight in the company's violet), on a pale plate that is the same in both themes, because the file is drawn for a
   light ground. The sign-in page says "A product of ashline", bottom left. It and the theme control, top right, are the
   two additions to the reference.
7. **Motion, from tokens, and only `transform` and `opacity`.** The card rises as it fades in; the three lines follow;
   the pulse line draws itself once (a wipe made of two opposite translations, not a stroke animation); the waves
   drift, the one thing that loops; a field's ring is there from the start and shown by opacity; the button moves a
   pixel under the pointer and the press and shows a spinner while the request runs; a refusal eases in. With
   `prefers-reduced-motion: reduce` no animation and no transition runs.
8. **Nothing in `index.html`'s script or nginx's headers changes.** The favicon is a `<link>`; `theme-color` is set by the
   theme provider from the token `--theme-color`, because `index.html` may hold no raw colour and its script is pinned.
   "Contact support" is a `mailto:` from the build's configuration (`VITE_SUPPORT_EMAIL`, a build argument of the
   frontend image from `SUPPORT_EMAIL` in `.env`); without a valid address the divider and the link are left out.
9. **What sign-in does is unchanged:** the same request, refusals through `ProblemAlert`, the two notices of an ended
   session, the remembered hospital code, the return address, the labels and the autocomplete attributes. New: a
   show/hide button beside the password, named "Show password" with `aria-pressed`.

## Alternatives

- **Matching the reference's pale field border.** Offered to the owner; not chosen (point 3).
- **An icon package or a motion library.** Twelve icons and CSS keyframes did not need one.
- **The typeface's files copied into the repository by hand.** The package gives the same files with a version, a lock
  entry and the audit.
- **`stroke-dashoffset` for the pulse line.** The usual way, but it animates neither transform nor opacity.

## Consequences

- Every screen already wears the new palette, typeface, buttons and fields; their layouts are Prompts 9.2 to 9.4.
- The browser journey of 8.2 fills the field labelled "Password" by its exact name now, and its exception for the
  browser's `/favicon.ico` request is gone.
- The sign-in browser tests need the built app (`vite preview` or nginx): the dev server's own inline scripts are
  rightly refused by the policy the tests apply.
- 30 npm packages.

## Built (2026-11-24)

- **Without the photograph.** `frontend/src/assets/brand/signin-hospital.webp` is not in the repository: the owner has
  no photograph apart from the reference itself. The page is built to take it (the left 68% under the tint, masked,
  asked for only on a wide window, fading in once decoded) and stands without it on the ground and the tint. Until the
  file is added the built page differs from the reference by the missing building.
- **Measured against the reference** in the installed Edge at 1672 x 941, in both themes: the card, the three fields,
  the button and the mark within 4 px (the e-mail field sits 2 px high). Screenshots: `docs/ui/sign-in-built.png`,
  `sign-in-built-dark.png`, `sign-in-built-phone.png`.
- **The content security policy** of `frontend/deploy/nginx.conf`, read from that file and put on the page's response by
  the browser test: nothing refused, the bundled typeface included. Not seen through nginx itself (runbook section 8,
  row 9.1).
- **Tests:** 28 new component tests (the page, the show/hide button, the pending button, the support link present and
  absent, the build's address, the ground and its photograph, 18 contrast cases); 6 browser tests (the layout in both
  themes under the policy with no console error and no layout shift, reduced motion, the keyboard's order, 390 and
  360 px). The 357 existing component tests pass unchanged, and the three steps of the journey pass through the new
  page.
- **Found:** `Shell.test.tsx`, "shows the hospital, the user and their roles", failed twice in six full runs and never
  alone: it waits one second for the dashboard, which loads on demand. Not changed; `docs/backlog.md` B35.
