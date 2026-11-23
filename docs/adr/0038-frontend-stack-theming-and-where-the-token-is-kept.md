# ADR 0038: The frontend: its stack, its theming, and where the browser keeps the token

- **Status:** Accepted. The owner approved the proposal of Prompt 7.1 on 2026-11-23 "as recommended", with its four
  answers (points 9 to 12 below). The built note at the end records what the build found.
- **Date:** 2026-11-23
- **Deciders:** project owner, Claude Code
- **Answers:** `docs/security.md` section 10, row S5 (where the token is kept); the chart library of Prompt 7.6.

## Context

R1 needs a browser frontend for five roles in a hospital: sign-in, documents, the cashier's work, approvals, reports and
a dashboard (Prompts 7.1 to 7.6). The API is complete: every endpoint under `/api/v1`, a bearer token from
`POST /api/v1/auth/login` valid for 60 minutes and never in a cookie (ADR 0031, ADR 0035), error answers as
ProblemDetails with a stable `errorCode` (ADR 0011), and an OpenAPI document at `/openapi/v1.json` in Development. The
frontend talks to it alone. Prompt 7.1 asks for the stack with a reason for each choice, theming with design tokens, the
chart library, and where the browser keeps the token. The working agreement asks for approval of every npm package.

## Decision

1. **React 19, TypeScript, Vite**, in `frontend/`, built to static files. **TypeScript 5.9**, not 6 or 7: the
   type-aware lint (typescript-eslint 8.72, `typescript < 6.1`) and the OpenAPI type generator (openapi-typescript 7.13,
   `typescript ^5`) accept no later version yet.
2. **React Router 8** in data mode (`createBrowserRouter`, `RouterProvider` from `react-router/dom`), with no framework
   mode and no server rendering: the router most developers know; version 8 is version 7 with its v8 flags switched on,
   ESM only, and the `react-router-dom` package removed.
3. **TanStack Query** for every read and change: caching, retries, the refresh of a list after a change, and one place
   for loading and error states, which every screen from 7.2 on needs.
4. **A typed API client generated from the API's own OpenAPI document**: `openapi-typescript` turns the document into
   types and `openapi-fetch` is the small client that uses them. The document is committed as
   `frontend/src/api/openapi.json` with the generated `schema.d.ts` beside it. `OpenApiSnapshotTests` (.NET, no
   database) fails when the API's document differs from the committed one, and rewrites it when the environment variable
   `BILLINGRECON_WRITE_OPENAPI_SNAPSHOT` is `1`; the frontend's CI checks that the generated types match the committed
   document. So a change of the API shows up in both builds until the frontend has caught up.
5. **React Hook Form, without a schema library.** The API validates every field and answers its messages per field
   (ADR 0018); the form shows them on their fields. The browser checks only that a required field is filled.
6. **Our own components, with CSS Modules and the design tokens, on plain HTML elements; no UI kit.** A kit brings its
   own colour system, which would fight the rule of point 7. A headless library for a dialog or a menu is added by the
   prompt that needs one, with approval.
7. **Theming.** Design tokens are CSS variables in one file, `src/theme/tokens.css`: `--surface`, `--surface-raised`,
   `--surface-sunken`, `--text`, `--text-muted`, `--border`, `--accent`, `--on-accent`, `--success`, `--warning`,
   `--danger`, `--focus`, and a fixed chart palette `--series-1` to `--series-6`; a light set and a dark set, chosen by
   `data-theme` on `<html>`. **No component holds a raw colour value**: a test scans every style and script file but
   `tokens.css` and fails with the file and line of one. **Text contrast meets WCAG AA in both themes**: a test computes
   it from `tokens.css` (4.5:1 for text, 3:1 for borders and the focus ring against their surfaces). The user chooses
   **System, Light or Dark** in the top bar; System follows `prefers-color-scheme` and reacts when it changes; the choice
   is kept in `localStorage` (`billingrecon.theme`) and applied by an **inline script in `index.html`** before React
   mounts, so a hard refresh does not flash. A test runs that script; another pins its SHA-256, which the content
   security policy of Prompt 8.1 lists (`script-src 'self' 'sha256-...'`).
8. **The chart library is Recharts** (installed by Prompt 7.6): it draws SVG, so a series is drawn with
   `stroke="var(--series-1)"` and follows the theme without a re-render, where a canvas library (Chart.js, ECharts) must
   read the computed colours and redraw on every change of theme. Its charts carry a text alternative of our own.
9. **The token is kept in `sessionStorage`** (row S5). A refresh keeps the user signed in; closing the tab signs them
   out, which matters on a shared computer on a ward; it never outlives its 60 minutes. Against a script injected into
   the page, memory, `sessionStorage` and `localStorage` protect about equally, since such a script can call the API
   through the app wherever the token is; the defences are React's escaping, a lint rule against raw HTML
   (`dangerouslySetInnerHTML`), the content security policy of 8.1 and the token's short life. The app forgets the token
   at its expiry and on any 401, and shows the sign-in again with the page the user was on. The hospital's code is kept
   in `localStorage`; it is not a secret.
10. **ESLint 9** with the React, hooks and accessibility (`jsx-a11y`) rules, and Prettier with a `--check` gate like
    `dotnet format`. ESLint 10 waits for `jsx-a11y` to support it.
11. **The branch stays `phase-2-billing`**, as since Phase 3.
12. **The snapshot is refreshed by the .NET test** of point 4, not by a script against a running API.

