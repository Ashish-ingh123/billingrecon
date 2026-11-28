# ADR 0064: Free hosting of the demo: Vercel, Render and Azure SQL

- **Status:** Accepted. On 2026-11-28 the owner asked to put BillingRecon online for free, starting with Vercel, and
  approved the plan of three steps with its three decisions "as recommended": the Demo environment; the visitors
  sharing the address the sign-in limits see; the cold starts accepted, without a retry of the database connection.
  Written with step 3; steps 1 (`frontend/vercel.json`) and 2 (the Azure mode of the migrator) were committed before it.
- **Date:** 2026-11-28
- **Deciders:** project owner, Claude Code
- **Amends:** ADR 0035 (for this hosting only: two proxies in front of the API), ADR 0045 (the migration step also runs
  against Azure SQL Database).

## Context

The owner wants the demo hospital reachable on the internet, as a portfolio piece, at no cost. The production design
(ADR 0055) is one Linux server running `docker-compose.prod.yml`: SQL Server 2022, Seq, the API, nginx with the app,
certbot, the backups and the watchdog. That needs a paid server.

Vercel, which the owner named, serves static files and runs Node functions. It runs no .NET process, no SQL Server and
no Docker container, so it can serve the app (static files since ADR 0038) and nothing else. The free services that run
the rest have these limits, as of 2026-11-28:

- **Render**, free web service: runs a Docker image built from a GitHub repository, ends TLS at its own proxy and sends
  the request on over plain HTTP, sleeps after about 15 minutes without a request (the next one waits 30 to 60 seconds),
  512 MB of memory.
- **Azure SQL Database**, free offer: real SQL Server (the migrations, the snapshot isolation, the row versions and the
  grants of ADR 0034 work as they are), 32 GB and 100,000 vCore-seconds a month, pauses when idle. It has no `sa`, and
  from the application's database there is no `CREATE DATABASE`, no `CREATE LOGIN` and no `USE`, which
  `deploy/migrate/logins.sql` needs.
- The free virtual machines that could run the whole compose file are either ARM (the SQL Server image is x64 only) or
  have 1 GB of memory (SQL Server needs 2 GB).

Four parts of the code decide what the hosting has to set: the API answers HTTPS only outside Development and believes
`X-Forwarded-Proto` only from `ReverseProxy:KnownProxies` (ADR 0035); it refuses to start without `AllowedHosts`, or with
`*`; the app calls `/api` on its own origin (ADR 0038); and nginx adds the content security policy and the other
headers of `docs/security.md` section 10, which Vercel does not add by itself.

## Decision

We will host the demo, and only the demo, on three free services:

1. **The app on Vercel**, the project's root directory `frontend`. `frontend/vercel.json` does what nginx does: it passes
   `/api/*` and `/health/*` to the API, answers `index.html` for every other path that is not a file, keeps a missing file
   under `/assets/` a 404, and adds the headers of `frontend/deploy/nginx.conf` to the app's pages and to `/assets/`,
   none to the API's answers. `frontend/src/test/vercelConfig.test.ts` holds the two files to the same headers and
   routes. The app keeps calling its own origin: no CORS, no change to the app.
2. **The API on Render**, built from `src/BillingRecon.Api/Dockerfile` with the repository's root as the context, in the
   environment **`Demo`** (ADR 0045, ADR 0052): the demo hospital is made at startup and the tenant commands are refused.
   `AllowedHosts` names the Render host. `ReverseProxy:KnownProxies` lists the private ranges `10.0.0.0/8`,
   `172.16.0.0/12` and `192.168.0.0/16`: Render's proxy reaches the container from inside Render's network, whose
   address Render does not publish, and nothing outside that network reaches the container at all. Seq is left out
   (`Seq:ServerUrl` empty); the JSON console log is Render's log.
3. **The database on Azure SQL Database**, free offer, made in the portal with the name `BillingRecon`. The migration
   step runs from the owner's machine with the migration image and `AZURE_SQL=true` (step 2): `contained-users.sql`
   makes `billingrecon_migrator` and `billingrecon_app` users of the database with their own passwords, in the roles of
   ADR 0034; the server's administrator takes the place of `sa`; the server's certificate is checked.
4. **Secrets** for this hosting are new ones, made for it and kept in Render's environment and in the owner's vault: the
   signing key, the key of the second step, the two database passwords and the demo password. None of the local `.env`
   is reused.
5. **Two proxies, accepted for the demo.** Vercel's rewrite sits in front of Render's proxy, and the API reads one entry
   of `X-Forwarded-For` (ADR 0035, one proxy deep), so the address the sign-in limits see is Vercel's, not the
   visitor's: visitors share the limit of 30 sign-ins a minute for each address. The per-account limit still holds. This
   is accepted for the demo and for this hosting only; ADR 0035 is unchanged everywhere else.
6. **Cold starts, accepted.** The first request after Render has slept, or after the database has paused, waits, and the
   first database command after a pause may fail once; the next one succeeds. The connection gets no retry strategy.

## Alternatives

- **Everything on Vercel.** Not possible: no .NET runtime, no SQL Server.
- **The API as Vercel functions.** A rewrite of the API in Node; years of the project's rules (the ledger, the number
  series, the grants) live in the .NET code.
- **A free virtual machine with the production compose file.** The SQL Server image needs x64 and 2 GB of memory; the
  free machines are ARM or have 1 GB.
- **Azure App Service F1 for the API.** Free, but 60 CPU minutes a day; Render builds and runs the image the repository
  already has, with no change.
- **The browser calling Render directly, with CORS.** Every visitor's own address would reach the sign-in limits, but the
  app would need an API origin set at build time (`VITE_API_ORIGIN`), the content security policy a second
  `connect-src`, and the API `Cors:Origins`. More change for a demo; the owner chose the shared address.
- **A retry strategy on the database connection.** EF Core's retrying strategy refuses the transactions the API begins
  itself (the idempotency store, the number series) unless each is wrapped in the strategy; a change of its own, not of
  the hosting.
- **Server logins on Azure SQL, made in `master`.** They would need a second connection to `master` for every change
  of a password, and contained users are what Azure recommends for one database.
- **Trusting only Render's proxy address.** Render does not publish it, and it may change between deploys.

## Consequences

- The demo is online at no cost, deployed again by every push to `main` (Vercel and Render both build from GitHub). The
  database is migrated by hand, from the owner's machine, when a release brings a migration.
- **This hosting is not for a hospital's data.** It has no backups of ADR 0055 (Azure keeps its own restore points of 7
  days), no TDE under the hospital's certificate (Azure encrypts with its own key), no watchdog or alerts of ADR 0056, no
  mail, and its data sits wherever the owner picked the Azure and Render regions. A real hospital goes on the server of
  ADR 0055.
- `frontend/vercel.json` names the Render host; a different host is a change of that file (the test only holds the two
  rewrites to one HTTPS origin). A change of the theme script's hash, or of any header of `nginx.conf`, now fails the
  frontend tests until `vercel.json` follows.
- Trusting the private ranges believes `X-Forwarded-*` from any of the owner's own Render services in the same region;
  there are none besides this one.
- The free limits decide what can go wrong: Render's 512 MB (the API has not been measured under it), Azure's
  vCore-seconds a month (when used up the database pauses until the next month, if the portal's option "auto-pause" was
  kept, rather than billing), and the cold starts. Revisit when the demo is used for more than showing.
- Not proved before it ran: Azure SQL itself (step 2 was proved on LocalDB 2019 with a contained database), the
  certificate check from the migration image, Render's proxy range and the host Vercel's rewrite sends. Runbook
  section 21 says how to check each on the first deploy.
