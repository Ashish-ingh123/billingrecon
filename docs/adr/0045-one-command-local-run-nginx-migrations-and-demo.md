# ADR 0045: The one-command local run: nginx with TLS in front of the API, the migrations as the migrator, and a demo entered through the API

- **Status:** Accepted. The owner approved the plan of Prompt 8.1 on 2026-11-23 "as recommended" (points 1 to 4 below).
  The built note records what the build found. First run with Docker on the owner's personal machine on 2026-11-24, with
  two fixes to the images (built note, "Run with Docker"); runbook section 8, row 8.1 keeps what is still owed.
  **Amended by ADR 0052 (2026-11-24):** the API of the one-command run runs in the environment `Demo` (`API_ENVIRONMENT`),
  and `Demo:Enabled` is refused in every environment but `Demo` and Development.
- **Date:** 2026-11-23
- **Deciders:** project owner, Claude Code
- **Builds on:** ADR 0017 (the API image), ADR 0034 (logins and grants), ADR 0035 (TLS at the proxy), ADR 0038 (the
  frontend and its inline script).

## Context

Prompt 8.1 asks for `docker compose up` on a clean machine to give a working demo that can be signed in to: the API and
the frontend in compose, nginx ending TLS and serving the app as the API's known proxy, a migration step as the migrator
login, an opt-in demo seeder (one hospital, five users, patients, payers, a catalogue, a week of bills and payments), and
the frontend's own headers. No API creates a hospital or its first user; outside Development the API never migrates,
refuses plain HTTP, and believes forwarded headers from known proxies only.

## Decision

1. **The demo is entered through the API.** With `Demo:Enabled` (`DEMO_ENABLED=true` in `.env`), the API makes sure at
   startup that the hospital `demo` and its administrator exist (`DemoTenantSeeder`, fixed ids, the password from
   `Demo:AdminPassword`, refused at startup when shorter than 12 characters); nothing else. The `demo-seed` service then
   signs in and enters four more users, the catalogue, patients, payers and a week of bills and payments through the API,
   so the numbers, the ledger and the audit trail are the API's own. It does nothing when the demo is off or already
   seeded.
2. **TLS with a local certificate made by nginx** on its first start (self-signed for `localhost` and `frontend`, kept in
   a volume); the browser warns once. Port 80 only redirects to HTTPS. nginx has a fixed address on a fixed network,
   which is the API's one `ReverseProxy:KnownProxies` entry.
3. **The migrations as in runbook section 10**, by a `migrate` service built from the SQL Server image: as the
   administrator, the database (with `READ_COMMITTED_SNAPSHOT`) and the two logins with the passwords of `.env`; as
   `billingrecon_migrator`, the idempotent script of every migration, written at build time by the repository's
   `dotnet-ef`; as the administrator again, the application's login into its role. Every step can run again. The API
   starts only when it has ended well.
4. **The branch is `phase-8-delivery`**, cut from `phase-2-billing`, since `main` has none of Phases 2 to 7.

The frontend's pages and files carry their own headers from nginx: a content security policy whose `script-src` lists
the inline theme script by its SHA-256, `frame-ancestors 'none'` and `X-Frame-Options: DENY`, `nosniff`, HSTS, no
referrer.

## Alternatives

- **A seeder written in C# inside the API** (point 1): no script, but a second way of writing bills and payments beside
  the use cases.
- **mkcert on the host** (point 2): a trusted certificate, at the price of a tool to install first.
- **An EF Core migrations bundle** (point 3, the plan's first wording): the same migrations, but not the procedure the
  runbook already gives for every other environment.

## Consequences

- The compose file needs three more secrets in `.env` (the two login passwords and the signing key) and, for the demo,
  two settings.
- The demo hospital exists only where `DEMO_ENABLED=true`; a real deployment never sets it.

## Built (2026-11-23)

- **The migration step is the runbook's idempotent script, not a bundle**, so that the compose file and runbook section
  10 do the same thing.
- **`sqlcmd` runs with `QUOTED_IDENTIFIER` off unless given `-I`**, and the migrations' filtered indexes are then refused
  (Msg 1934). Found by running the steps on LocalDB; `setup.sh` and runbook section 10 step 2 now pass `-I`.
- **`AllowedHosts` is `localhost;frontend`**: the demo seed calls nginx by its compose name, with the local certificate
  as its extra CA.
- **Verified here:** the three SQL steps twice on LocalDB 2019 (the second time changes nothing; as `billingrecon_app`,
  member of its role, not `db_owner`, snapshot isolation on, 20 migrations, `/health/ready` 200); the demo seed against the
  API in Development with the demo on (14 bills, 10 payments, one refund waiting, no drift, the dashboard equal to the
  bill register), and a second run that does nothing; `DemoTenantSeederTests` and `DemoStartupValidationTests`. **Not
  verified:** nginx, the images and `docker compose up` itself (runbook section 8, row 8.1).

## Run with Docker (2026-11-24)

The owner ran `docker compose up --build` with the demo on the personal machine and reported: the three images built,
`migrate` and `demo-seed` ran to their end, `sql`, `seq`, `api` and `frontend` were up, and https://localhost signed in as
`supervisor@demo.local` and showed the dashboard. Two fixes were made there and are their own commit:

- **The `migrate` image restores the Infrastructure project before `dotnet ef` writes the script**, which reads the
  project's restore output first.
- **The frontend image builds on `node:24-bookworm-slim`, not `node:24-alpine`**: on Alpine (musl) the native bindings of
  Vite 8 and Rolldown failed. nginx stays on Alpine. The healthcheck names `127.0.0.1`, because `localhost` can resolve to
  `::1` and nginx listens on IPv4 only.

Not checked in that run: the page's headers and the redirect of http://localhost (runbook section 8, row 8.1).