Also: Vitest with jsdom and React Testing Library for the tests, the API faked by passing a fake `fetch` to the client
(no mocking package); in development Vite forwards `/api` to the API, so the app calls its own origin, as it will behind
the proxy of Prompt 8.1; each request carries an `X-Correlation-Id` of its own; the navigation shows a role the sections
of the permission matrix (`docs/requirements.md` section 1) it may open, from a copy of the matrix a test holds against a
table of its own; versions are pinned exactly, resolved as of two weeks before the install, with the lock file
committed, `npm ci` in CI, `npm audit` as a gate, and Dependabot for npm.

## Alternatives

- **TanStack Router** (typed routes and search parameters): fewer people know it, and its own concepts would be one more
  thing to learn for a modest gain.
- **A UI kit** (MUI, Mantine, Chakra): fast to start, but its theme system and its colours would sit beside the tokens.
  **Tailwind**: colour classes in markup make the no-raw-colour rule hard to hold.
- **Zod with a resolver** for forms: two more packages to repeat what the API checks.
- **MSW** for API mocks: a fake `fetch` given to the client does the same here.
- **`localStorage` for the token**: survives the browser's restart, on shared computers. **Memory only**: every refresh
  signs the user out, for little gain against an injected script. **An httpOnly cookie**: the API is bearer-only by
  design (ADR 0035, CORS without credentials) and would need a cookie scheme and CSRF protection.
- **ESLint 10 without the accessibility rules**: newer, but the prompt asks for WCAG AA and the rules catch the common
  mistakes early.
- **Fetching the OpenAPI document from a running API**: needs the API running, with a database, to build the frontend.
- **An external theme script** (`<script src>` in the head) instead of an inline one: it needs no hash in the content
  security policy, but the prompt asks for an inline script, and a pinned hash keeps it honest.

## Consequences

- The office machine builds the frontend (Node 24.22.2 since 2026-11-23); `engines` asks for Node 22.22 or later
  (React Router 8's minimum). CI uses Node 24.
- A change of an API endpoint fails `OpenApiSnapshotTests` until the snapshot is rewritten and the types regenerated
  (`docs/runbook.md`).
- Amounts arrive as JSON numbers; the frontend displays the server's figures and does no money arithmetic before
  Prompt 7.3, which decides how its live totals are computed.
- The navigation's copy of the matrix must follow `docs/requirements.md`; the server's policies remain the authority,
  and a hidden link is a convenience, not a control.
- The inline theme script's hash goes into the content security policy of 8.1; changing the script changes the hash.

## Built (2026-11-23)

Built as decided, on the office machine (Node 24.22.2, npm 11.19.1). What the build found:

- **The OpenAPI document described enums as numbers.** The built-in OpenAPI generator reads its schemas from the JSON
  options of minimal APIs (`ConfigureHttpJsonOptions`), not from the controllers' (`AddJsonOptions`), so all 27 enums
  came out as `integer`. `Program.cs` now gives both the same `JsonStringEnumConverter`; the wire did not change, only
  the document, and `OpenApiSnapshotTests` checks that `PaymentMethod` is listed by name. Left as it is, for the
  owner: the document gives amounts and counts as `number | string`, because the API's JSON defaults
  (`JsonSerializerDefaults.Web`) read a number written as a string; the API always writes numbers. Making the API strict
  about numbers is a change of the API, not of the frontend.
- **The snapshot is the shipped API's.** The test hosts add test-only controllers; `OpenApiSnapshotTests` hosts the API in
  Development without them, and drops the document's `servers` (the test host's address) before comparing.
- **One advisory at install:** a denial of service in `minimatch` (GHSA-g3hv-75wg-m249, moderate), reached only through
  typescript-eslint 8.72.1, a lint tool. typescript-eslint 8.73.0 fixes it and accepts TypeScript 5.9; it was taken,
  a week old, as a NuGet audit fix is (runbook section 5). The runtime packages had none. Every other version was
  resolved as of 2026-11-09 (`npm install --before`).
- **The token's roles, from a real sign-in,** are `role` as a string for one role and an array for more; the session
  keeps the roles the app knows and drops any other.
- **Raw HTML and evaluated code:** the one `eslint-disable` of the frontend is in `src/test/themeScript.test.ts`, where
  the test runs the text of the inline script with `new Function`, as the browser runs it; it names its reason.
- **The inline script's SHA-256** is `EiUJJFQJQCBNqB/aazghpxtqLJ7StNqlkar3PJPRG+g=`, the same in `index.html` and in the
  built `dist/index.html` (Vite leaves a classic inline script as it is). Prompt 8.1's policy lists
  `'sha256-EiUJJFQJQCBNqB/aazghpxtqLJ7StNqlkar3PJPRG+g='`.
- **In the browser** (the API in Development against a throwaway LocalDB database, the Vite dev server in front): a
  Supervisor and a Cashier signed in and saw their own sections, a Cashier was refused the dashboard by its address, a
  refused sign-in showed the API's message with its reference, Light, Dark and System applied without a reload, and a
  hard reload kept Dark while the system was light. The browser pane's emulation of the system's colour scheme changes
  `matchMedia` without firing its `change` event (a listener of the test's own got none), so the live switch under
  System is proven by the component test, not in the pane. A single frame cannot be seen in a screenshot: that the
  script runs before the first paint rests on the script test and on its place in the built page, before the app's
  script and its stylesheet.
- **No source maps in the production build** (Vite's default): they would publish the whole source to every browser.
