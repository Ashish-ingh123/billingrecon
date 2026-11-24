# ADR 0046: The load test, the timing of the number series, and the browser journey

- **Status:** Accepted. The owner approved the plan of Prompt 8.2 on 2026-11-24 "as recommended" (points 1 to 6), then
  chose the load model of point 2 and the smaller run of the built note after the first full run missed (the same day).
- **Date:** 2026-11-24
- **Deciders:** project owner, Claude Code
- **Builds on:** ADR 0006 (idempotency: the store holds the transaction), ADR 0008 (the number series and its lock),
  ADR 0022 (the order of a posting), ADR 0035 (the proxy and the loopback), ADR 0038 (the frontend's tooling), ADR 0045
  (the one-command local run and its demo hospital).

## Context

`docs/requirements.md` section 5: posting a bill or recording a payment, p95 under 500 ms at 50 concurrent users, with
10,000 bills per tenant per month, on one SQL Server; number allocation serialises per (tenant, type, fiscal year) and
must stay under 20 ms inside the transaction. Prompt 8.2 asks for a load test that reports p95 and the number-allocation
time, a fix for what misses, a small Playwright suite of the main journey, and zero drift after the load.

A load test sees the API from outside; how long a posting holds its series is only known inside it. The office machine
has no Docker and, it turned out, about 1 GB of free disk.

## Decision

1. **The load test is a Node script with no package** (`deploy/load/load.mjs`, with `stats.mjs` for the arithmetic and
   `stats.test.mjs` run by Node's own test runner in CI), like the demo seed. It runs as the compose service `load`
   behind the profile `load` (through nginx, the real stack), or with `node` against any API. Rejected: k6 (a new tool on
   two machines for what the script does); NBomber (its licence needs payment for use in an organisation).
2. **The workload:** one hospital (the demo, ADR 0045), 50 users each with BillingClerk and Cashier and a patient and
   payer of their own (no two users contend for a payer's balances), each in a loop: draft a bill, post it, record a
   payment of its total, allocate it. First the hospital is filled to 10,000 posted bills in the month, with no pause,
   timed and not judged. Then **two minutes with a pause of one second between one user's requests are judged**: still
   far quicker than any clerk, and what "50 concurrent users" means. Then a minute with **no pause at all is reported as
   a stress figure, not judged**: every user then queues on the one series of bills (built note). Then the
   reconciliation must find no drift. The verdict: p95 of posting and of recording under 500 ms, the p95 hold of each
   series under 20 ms, no error while judged, no drift row; the exit code says it.
3. **"Number allocation under 20 ms inside the transaction" is the hold:** from the moment a posting has its number to
   the commit or rollback that frees the series, the part that serialises. The **wait** (the locked statement, so the
   time behind other postings) is reported, not judged.
4. **The API tells its timings in `Server-Timing`, off by default.** `NumberSeriesTimer` (Infrastructure, scoped;
   `Stopwatch` durations, never instants, so `IClock` stays the one source of instants) is marked by the allocator when
   the locked statement answers, and by whoever ends the transaction (the unit of work, or the idempotency store while it
   holds it). `ServerTimingMiddleware` writes `number-wait;dur=…, number-hold;dur=…` when the response starts, only
   when `Diagnostics:ServerTiming` is true (`DiagnosticsOptions`, bound and checked at startup) and only on an answer to
   a request that took a number. The Application port `IRequestTimings` keeps the API off Infrastructure types. Compose
   passes `SERVER_TIMING` (false by default). Threat model row I14.
5. **The browser journey is Playwright** (`@playwright/test` 1.65.1, pinned: three packages in the lock file), one
   serial suite in `frontend/e2e` with its own `tsconfig.e2e.json`, against a running stack (by default the
   one-command run at https://localhost, its local certificate accepted), with Chromium or an installed Edge or Chrome
   (`E2E_BROWSER_CHANNEL`). Every step fails on an error a page writes to the console, so a script the content security
   policy refuses fails it; the browser's own request for `/favicon.ico` is the one exception (the app has no icon).
   Vitest is kept to `src/`.
6. **Neither suite runs in CI.** Shared runners say nothing about latency, and the journey needs SQL Server, the API
   and nginx; CI has not run once yet. Only the load test's arithmetic is in CI.

## Alternatives

- **No pause as the judged load** (the first reading of the plan). With no pause, 50 users keep the series of bills busy
  all the time, and a closed loop then queues every user on it: throughput is one over the mean hold, and the time
  waited grows with the number of users. Meeting 500 ms that way needs each posting to hold the series about half as
  long, which means moving the payer's balance lock before the number in every posting use case (the lock order of ADR
  0022), and even then this laptop may not reach it. The two targets of the requirement agree with the pause: a hold
  under 20 ms with p95 under 500 ms allows about 25 postings of one series to overlap, not 50. Not chosen by the owner.
- **A log event per posting** instead of a header, read from the API's log after the run: works only where the log can
  be read, which the compose service cannot.
- **A load harness hosting the API in-process** (WebApplicationFactory on Kestrel with test decorators of the timer): no
  production code, but not the stack that ships, and a second hosting of the API's database setup.

## Consequences

- The runbook's section 14 has the commands for the owner's machine; row 8.2 of section 8 is what is still owed.
- `Server-Timing` is production code that is off unless switched on; turning it on in a deployment would tell a
  signed-in user how busy their hospital's series is (I14).
- The load leaves its users, patients and bills in the demo hospital; `docker compose down -v` gives a fresh demo.
- The suites rest on the demo's users, catalogue and password, and on the screens' labels.

## Built (2026-11-24)

- **First full run, office machine** (LocalDB 2019, the published API outside Development on port 5090, reached over
  loopback with `X-Forwarded-Proto: https`, Server-Timing on, 50 users, no pause): the fill phase, 7,844 bills in 77 s
  with no error, gave **posting a bill p95 1,130 ms** (recording a payment 49.6 ms); the bill series' wait p95 was 1,084
  ms, its hold p50 4.8 ms, p95 17.2 ms, max 3.9 s; 102 bills a second, so the series was saturated. Then **the disk of
  the office machine filled up** a minute into the measured phase (the database had grown to 980 MB); 45,921 requests
  answered 500 ("Could not allocate space"), every one rolled back whole: **the reconciliation found no drift**. The
  database was dropped; that run's measured phase is void.
- **The owner's decisions** after it: judge with a pause of one second, report the no-pause run as a stress figure
  (point 2), make the small fix below, and run smaller here (the fill to 2,000 bills); only the owner's machine proves
  the full 10,000.
- **The fix inside the lock:** the idempotency store read its key's row in `CompleteAsync`, after the use case had
  taken its number, so one round trip of every posting fell inside the hold. It now reads the row into the request's
  context at the reservation, before the transaction opens (`IdempotencyStore`, ADR 0006 unchanged otherwise), and
  `CompleteAsync` updates the tracked row. A new test asserts the row is tracked from the reservation. Its effect is
  within the noise of this laptop (the no-pause hold p50 4.8 ms before, 4.7 ms after).
- **The smaller run, office machine** (same set-up, a new database): fill to 2,049 bills (not judged); **judged, 50
  users with a one-second pause, 122 s, 1,450 bills: posting p95 107.6 ms, recording p95 102.9 ms, the bill series
  held p95 7.9 ms (max 103.7 ms), the payment series p95 8.9 ms, no error**; stress, no pause, 24 s, 2,789 bills: posting
  p95 417 ms, p99 6.9 s, hold p95 11.1 ms; **the reconciliation found no drift. Every target was met.** The multi-second
  maxima appear in every operation at the same moments, as LocalDB grows a new, small database; nothing in the API
  waits that long.
- **Room:** about 50 kB of data per bill, 166 of the 305 MB in `AuditRecords` (the before and after images of every
  change) and 86 MB in `IdempotencyKeys` (their stored answers), plus the transaction log. At 10,000 bills a month that
  is about 0.5 GB a month per hospital before the log; the audit images are kept for good (CLAUDE.md section 9, item 14),
  the keys for 30 days (ADR 0006).
- **The browser journey, office machine:** against the Vite dev server and the published API in Development on a new
  LocalDB database with the demo seeded, in the installed Edge: three steps passed in 10 s. The first try failed on the
  browser's 404 for `/favicon.ico`, which led to the one exception of point 5. With "Apply oldest first" renamed in the
  app, the cashier's step failed waiting for it.
- **Not verified here:** SQL Server 2022, the stack through nginx and the compose service `load`, the full 10,000-bill
  fill, the journey against nginx with its content security policy, and a fresh Chromium (runbook section 8, row 8.2).
