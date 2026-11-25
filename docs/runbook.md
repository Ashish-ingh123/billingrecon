# Runbook

- **Status:** sections 1 and 2 written 2026-10-03 with the infrastructure step, section 3 with the
  DbContext step, section 4 with the test infrastructure and sections 5 and 6 with the CI step
  (2026-10-17); later sections with the prompts that needed them: section 11, the proxy in front of the
  API (Prompt 5.4b, 2026-11-23), section 12, the frontend (Prompt 7.1), section 13, the one-command run (Prompt 8.1),
  section 14, the load test and the browser journey (Prompt 8.2), and with the release (Prompt 8.3, 2026-11-24) rolling
  back a migration (section 3), backup and restore (section 15) and the incident procedures (section 16). Migrations
  outside Development are section 10. Complete for R1. Section 17, onboarding and suspending a hospital, with Prompt 10.2
  (ADR 0052).

---

## 1. Local infrastructure

| Piece | How it runs | Address |
|---|---|---|
| SQL Server 2022 Developer | `docker compose` service `sql` | `localhost,1433` |
| Seq (log server) | `docker compose` service `seq` | `http://localhost:5341` (UI and ingestion) |
| API | `dotnet run --project src/BillingRecon.Api` | `http://localhost:5080` |

Both containers publish their ports on 127.0.0.1 only and keep their data in named volumes
(`sql-data`, `seq-data`). Seq runs without a login because it is reachable from this machine only.

### Configuration

Sources, later ones win: `appsettings.json`, `appsettings.<Environment>.json`, user-secrets
(Development only), environment variables. Options are validated when the API starts; a missing or
malformed value stops it with a message that names the setting.

| Setting | Comes from | Notes |
|---|---|---|
| `Database:ConnectionString` | user-secrets, or the `Database__ConnectionString` environment variable | Required. A secret: never in a file in git. |
| `Seq:ServerUrl` | `appsettings.Development.json` (`http://localhost:5341`); empty elsewhere | Empty turns the Seq sink off. The JSON console log is always on. |
| `Seq:ApiKey` | user-secrets, or the `Seq__ApiKey` environment variable | Optional. A secret. Not needed for the local container. |
| `Database:ApplyMigrationsOnStartup` | `appsettings.Development.json` (`true`); off elsewhere | Applies pending migrations when the API starts. Allowed in Development only: in any other environment the API refuses to start with it on. |
| `Jwt:SigningKey` | `appsettings.Development.json` (a key for Development only); user-secrets, or the `Jwt__SigningKey` environment variable, elsewhere | Required. The key that signs and checks the tokens (ADR 0031): base64 of at least 32 random bytes. A secret outside Development: never in a file in git. The API refuses to start without one, with a short one, or, outside Development, with the key of `appsettings.Development.json`. Section 9 says how to make one and what changing it does. |
| `DevelopmentIdentity:TenantId`, `DevelopmentIdentity:UserId` | `appsettings.Development.json` | The development tenant and user: in Development a request without a token acts as this user (a TenantAdmin), and the API adds their rows when it migrates (ADR 0031). Not secrets. Outside Development they are not read: a request without a valid token gets 401. |
| `AllowedHosts` | the `AllowedHosts` environment variable, per environment | Required outside Development: the host names the API answers for, separated by semicolons (`api.hospital.example`, `*.hospital.example`, an IP address); `*` is refused there. Unset in Development, where any host is answered. Section 11 (ADR 0035). |
| `ReverseProxy:KnownProxies` | environment variables `ReverseProxy__KnownProxies__0`, `__1`, ... | The addresses or CIDR ranges of the proxy that ends TLS in front of the API, when it is not on the API's own machine; the loopback addresses are always trusted. Section 11. Never set `ASPNETCORE_FORWARDEDHEADERS_ENABLED`: the API refuses to start with it. |
| `Cors:Origins` | `appsettings.Development.json` (`http://localhost:5173`); environment variables `Cors__Origins__0`, ... elsewhere | The origins a browser may call the API from. None outside Development unless set; `https` only there. Not needed when the proxy serves the frontend and the API from one origin. Section 11. |
| `Serilog` section | `appsettings.json` | Log levels. The whole section is read, so configuration can also add sinks or enrichers; it cannot remove the console sink, the Seq sink or the PII mask, which are set in code, and anything it adds still receives masked events (ADR 0012). |

### Health endpoints

- `/health/live`: 200 as long as the process answers. No dependency is checked.
- `/health/ready`: 200 when the application database is reachable **and** every migration of this
  build has been applied to it; 503 otherwise (server down, database missing, or a migration not
  applied). The reason is in the log, not in the response.

### Time zone

Business dates are calendar dates in India. The API looks up the time zone `Asia/Kolkata` when it
starts and stops with a clear message if the machine does not have it. Windows and the standard
Linux images have it; a slimmed-down container image may not (see section 2, step 13).

### Stopping and resetting

- `docker compose stop` stops the containers and keeps the data.
- `docker compose down` removes the containers and keeps the data.
- `docker compose down -v` also deletes **both** volumes: the development database and the Seq history.
- `MSSQL_SA_PASSWORD` and the Seq first-run settings are applied only when a volume is first created.
  Changing them later needs the volume to be removed.
- **Before migration `0018_Users` reaches a development database that already holds data, drop that database**
  (`DROP DATABASE [BillingRecon]` in the container's SQL Server, or `docker compose down -v`). `0018` makes every
  column that names a user a foreign key to `Users`, and existing rows name users that have no row; there is no data
  step for that (ADR 0030, point 2). At its next start the API creates the database again, with the development tenant
  and the development user.

---

## 2. Verify on a Docker machine

`docker-compose.yml` was written on a machine without Docker. The owner first started the containers
on 2026-10-17; the results of this checklist (steps 3, 7, 8 and 10 in particular) are **still to be
recorded**. Do it on a machine with Docker Desktop, then record the result (step 12).

Needs: Docker Desktop running on an x86-64 machine (the SQL Server image has no ARM build), the .NET
SDK from `global.json`, and ports 1433 and 5341 free.

- **Shell.** Run the commands in Git Bash or PowerShell 7. In Windows PowerShell 5.1 type `curl.exe`
  instead of `curl`, because there `curl` is an alias for a different command.
- **Other ports.** If you change `MSSQL_HOST_PORT` in `.env`, use that port in the connection string
  of step 5 (`Server=localhost,<port>`). If you change `SEQ_HOST_PORT`, open Seq on that port in
  step 4 and tell the API before step 6:
  `dotnet user-secrets set "Seq:ServerUrl" "http://localhost:<port>" --project src/BillingRecon.Api`.

1. **Create `.env`.** Copy the template, then open `.env` and set `MSSQL_ACCEPT_EULA=Y`,
   `SEQ_ACCEPT_EULA=Y` and a password of your own for `MSSQL_SA_PASSWORD` (rules are in the file).

   ```bash
   cp .env.example .env
   ```

2. **Start the containers.**

   ```bash
   docker compose up -d
   ```

3. **Confirm SQL Server and Seq are healthy.** Both should show `healthy`; SQL Server can take up to
   a minute.

   ```bash
   docker compose ps
   ```

   If `sql` is not healthy, `docker logs billingrecon-sql` shows the reason (most often the password
   policy), and `docker inspect --format "{{json .State.Health}}" billingrecon-sql` shows the output
   of the health command. For Seq, `docker logs billingrecon-seq`.

4. **Open Seq** at `http://localhost:5341`. It should open without asking for a login.

5. **Set the connection string with user-secrets** (once per machine). Replace the placeholder with
   the password you put in `.env`. It uses `sa` only until the migration slice creates the
   `billingrecon_app` login (`docs/database.md` §7).

   ```bash
   dotnet user-secrets set "Database:ConnectionString" "Server=localhost,1433;Database=BillingRecon;User Id=sa;Password=<your-sa-password>;TrustServerCertificate=True" --project src/BillingRecon.Api
   ```

6. **Run the API.** It should start and log JSON lines to the console. In Development it creates the
   `BillingRecon` database on first start and applies the migrations (one line says so), then adds the
   row of the development tenant of `appsettings.Development.json` if it is missing (another line says
   so), which every patient and payer saved in Development refers to.

   ```bash
   dotnet run --project src/BillingRecon.Api
   ```

7. **Check liveness and readiness.** Expect `200 Healthy` for both.

   ```bash
   curl -i http://localhost:5080/health/live
   ```

   ```bash
   curl -i http://localhost:5080/health/ready
   ```

8. **Check that readiness follows SQL Server.** Stop the database, repeat the readiness call and
   expect `503 Unhealthy` while liveness stays 200. Start it again and wait for `healthy`.

   ```bash
   docker compose stop sql
   ```

   ```bash
   docker compose start sql
   ```

9. **Send a request with a correlation id.** The response must carry the same
   `X-Correlation-Id` header.

   ```bash
   curl -i -H "X-Correlation-Id: 3f2504e0-4f89-11d3-9a0c-0305e82c3301" http://localhost:5080/api/v1/system/info
   ```

10. **Find it in Seq.** In the Seq search box enter the filter below. The request event
    (`HTTP GET /api/v1/system/info responded 200 ...`) should appear, with
    `Application = BillingRecon.Api`.

    ```
    CorrelationId = '3f2504e0-4f89-11d3-9a0c-0305e82c3301'
    ```

11. **Check the error shape and the test suite.** The first command should return 404 with
    `application/problem+json` and `errorCode`, `traceId` and `correlationId` in the body.

    ```bash
    curl -i http://localhost:5080/api/v1/does-not-exist
    ```

    ```bash
    dotnet test
    ```

12. **Record the result.** When steps 3, 7 and 10 pass, remove every "results still to be recorded" note: row 1.3
    of section 8, the note at the top of `docker-compose.yml`, and the first paragraph of this section. If something had
    to change (an image tag, a healthcheck timing, a variable), change it in the compose file and
    say so in the commit.

13. **The API container image** (`src/BillingRecon.Api/Dockerfile`, section 6) runs on the Debian-based
    .NET image, which includes the time zone data the API needs: it refuses to start without
    `Asia/Kolkata`. Alpine and "chiseled" images would need the `tzdata` package or the variant that
    ships it. CI starts the container and expects `/health/live` to answer; repeat that check after any
    change of the base image.

Things only a real run can show, worth a note when you see them: how long each container takes to
become healthy, and the message Compose prints when a licence variable is left empty.

---

## 3. Migrations

The EF Core tools are a local tool of the repository (`dotnet tool restore`, once per clone). They
use the Infrastructure project and its design-time factory; they do not start the API and do not
read its configuration. Run every command from the repository root.

**Add a migration** (no database needed). Names are `NNNN_<Slice>`, the next free number first:

```bash
dotnet ef migrations add 0003_ServiceItems --project src/BillingRecon.Infrastructure --output-dir Common/Migrations
```

(That is the command for the third migration, the service catalogue of Prompt 2.3. The first was `0001_Tenants`; the
second, Patients and Payers, was scaffolded as `0002_SyncCurrentModel` and keeps that name.)

**The current list of every migration that is waiting, with its commands in order, is section 8; the recipe below is how it began and explains the method.**

**The five migrations that are waiting (Prompts 2.3, 2.4, 3.1, 3.2 and 3.4), one after the other.** The five prompts were
written before any of their migrations was scaffolded, so the model already holds the tables of all five, and the
command above alone would put them all into `0003_ServiceItems`. To get one migration per slice, take the
configurations of the later slices out of the project, and put them back one slice at a time (PowerShell, from the
repository root; without its configuration a table is not in the model, and the project still builds):

```powershell
New-Item -ItemType Directory -Force ..\held | Out-Null
Move-Item src\BillingRecon.Infrastructure\Billing\BillConfiguration.cs, src\BillingRecon.Infrastructure\Billing\BillLineConfiguration.cs, src\BillingRecon.Infrastructure\Common\Numbering\NumberSeriesConfiguration.cs, src\BillingRecon.Infrastructure\Common\Idempotency\IdempotencyKeyConfiguration.cs, src\BillingRecon.Infrastructure\Audit\AuditRecordConfiguration.cs ..\held\
dotnet ef migrations add 0003_ServiceItems --project src/BillingRecon.Infrastructure --output-dir Common/Migrations
Move-Item ..\held\BillConfiguration.cs, ..\held\BillLineConfiguration.cs src\BillingRecon.Infrastructure\Billing\
dotnet ef migrations add 0004_Bills --project src/BillingRecon.Infrastructure --output-dir Common/Migrations
Move-Item ..\held\NumberSeriesConfiguration.cs src\BillingRecon.Infrastructure\Common\Numbering\
dotnet ef migrations add 0005_NumberSeries --project src/BillingRecon.Infrastructure --output-dir Common/Migrations
Move-Item ..\held\IdempotencyKeyConfiguration.cs src\BillingRecon.Infrastructure\Common\Idempotency\
dotnet ef migrations add 0006_IdempotencyKeys --project src/BillingRecon.Infrastructure --output-dir Common/Migrations
Move-Item ..\held\AuditRecordConfiguration.cs src\BillingRecon.Infrastructure\Audit\
dotnet ef migrations add 0007_Audit --project src/BillingRecon.Infrastructure --output-dir Common/Migrations
```

Check before going on: `*_0003_ServiceItems.cs` creates the table `ServiceItems` and nothing else,
`*_0004_Bills.cs` creates `Bills` and `BillLines` and nothing else, `*_0005_NumberSeries.cs` creates
`NumberSeries` and nothing else, `*_0006_IdempotencyKeys.cs` creates `IdempotencyKeys` and nothing else, and
`*_0007_Audit.cs` creates `AuditRecords` and nothing else. If
one of them creates more, a file was not moved: run
`dotnet ef migrations remove --project src/BillingRecon.Infrastructure` once per migration added, and start again.
Check too that `..\held` is empty at the end. Remove this paragraph once the five migrations are committed.
Then build and run the tests: `MigrationsTests` checks that each migration exists under its name, that
the model has no change missing from the migrations, and that their script creates what
`docs/database.md` describes; they fail while a migration the model needs is missing, and so does
every test of the `Database` collection, whose database is migrated first. Review the generated `Up`
and `Down` before committing; they should need no hand editing (ADR 0015).

**Check that no migration is missing** (no database needed; the same check runs as a test):

```bash
dotnet ef migrations has-pending-model-changes --project src/BillingRecon.Infrastructure
```

**See the SQL** the migrations run (no database needed):

```bash
dotnet ef migrations script --project src/BillingRecon.Infrastructure
```

**Apply them.** In Development the API does it when it starts. To do it by hand, give the tools a
connection string, either with `--connection` or in the `Database__ConnectionString` environment
variable (the tools do not read user-secrets):

```bash
dotnet ef database update --project src/BillingRecon.Infrastructure --connection "Server=localhost,1433;Database=BillingRecon;User Id=sa;Password=<your-sa-password>;TrustServerCertificate=True"
```

Outside Development the API never migrates. Applying migrations there is a deployment step, run
with the migration login (`docs/database.md` section 7); the procedure, with the creation of the logins, is
section 10.

**Undo the last migration** that has not been applied anywhere yet:

```bash
dotnet ef migrations remove --project src/BillingRecon.Infrastructure
```

A migration that has been applied to a shared database is never removed or edited; it is corrected
by a new one.

**Rolling back a migration that was applied** (Prompt 8.3). Migrations go forward only. Take a full backup before every
deployment of migrations (section 15). Then, if a deployed migration is wrong:

- it only added what nothing uses yet: write a new migration that takes it out again, scaffolded from the model with the
  change reverted, and deploy it like any other (section 10);
- it changed or lost data: stop the API, restore the backup taken before the deployment (section 15), and deploy the
  corrected migrations onto it.

`dotnet ef database update <earlier migration>` runs the `Down()` methods and is never used on a database with financial
data: a `Down()` that drops a column drops what it held, and the ledger and the audit trail refuse the deletes some would
need (ADR 0034).

## 4. Tests

`dotnet test` runs the four test projects. The `Database` collection of the integration tests starts
SQL Server 2022 in a container with Testcontainers (the image `docker-compose.yml` names), applies
the migrations once and empties the tables between tests (ADR 0016). It needs a running Docker
engine; the first run on a machine pulls the image unless `docker compose up` already did.
Testcontainers removes the container when the run ends.

```bash
dotnet test
```

Without Docker the collection fails with one message that says so, and every other test still runs.
To run everything but the database tests:

```bash
dotnet test --filter "FullyQualifiedName!~IntegrationTests.Database."
```

The trailing dot matters: without it the filter also drops `DatabaseStartupTests`, which needs no
Docker.

If the container does not start: Docker Desktop must be running, and the image is x86-64 only. Port
conflicts cannot happen (Testcontainers maps a free host port). The fixture's message carries the
Testcontainers exception with the detail.

## 5. Continuous integration

`.github/workflows/ci.yml` runs on every push to `main` and every pull request (ADR 0017), as three jobs
on GitHub's Ubuntu runners, which have Docker: the API's gates below, its container image (section 6), and since
Prompt 7.1 the frontend's (install the locked packages, `npm audit`, format, lint, the generated API types against the
committed document, types and build, tests; section 12, ADR 0038). Every gate has a local equivalent:

| Gate | What it checks | The same thing locally |
|---|---|---|
| Restore | Tools and packages restore; the NuGet audit refuses an advisory of any severity | `dotnet tool restore`, then `dotnet restore` |
| Format | Whitespace, usings and the style rules of `.editorconfig` | `dotnet format --verify-no-changes`; plain `dotnet format` applies the fixes |
| Build | Warnings are errors; the deterministic CI build is on | `dotnet build --configuration Release` |
| Test | All four projects, the `Database` collection included (SQL Server 2022 in Docker) | `dotnet test`, Docker running (section 4) |
| Coverage | The Domain and Application unit tests, instrumented by coverlet (cobertura files); a guide of 80 per cent, not a gate. The integration tests are not instrumented: the PII masker's timeout test is sensitive to the slowdown | `dotnet test tests/BillingRecon.Domain.Tests --collect:"XPlat Code Coverage"`, same for `Application.Tests` |
| Vulnerable packages | A readable report of every direct and transitive advisory; a High or Critical one fails | `dotnet list package --vulnerable --include-transitive` |
| Migration script | The idempotent script of every migration, kept as an artifact | `dotnet ef migrations script --idempotent --project src/BillingRecon.Infrastructure --output artifacts/BillingRecon.sql` |
| Image | The API image builds, starts as non-root with time zone data, and answers `/health/live` | section 6 |

Artifacts of a run: `test-results` (trx files), `coverage` (cobertura files), `vulnerable-packages`
and `migration-script`. Coverage is a report to read, not a gate.

**When the restore fails on an advisory** (NU1901 to NU1904), in CI or locally: take the package from
the message and pin the patched version in `Directory.Packages.props`. A transitive package gets a
`PackageVersion` entry of its own and a direct `PackageReference` in the project that pulls it in,
until the package above it moves on. Say so in the commit. The audit stays on.

**Dependabot** opens weekly pull requests for NuGet, with minor and patch updates grouped, and for
the actions the workflow uses. Major versions of the framework-bound packages are left to a
deliberate decision (ADR 0014). Since Prompt 7.1 it does the same for the frontend's npm packages, a week after a
release is published, and leaves the major versions of ESLint and TypeScript to a decision (ADR 0038).

**Acceptance of the CI step** (section 8, row 1.5): the first green run on GitHub, and a pull request with one deliberately broken assertion that goes red and is closed unmerged.

## 6. The API container image

`src/BillingRecon.Api/Dockerfile` builds a multi-stage image: the SDK image publishes, the Debian-based
ASP.NET Core runtime image runs the result as the non-root `app` user on port 8080, with curl for the
health check (ADR 0017). Build it from the repository root:

```bash
docker build -f src/BillingRecon.Api/Dockerfile -t billingrecon-api .
```

Run it against the compose SQL Server. From a container, the host is `host.docker.internal` on Docker
Desktop; the password is the one in `.env`:

```bash
docker run --rm -p 8080:8080 -e "Database__ConnectionString=Server=host.docker.internal,1433;Database=BillingRecon;User Id=sa;Password=<your-sa-password>;TrustServerCertificate=True" -e "Jwt__SigningKey=<a key of your own, section 9>" -e "AllowedHosts=localhost" billingrecon-api
```

Then `http://localhost:8080/health/live` answers 200, and `/health/ready` answers 200 once the
database has every migration. The container runs as Production and never migrates: apply the script
of section 3, or let a Development run of the API do it first. Without `Database__ConnectionString`,
`Jwt__SigningKey` or `AllowedHosts` the container stops at startup with the message that names the setting. The
health check is visible with `docker inspect --format "{{json .State.Health}}" <container>`.

**The API itself answers HTTPS only** (ADR 0035): over plain HTTP, `http://localhost:8080/api/v1/...` gets 400
`https_required`, and only the health endpoints answer. That is by design: TLS ends at a proxy in front of the
container (section 11). The CI smoke run checks exactly this, and that no answer has a `Server` header. To call the
API locally without a proxy, run it with `dotnet run` (Development, section 1).

The image tags follow the major version (`sdk:10.0`, `aspnet:10.0`), and `global.json` must accept the
SDK the build image carries. If a build fails on the SDK version, pass the exact tag:

```bash
docker build -f src/BillingRecon.Api/Dockerfile --build-arg SDK_TAG=10.0.402 -t billingrecon-api .
```

---

## 7. Idempotency keys: retention and a key that is stuck

Every financial POST leaves a row in `IdempotencyKeys` (BR-803, ADR 0006; `docs/database.md` section 6). Nothing
deletes these rows on a schedule yet. Until the sweep is built, the two clean-ups below are run by hand, as the
migrator login or `sa`, when the table has grown or a client reports a key that answers 409 for ever. Both delete
only what the application itself is allowed to delete; neither touches a financial record.

**Expired keys.** A key is kept for 30 days (`ExpiresAtUtc`). After that a retry with it is no longer expected, and
the row can go. Run it in batches, so that the delete never holds a long lock on a table every posting writes to:

```sql
DECLARE @now datetimeoffset(3) = SYSUTCDATETIME();

WHILE 1 = 1
BEGIN
    DELETE TOP (5000) FROM dbo.IdempotencyKeys WHERE ExpiresAtUtc < @now;
    IF @@ROWCOUNT = 0 BREAK;
END
```

The index of the table leads with the tenant, so this statement scans. That is acceptable for an occasional manual
run; the scheduled sweep will go tenant by tenant.

**A key stuck in progress.** If the API process dies between reserving a key and answering, the row stays
`InProgress` and every retry with that key is answered 409 `idempotency_in_progress`. The business transaction of
the dead request was rolled back by SQL Server, so nothing of it was saved. First make sure the request is not
simply still running (a row younger than a few minutes may be), then look at it and remove it:

```sql
SELECT TenantId, [Key], UserId, Method, Path, CreatedAtUtc
FROM   dbo.IdempotencyKeys
WHERE  Status = 'InProgress' AND CreatedAtUtc < DATEADD(minute, -15, SYSUTCDATETIME());

DELETE FROM dbo.IdempotencyKeys
WHERE  TenantId = @tenant AND [Key] = @key AND Status = 'InProgress';
```

After that the client's retry with the same key runs as a first request. Never delete a `Completed` row to "let a
request run again": its work is already done, and the retry would do it a second time.

---

## 9. Sign-in and the signing key

The API issues its own tokens at `POST /api/v1/auth/login` and checks them on every request (ADR 0031;
`docs/api.md` sections 2 and 8.12). Everything about them hangs on one secret, `Jwt:SigningKey`.

**Making a key.** 32 random bytes or more, in base64. In PowerShell 7 or bash:

```powershell
[Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

```bash
openssl rand -base64 32
```

Keep it where the connection string is kept (user-secrets on a developer's machine, the secret store of the
deployment elsewhere) and set it as `Jwt__SigningKey`. The key of `appsettings.Development.json` is in the
repository, so the API refuses it outside Development.

**Changing the key** signs everybody out: every token issued with the old key is refused (401) from the moment the
API starts with the new one, and each client signs in again. That is also what to do when the key may have leaked.
There is no list of tokens to revoke; a token lives 60 minutes at most.

**Signing in locally.** In Development nothing needs a token: a request without one acts as the development user, so
the Scalar page works as it is. To try the real sign-in there, give a user a password first (a TenantAdmin creates
one with `POST /api/v1/users`, or resets one with `POST /api/v1/users/{id}/password`), then sign in with the code of
its tenant (the development tenant's is `development`), its address and that password, and send the token as
`Authorization: Bearer <token>`.

**A refused sign-in** answers 401 `invalid_credentials` whatever went wrong. The reason is in the log: a warning
"A sign-in to tenant {TenantCode} was refused: {Refusal}", with the user's id when the address was found. The
reasons are `UnknownTenant`, `TenantSuspended`, `UnknownUser`, `WrongPassword` and `UserDisabled`. The address and
the password are never logged.

**Too many sign-ins** answer 429 `too_many_sign_ins` with `Retry-After` (ADR 0035): from one client address, more than
30 a minute; for one account (the hospital's code and the address), after 10 refusals in 15 minutes, until the 15
minutes have passed, the right password included. Each writes a warning: "Sign-ins from {ClientAddress} are limited"
or "Sign-ins to tenant {TenantCode} for one address are held". **A user who is held** waits, at most 15 minutes; there
is nothing to unlock, and nothing an administrator can do sooner short of restarting the API, which forgets every
limit. Many warnings for one tenant, or from one address, are worth a look: someone may be guessing. The limits are
kept by each instance of the API in memory, so a restart forgets them and two instances would each allow as much.

---

## 8. Deferred verification: the running list for the personal machine

**Since Prompt 10.0 the whole list runs as one command**, from the repository root with Docker Desktop running and
`.env` in place: `powershell -ExecutionPolicy Bypass -File deploy\verify\personal-machine.ps1` (`-SkipLoad` leaves out the
10,000-bill load). It runs A, C and D below (D three rounds), the frontend's gates, the one-command run afresh with the
checks of section 2 and row 8.1, the load of row 8.2, every browser suite through nginx, and the backup of row 8.3, and
writes everything to `verify-10.0.log`, which is sent back. What it cannot do is listed at its end: the event in Seq,
a CSV opened in a spreadsheet, the screens looked at by the owner.

On 2026-11-20 the owner decided to go on building and to run the verification on the personal machine later.
Until this list has been run and its output recorded, **nothing after Prompt 2.1 has been built on .NET 10, run on
SQL Server 2022, or had its migration scaffolded with EF Core 10**; every commit made in the meantime says so in
its message. This is the one list of what is owed. A prompt that adds a migration or a check adds its line here,
in order; nothing is removed until the owner has run it and sent the output.

**B and E done on 2026-11-21, in the repository: migrations `0003` to `0015` are committed ("Migrations 0003 to 0015,
scaffolded with EF Core 10"), each scaffolded on its own with EF Core 10.0.12 as B describes, and EF Core reports no
pending model change. Do not run B again.** A, C and D are still owed on a machine with Docker. Since then
`0016_CreditApplicationReversals` (Prompt 4.5b), `0017_Refunds` (Prompt 4.6), `0018_Users` (Prompt 5.1),
`0019_PrefixChanges` (Prompt 5.3b) and `0020_DatabaseRoles` (Prompt 5.4) were added the ordinary way, described in the
paragraph after the table below; they are in the repository too.

**Partly run on 2026-11-21, on the office machine, which got the .NET 10 SDK (10.0.404) that day; it still has no
Docker.** In a throwaway copy of the tip of the branch with Prompt 4.4, with no version substitution: A and B ran
as written (the repository's `dotnet-ef` 10.0.12 scaffolded `0003` to `0014` one by one, each holding exactly what
its row below says, and the hand-written line went into `0008`); C ran with the database fixture pointed at SQL
Server LocalDB 2019 in the copy: strict build and format gate clean, Domain 560, Application 313, Architecture 113
and Integration 977 tests green, none failing; D ran six times for all its classes together, five clean and once
with one failure in `BillApprovalApiTests` that did not come back in 45 further runs (CLAUDE.md section 9,
item 23). **Still owed:** B in the repository itself, with the migration files committed (E); C and D against SQL
Server 2022 in Docker, which is the one thing LocalDB 2019 cannot stand in for.

Run from the repository root, in PowerShell, with Docker Desktop running, on the tip of the branch.

**A. Once**

```powershell
dotnet --list-sdks
dotnet tool restore
```

**B. The migrations, one after the other** (section 3 explains why the configurations are moved out and back).
**Done on 2026-11-21; kept as the record of how `0003` to `0015` were made, not to be run again.**

```powershell
New-Item -ItemType Directory -Force ..\held | Out-Null
Move-Item src\BillingRecon.Infrastructure\Billing\BillConfiguration.cs, src\BillingRecon.Infrastructure\Billing\BillLineConfiguration.cs, src\BillingRecon.Infrastructure\Common\Numbering\NumberSeriesConfiguration.cs, src\BillingRecon.Infrastructure\Common\Idempotency\IdempotencyKeyConfiguration.cs, src\BillingRecon.Infrastructure\Audit\AuditRecordConfiguration.cs, src\BillingRecon.Infrastructure\Receivables\LedgerPostingConfiguration.cs, src\BillingRecon.Infrastructure\Receivables\LedgerEntryConfiguration.cs, src\BillingRecon.Infrastructure\Receivables\PayerBalanceConfiguration.cs, src\BillingRecon.Infrastructure\Billing\BillPostingConfiguration.cs, src\BillingRecon.Infrastructure\Billing\BillApprovalConfiguration.cs, src\BillingRecon.Infrastructure\Common\Approvals\ApprovalRequestConfiguration.cs, src\BillingRecon.Infrastructure\Payments\PaymentConfiguration.cs, src\BillingRecon.Infrastructure\Payments\PaymentWithAllocationsConfiguration.cs, src\BillingRecon.Infrastructure\Billing\CreditNoteConfiguration.cs, src\BillingRecon.Infrastructure\Billing\CreditNoteLineConfiguration.cs, src\BillingRecon.Infrastructure\Billing\CreditNoteAllocationConfiguration.cs, src\BillingRecon.Infrastructure\Adjustments\AdjustmentConfiguration.cs, src\BillingRecon.Infrastructure\Payments\PaymentWithReversalsConfiguration.cs ..\held\
dotnet ef migrations add 0003_ServiceItems --project src/BillingRecon.Infrastructure --output-dir Common/Migrations
Move-Item ..\held\BillConfiguration.cs, ..\held\BillLineConfiguration.cs src\BillingRecon.Infrastructure\Billing\
dotnet ef migrations add 0004_Bills --project src/BillingRecon.Infrastructure --output-dir Common/Migrations
Move-Item ..\held\NumberSeriesConfiguration.cs src\BillingRecon.Infrastructure\Common\Numbering\
dotnet ef migrations add 0005_NumberSeries --project src/BillingRecon.Infrastructure --output-dir Common/Migrations
Move-Item ..\held\IdempotencyKeyConfiguration.cs src\BillingRecon.Infrastructure\Common\Idempotency\
dotnet ef migrations add 0006_IdempotencyKeys --project src/BillingRecon.Infrastructure --output-dir Common/Migrations
Move-Item ..\held\AuditRecordConfiguration.cs src\BillingRecon.Infrastructure\Audit\
dotnet ef migrations add 0007_Audit --project src/BillingRecon.Infrastructure --output-dir Common/Migrations
Move-Item ..\held\LedgerPostingConfiguration.cs, ..\held\LedgerEntryConfiguration.cs, ..\held\PayerBalanceConfiguration.cs src\BillingRecon.Infrastructure\Receivables\
dotnet ef migrations add 0008_Receivables --project src/BillingRecon.Infrastructure --output-dir Common/Migrations
Move-Item ..\held\BillPostingConfiguration.cs src\BillingRecon.Infrastructure\Billing\
dotnet ef migrations add 0009_BillPosting --project src/BillingRecon.Infrastructure --output-dir Common/Migrations
Move-Item ..\held\BillApprovalConfiguration.cs src\BillingRecon.Infrastructure\Billing\
Move-Item ..\held\ApprovalRequestConfiguration.cs src\BillingRecon.Infrastructure\Common\Approvals\
dotnet ef migrations add 0010_Approvals --project src/BillingRecon.Infrastructure --output-dir Common/Migrations
Move-Item ..\held\PaymentConfiguration.cs src\BillingRecon.Infrastructure\Payments\
dotnet ef migrations add 0011_Payments --project src/BillingRecon.Infrastructure --output-dir Common/Migrations
Move-Item ..\held\PaymentWithAllocationsConfiguration.cs src\BillingRecon.Infrastructure\Payments\
dotnet ef migrations add 0012_PaymentAllocations --project src/BillingRecon.Infrastructure --output-dir Common/Migrations
Move-Item ..\held\CreditNoteConfiguration.cs, ..\held\CreditNoteLineConfiguration.cs, ..\held\CreditNoteAllocationConfiguration.cs src\BillingRecon.Infrastructure\Billing\
dotnet ef migrations add 0013_CreditNotes --project src/BillingRecon.Infrastructure --output-dir Common/Migrations
Move-Item ..\held\AdjustmentConfiguration.cs src\BillingRecon.Infrastructure\Adjustments\
dotnet ef migrations add 0014_Adjustments --project src/BillingRecon.Infrastructure --output-dir Common/Migrations
Move-Item ..\held\PaymentWithReversalsConfiguration.cs src\BillingRecon.Infrastructure\Payments\
dotnet ef migrations add 0015_PaymentReversals --project src/BillingRecon.Infrastructure --output-dir Common/Migrations
```

Mind the second line: `BillPostingConfiguration.cs` and `BillApprovalConfiguration.cs` are moved out with the others
and come back **late**, each before its own migration, not with the two Bill configurations before `0004_Bills`.
That is what keeps the columns of posting and the index of waiting bills out of `0004` (ADR 0022, ADR 0023). In the
same way `PaymentWithAllocationsConfiguration.cs` comes back **after** `0011_Payments`, not with
`PaymentConfiguration.cs`: that keeps the table of allocations out of `0011` (ADR 0024).

| Migration | Prompt | Must create exactly |
|---|---|---|
| `0003_ServiceItems` | 2.3 | `ServiceItems` |
| `0004_Bills` | 2.4 | `Bills`, `BillLines`; no `Number`, `FiscalYear`, `SequenceNumber`, `PostedAtUtc` or `PostedByUserId` in `Bills` |
| `0005_NumberSeries` | 3.1 | `NumberSeries` |
| `0006_IdempotencyKeys` | 3.2 | `IdempotencyKeys` |
| `0007_Audit` | 3.4 | `AuditRecords` |
| `0008_Receivables` | 3.3 | `LedgerPostings`, `LedgerEntries`, `PayerBalances`, and the hand-written line below |
| `0009_BillPosting` | 3.5 | No table. On `Bills`: five columns (`Number`, `FiscalYear`, `SequenceNumber`, `PostedAtUtc`, `PostedByUserId`), two unique indexes (`UX_Bills_TenantId_Number`, `UX_Bills_TenantId_FiscalYear_SequenceNumber`), three CHECK constraints (`CK_Bills_NumberIffPosted`, `CK_Bills_NumberParts`, `CK_Bills_StatusMatchesBalance`) |
| `0010_Approvals` | 3.6 | `ApprovalRequests`, and on `Bills` the one index `IX_Bills_TenantId_Status` |
| `0011_Payments` | 4.1 | `Payments`, with three foreign keys (`TenantId`, `PayerId`, `PostingId`), six CHECK constraints and four indexes; no reversal column |
| `0012_PaymentAllocations` | 4.2 | `PaymentAllocations`, with four foreign keys (`TenantId`, `PaymentId`, `BillId`, `PostingId`), two CHECK constraints and two indexes; no row version and no reversal column. Nothing on `Payments` or `Bills` |
| `0013_CreditNotes` | 4.3 | `CreditNotes`, `CreditNoteLines`, `CreditNoteAllocations`, with eleven foreign keys in all (four, three and four) and nine indexes (five, two and two). Nothing on `Bills` or `BillLines` |
| `0014_Adjustments` | 4.4 | `Adjustments`, with four foreign keys (`TenantId`, `BillId`, `PayerId`, `PostingId`) and four indexes. Nothing on `Bills` |
| `0015_PaymentReversals` | 4.5 | No table. On `Payments` and on `PaymentAllocations`: four columns each (`ReversedAtUtc`, `ReversedByUserId`, `ReversalReason`, `ReversalPostingId`), one CHECK constraint (`CK_..._ReversedIffReversed`) and one foreign key (`FK_..._ReversalPostingId`). `0011` and `0012` must hold none of these columns |
| `0016_CreditApplicationReversals` | 4.5b | Added the ordinary way, not by B. No table. On `CreditNoteAllocations`: five columns (`AppliedAtPosting`, `ReversedAtUtc`, `ReversedByUserId`, `ReversalReason`, `ReversalPostingId`), `CK_CreditNoteAllocations_Status` dropped and created again with three states, two new CHECK constraints (`_ReversedIffReversed`, `_AtPostingStaysActive`) and one foreign key (`FK_CreditNoteAllocations_ReversalPostingId`). `0013` must hold none of these columns |
| `0017_Refunds` | 4.6 | Added the ordinary way, not by B. `Refunds`, with five foreign keys (`TenantId`, `PayerId`, `SourcePaymentId`, `SourceCreditNoteId`, `PostingId`), nine CHECK constraints and five indexes. Nothing on `Payments` or `CreditNotes` |
| `0018_Users` | 5.1 | Added the ordinary way, not by B. `Roles` with its five rows, `Users` and `UserRoles`; then 34 foreign keys `FK_<Table>_<Column>` from every earlier column that names a user to `Users`. No column of an earlier table changes. Applied to a development database with data, it fails: drop that database first (section 1) |
| `0019_PrefixChanges` | 5.3b | Added the ordinary way, not by B. `PrefixChanges`, with three foreign keys (`TenantId`, `CreatedByUserId`, `UpdatedByUserId`), six CHECK constraints and two indexes, one of them the filtered unique index of the waiting change. On `Tenants`: two CHECK constraints (`CK_Tenants_PrefixesDistinct`, `CK_Tenants_PostingApprovalThreshold`). On `ApprovalRequests`: `CK_ApprovalRequests_DocumentType` dropped and created again with `PrefixChange` in place of the unused `NumberSeries`. No column of an earlier table changes, and no data step |
| `0020_DatabaseRoles` | 5.4 | Added the ordinary way, not by B: scaffolded empty (no model change) and written by hand (ADR 0034). The role `billingrecon_application`, owned by `dbo`, and its grants (`docs/database.md` section 7). No table changes. The logins are section 10 |

**`0008_Receivables` is finished by hand** (ADR 0021): a scaffold writes schema, and this migration also has to
open the balances of every payer that already exists. Open `Common/Migrations/*_0008_Receivables.cs` (not the
`.Designer.cs`), and paste this one line as the last statement of `Up()`, after the last `CreateIndex` and before
the closing brace of the method:

```csharp
            migrationBuilder.Sql("INSERT INTO [PayerBalances] ([PayerId], [TenantId], [Receivable], [UnappliedCash], [CreditAvailable], [UpdatedAtUtc]) SELECT p.[Id], p.[TenantId], 0, 0, 0, SYSUTCDATETIME() FROM [Payers] AS p WHERE NOT EXISTS (SELECT 1 FROM [PayerBalances] AS b WHERE b.[PayerId] = p.[Id]);");
```

Change nothing else in the file, and nothing in `Down()`. If the line is missing or altered, the test
`BR404_the_receivables_migration_opens_the_balances_of_every_payer_that_already_exists` fails and names it.

`..\held` must be empty at the end. If a migration creates more than its row says, a file was not moved: run
`dotnet ef migrations remove --project src/BillingRecon.Infrastructure` once per migration added and start B again.
Since the migrations are in the repository, a later prompt adds its migration the ordinary way, with
`dotnet ef migrations add NNNN_<Slice> --project src/BillingRecon.Infrastructure --output-dir Common/Migrations`, and
holds no configuration back; it adds its row to the table above.

**C. Build, format, tests**

```powershell
dotnet build
dotnet format --verify-no-changes
dotnet test
```

**D. The concurrency tests, several times each** (a race shows on some runs only)

```powershell
dotnet test --filter "FullyQualifiedName~NumberSeriesOnSqlServerTests"
dotnet test --filter "FullyQualifiedName~IdempotencyApiTests"
dotnet test --filter "FullyQualifiedName~BillsApiTests"
dotnet test --filter "FullyQualifiedName~AuditOnSqlServerTests|FullyQualifiedName~AuditTrailApiTests"
dotnet test --filter "FullyQualifiedName~ReceivablesOnSqlServerTests"
dotnet test --filter "FullyQualifiedName~BillPostingApiTests"
dotnet test --filter "FullyQualifiedName~BillApprovalApiTests"
dotnet test --filter "FullyQualifiedName~PaymentsApiTests"
dotnet test --filter "FullyQualifiedName~PaymentAllocationsApiTests"
dotnet test --filter "FullyQualifiedName~CreditNotesApiTests"
dotnet test --filter "FullyQualifiedName~AdjustmentsApiTests"
dotnet test --filter "FullyQualifiedName~PaymentReversalsApiTests"
dotnet test --filter "FullyQualifiedName~CreditApplicationReversalsApiTests"
dotnet test --filter "FullyQualifiedName~RefundsApiTests|FullyQualifiedName~EndToEndScenarioTests"
dotnet test --filter "FullyQualifiedName~UsersApiTests"
dotnet test --filter "FullyQualifiedName~SettingsApiTests"
dotnet test --filter "FullyQualifiedName~DatabasePermissionsOnSqlServerTests"
```

`SettingsApiTests` includes the ten rounds of a first bill and a prefix approval at the same instant (ADR 0033,
"Amended 2026-11-23").

**E. Send back**

- the output of A, C and D, and the names of the migration files B created;
- the migration files themselves (`Common/Migrations/*_0003_*` to `*_0015_*` and the updated
  `BillingReconDbContextModelSnapshot.cs`), which are then committed as "Migrations 0003 to 0015, scaffolded with
  EF Core 10".

**What that run has to show, because nothing here could** (the office machine has .NET 9 and LocalDB 2019 only):

**First run of the whole list: 2026-11-24, on the personal machine, by `deploy/verify/personal-machine.ps1` at commit
`4c2e75f` (Prompt 10.2), with its `verify-10.0.log` sent back.** SDK 10.0.404, Docker 29.8.1, SQL Server 2022 CU27 in
Docker, Node 22.12.0. What it showed:

- **A and C.** `dotnet tool restore`, the strict build and the format gate clean. `dotnet test`: Domain 652,
  Application 431 and Architecture 132 green; Integration 1277 green of 1280, the `Database` collection on SQL Server
  2022 included, with the reconciliation after every test. The three failures were the long values of `PiiMaskerTests`,
  which crossed the 250 ms match timeout on a busy machine; fixed in `17af1e5`, to be seen green in the second run.
- **D.** Three rounds of the concurrency classes, 291 tests each, all green.
- **Section 2 and row 8.1.** Every container healthy, `/health/ready` 200, 503 while SQL Server was stopped and 200
  again after; the page's headers with the policy, `frame-ancestors 'none'` and `nosniff`; http://localhost answering
  301 to https.
- **Row 8.3.** The backup, the `.bak` copied out, the restore under another name with `CHECKSUM`, `DBCC CHECKDB` and
  the 21 migrations in the restored database.
- **The browser suites through nginx.** 283 of 288 green: `cashier`, `dashboard`, `money`, `print`, `shell` and
  `signin` whole, and `routes` but two tests.
- **Red, and why.** The demo seed and the load test, refused by the forced password change of Prompt 10.1 (the journey
  and the full-size load with them; fixed in `a6cf3c3` and `fc7c07d`); two tests of `routes.spec.ts` that named a
  heading shown only while loading (`c85f539`); the last query of the backup step, a fault of the script (`f89c96b`);
  `npm ci` and the component tests, because that machine's Node is older than 22.22.

That run proved the rows of Prompts 2.2 to 6.3 and 8.1, which are gone from the table. What follows is what a second
run of the script, the owner's own eyes, or CI still have to show.

| Prompt | Not proven anywhere yet |
|---|---|
| 1.3 | The request's event found in Seq by its correlation id (step 9c of the script sends it; the owner looks for `3f2504e0-4f89-11d3-9a0c-0305e82c3301` at http://localhost:5341). |
| 1.5 | The first green run of `.github/workflows/ci.yml` on GitHub (the API job with the `Database` collection, the image built and smoke-run, the frontend job); then a pull request that breaks one assertion goes red and is closed unmerged (ADR 0017). The repository has a GitHub remote with `main` and `phase-2-billing` pushed; whether CI ran there is not recorded. |
| 5.4b | The API image's smoke run in CI (`AllowedHosts=localhost`, no `Server` header, 400 `https_required` for an API request over plain HTTP). |
| 7.1 | `npm ci` and `npm test` in `frontend/` on the personal machine with Node 22.22 or later (steps 7a and 7g; the first run had Node 22.12.0). |
| 7.1 to 7.6, 8.2, 9.1 | The frontend job of CI (see row 1.5), on Linux: the lock file with `@playwright/test`, Recharts 3.10.1 and `@fontsource-variable/inter`, the step that runs `deploy/load/stats.test.mjs`, and on the API job `OpenApiSnapshotTests` and `LineCalculatorSharedCasesTests` there. |
| 7.5b | A CSV of each report opened in a spreadsheet by the owner. |
| 1.2 | `PiiMaskerTests` green in the whole `dotnet test` of the second run (step 5). |
| 8.2 | The load test of section 14 at full size (10,000 bills) through nginx on SQL Server 2022, with `SERVER_TIMING=true` and at least 2 GB free: every target met, no drift (the whole output is in the log); the stress figure with no pause. The browser journey of section 14 against https://localhost (three tests of `journey.spec.ts`). The office machine ran the load on LocalDB at 2,000 bills only (ADR 0046, built note). |
| 8.3 | The recovery model of the compose database (`FULL` expected) and `is_read_committed_snapshot_on` = 1, the last query of step 12. |
| 10.3 | The `Database` collection on SQL Server 2022 (step 5 of the script) with migration `0023_OpeningBalances`: `OpeningBalancesApiTests`, the whole export of the reference hospital among them, ran on LocalDB 2019 only. The migration applied by `migrate` to a database that already holds bills and payments (every existing row gets `Origin = Standard`). By hand through nginx: the procedure of section 18 with `deploy/reference/export`, the batch approved from the inbox by a second user, the reconciliation without drift. `node deploy/reference/generate-export.mjs --check` in CI. |
| 10.2c | The `Database` collection on SQL Server 2022 (step 5 of the script) with migration `0022_UserSessions`: `AuthApiTests` (a sign-out, a disabling, a reset and changed roles refuse the token; a change of one's own password) and the permission walk ran on LocalDB 2019 only. The load test of section 14: every request now reads the user's row once, so its p95 targets are to be met again. By hand through nginx at https://localhost: sign in on two tabs, sign out on one, the other goes to the sign-in at once; a token copied before the sign-out answers 401 `session_ended` (`curl -k -H "Authorization: Bearer <token>" https://localhost/api/v1/system/info`). Everyone signs in once more after this deployment. |
| 10.2b | The `Database` collection on SQL Server 2022 (`dotnet test`, step 5 of the script): `PatientsApiTests` (a part of an MRN, the wildcards in an MRN) and `UsersApiTests` (a part of an e-mail, its wildcards) ran on LocalDB 2019 only. `e2e/search.spec.ts` with the other stubbed suites through nginx (step 11b). By hand at https://localhost, in both themes on a desktop and a phone: the lists of patients, payers, service items and users and the outstanding report answer while a search is typed, with no Search or Find button and Back leaving the list; in a new bill, a payment and a report, the patient, payer and catalogue pickers open their results while typing and are used by the keyboard alone and by a tap; the journey (`journey.spec.ts`) chooses from those results. |
| 10.2 | With Compose (`API_ENVIRONMENT=Production`, `DEMO_ENABLED=false`): `docker compose run --rm api tenant create ...` as in section 17.1, the administrator signs in at https://localhost and must change the password; `tenant suspend` refuses that user within 30 seconds, `tenant resume` lets them in; then `API_ENVIRONMENT=Demo` with the demo as before. By hand: the script does not run it. |
| 10.1 | Through nginx at https://localhost, by hand: the demo's administrator creates a user, that user signs in with the first password, is taken to "Set a password of your own" and reaches no screen before changing it; a reset does the same; the settings saved and a prefix change requested, then approved by a second administrator in the inbox; both themes on a desktop and a phone. |
| 9.4b | `routes.spec.ts` whole through nginx (step 11b). By the owner's eyes: the reports (the index, a report's filters, its totals in view, Export CSV with its progress), and a bill, a statement and a report printed from the browser on A4. |
| 9.1 to 9.4 | By the owner's eyes at https://localhost, in both themes on a desktop and a phone, with no content security policy refusal in the console: the sign-in page (the typeface from `/assets/`, the favicon), the shell, the money screens, the cashier's screens and the inbox, the dashboard with its tooltips by mouse, keyboard and tap. The frontend image built with `SUPPORT_EMAIL` set and unset. In Seq, no "severe error" from the database during the journey (backlog B35). |

A red result is reported as it is, with its output; the fix is its own commit on top, never an edit of history.

---

## 10. Database logins and grants (ADR 0034)

The API connects as **`billingrecon_app`**, a member of the database role `billingrecon_application` and of nothing
else; the migrations run as **`billingrecon_migrator`**. The role and its grants are made by migration
`0020_DatabaseRoles`; the logins, their passwords and their database users are made here, once per environment. Keep
the two passwords in the secret store of the deployment, never in a file in git; `<…>` below are placeholders.

**1. As the administrator** (a login with `CREATE DATABASE` and `ALTER ANY LOGIN`, for example `sysadmin`), on
`master`:

```sql
CREATE DATABASE [BillingRecon];
GO
-- Readers see the last committed row instead of waiting for a writer: the application relies on it (ADR 0034,
-- point 6). EF Core sends the same statement when it creates a database itself.
IF SERVERPROPERTY('EngineEdition') <> 5 BEGIN ALTER DATABASE [BillingRecon] SET READ_COMMITTED_SNAPSHOT ON; END;
GO
CREATE LOGIN [billingrecon_migrator] WITH PASSWORD = N'<migrator password>';
CREATE LOGIN [billingrecon_app] WITH PASSWORD = N'<application password>';
GO
USE [BillingRecon];
CREATE USER [billingrecon_migrator] FOR LOGIN [billingrecon_migrator];
ALTER ROLE [db_ddladmin] ADD MEMBER [billingrecon_migrator];
ALTER ROLE [db_datareader] ADD MEMBER [billingrecon_migrator];
ALTER ROLE [db_datawriter] ADD MEMBER [billingrecon_migrator];
ALTER ROLE [db_securityadmin] ADD MEMBER [billingrecon_migrator];
CREATE USER [billingrecon_app] FOR LOGIN [billingrecon_app];
```

The migrator changes the schema, runs the data steps and creates the role with its grants; it is not `db_owner` and
cannot create a database (`docs/database.md` section 7).

**2. As the migrator, apply the migrations**, now and at every release. The script is idempotent: it skips what the
database already has. PowerShell, from the repository root; the password comes from the environment, not the command
line:

```powershell
dotnet ef migrations script --idempotent --project src/BillingRecon.Infrastructure --output migrate.sql
$env:SQLCMDPASSWORD = '<migrator password>'
sqlcmd -S <server> -d BillingRecon -U billingrecon_migrator -b -I -i migrate.sql
Remove-Item Env:SQLCMDPASSWORD
```

**3. As the administrator, once,** after the first run that applied `0020_DatabaseRoles`:

```sql
USE [BillingRecon];
ALTER ROLE [billingrecon_application] ADD MEMBER [billingrecon_app];
```

**4. Point the API at the application's login**, with `Database__ConnectionString`, for example
`Server=<server>;Database=BillingRecon;User Id=billingrecon_app;Password=<application password>;Encrypt=True`.
Outside Development the API never migrates (`Database:ApplyMigrationsOnStartup` is false there).

**5. Check**, connected as `billingrecon_app`:

```sql
SELECT IS_MEMBER(N'billingrecon_application') AS ApplicationRole, IS_MEMBER(N'db_owner') AS Owner;  -- 1 and 0
DELETE FROM [dbo].[Bills] WHERE 1 = 0;  -- Msg 229: The DELETE permission was denied on the object 'Bills'
```

Then `/health/ready` answers 200; without `SELECT` on `__EFMigrationsHistory`, which the role has, it never would.

**What the role may do** (migration `0020_DatabaseRoles`): read, add and change every table of `dbo`; never change or
delete `LedgerPostings`, `LedgerEntries` or `AuditRecords`; delete only from `BillLines`, `CreditNoteLines`,
`UserRoles` and `IdempotencyKeys` (the expiry sweep of section 7 too); only read `Roles` and `__EFMigrationsHistory`.
A later migration that adds an append-only or a deletable table adds its own line; `DatabasePermissionsOnSqlServerTests`
says which.

**Development** keeps the administrator's connection (`sa` on the compose SQL Server, the Windows login on LocalDB): the
API creates the database itself (with `READ_COMMITTED_SNAPSHOT` on), applies the migrations at startup, and the role
exists with no member. To try the logins locally, run steps 1 to 5 against the compose server with a database of your
own and start the API with that connection string.

**Changing a password:** `ALTER LOGIN [billingrecon_app] WITH PASSWORD = N'<new>';`, update the secret, restart the
API. **Removing the migrator's user** between releases is allowed: the role and its grants belong to `dbo` (ADR 0034,
point 5), so `DROP USER [billingrecon_migrator]` takes nothing with it; make it again before the next release.

---

## 11. Behind the proxy: TLS, hosts and origins (ADR 0035)

The API never holds a certificate. **A reverse proxy ends TLS** in front of it (an ingress, a load balancer, nginx) and
sends each request on over plain HTTP to the container's port 8080, saying in two headers what the client used:
`X-Forwarded-Proto` (`https`) and `X-Forwarded-For` (the client's address). Outside Development the API answers an
API request only when it arrived over HTTPS, so the proxy has to say so, and the API has to believe it.

**1. Tell the API where the proxy is.** The loopback addresses are always believed (a proxy on the same machine, or a
sidecar in the same pod). A proxy anywhere else is listed by address or range:

```bash
ReverseProxy__KnownProxies__0=10.0.0.5
ReverseProxy__KnownProxies__1=10.1.0.0/16
```

From any other address the two headers are ignored. A proxy that is not listed shows at once: every API request gets
400 `https_required`. Never set `ASPNETCORE_FORWARDEDHEADERS_ENABLED`: it believes the headers from anyone, and the API
refuses to start with it. One proxy deep: if a second proxy (a CDN) sits in front of the first, the address the limits
see is the first proxy's; that needs an amendment of ADR 0035, not a setting.

**2. Name the hosts the API answers for**, the names clients use, separated by semicolons:

```bash
AllowedHosts=api.cityhospital.example
```

A wildcard covers subdomains (`*.cityhospital.example`, not `cityhospital.example` itself). A request for any other
host gets 400 `host_not_allowed`. Outside Development the API does not start without it, nor with `*`. The health
endpoints are answered for any host, so a probe needs no name.

**3. Configure the proxy** to pass the original `Host` on, to set the two headers, and not to listen on plain HTTP for
the API (it may redirect browsers to the frontend's `https` address; the API never redirects). For nginx:

```nginx
location /api/ {
    proxy_pass http://billingrecon-api:8080;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_set_header X-Forwarded-For $remote_addr;
}
```

The API reads one entry of `X-Forwarded-For`, the last, which is the one the proxy adds; `$remote_addr` sends just that
one. Probes call `/health/live` and `/health/ready` on the container directly.

**4. Origins of the frontend.** When the proxy serves the frontend and the API from one origin (the plan of Prompt
8.1), set nothing. When the frontend lives on another origin, list it, as the browser sends it:

```bash
Cors__Origins__0=https://billing.cityhospital.example
```

No `*`, no path, no trailing slash, and `https` outside Development; the API refuses to start otherwise.

**5. Check**, from a machine outside the proxy:

```bash
curl -sS -D - -o /dev/null https://api.cityhospital.example/api/v1/system/info
```

The answer is 401 (no token), with `Strict-Transport-Security: max-age=31536000`, `Cache-Control: no-store`,
`X-Content-Type-Options: nosniff` and no `Server` header. A 400 `https_required` means the proxy is not believed
(step 1) or does not send `X-Forwarded-Proto`; a 400 `host_not_allowed` means step 2 lacks the name.

**Sign-in limits** count per client address, the one the proxy puts in `X-Forwarded-For` (section 9). A proxy that
sends the same address for everyone (a misconfigured header) makes all clients share one limit of 30 sign-ins a
minute.

---

## 12. The frontend (ADR 0038)

`frontend/` is the React app: TypeScript, Vite, React Router, TanStack Query and a client typed from the API's OpenAPI
document. It needs **Node 22.22 or later** (`engines` in `package.json`; CI uses Node 24). Every command runs in
`frontend/`.

| Command | What it does |
|---|---|
| `npm ci` | Installs exactly the locked packages (`package-lock.json`). Once per clone, and after a change of the lock file |
| `npm run dev` | The dev server at http://localhost:5173, which forwards `/api` to the API at http://localhost:5080 (another address: `BILLINGRECON_API_URL`) |
| `npm test` | The tests (Vitest, jsdom, React Testing Library) |
| `npm run lint` | ESLint, warnings as errors: TypeScript with types, hooks, accessibility, no raw HTML |
| `npm run format:check` | Prettier's check, the format gate; `npm run format` writes the fixes |
| `npm run typecheck` | TypeScript alone |
| `npm run build` | TypeScript, then the static files in `frontend/dist/` |
| `npm run api:types` | Generates `src/api/schema.d.ts` from `src/api/openapi.json` |
| `npm run budgets` | After a build: what a first visit to the sign-in page loads, and each route's chunk, compressed, against their budgets (ADR 0050, amended); CI runs it |
| `npm audit` | The advisories of the installed packages; CI fails on any |

**Sign in locally.** Run the API in Development (README, "Getting started") and `npm run dev`. The development user
has a password nothing matches (ADR 0030), so make a user first: in Development a request without a token acts as the
development user, a TenantAdmin.

```powershell
$body = '{"email":"admin@example.org","displayName":"Admin","password":"choose-twelve-or-more","roles":["TenantAdmin"]}'
Invoke-RestMethod -Method Post -Uri http://localhost:5080/api/v1/users -ContentType 'application/json' -Body $body
```

Then sign in at http://localhost:5173 with the hospital code `development`, that address and that password. A user with
other roles sees the sections of the permission matrix those roles may open.

**When an endpoint changes,** `OpenApiSnapshotTests` fails: `frontend/src/api/openapi.json` is no longer the API's
document. Rewrite it from the API and regenerate the types, then commit both with the change:

```powershell
$env:BILLINGRECON_WRITE_OPENAPI_SNAPSHOT = '1'; dotnet test tests/BillingRecon.IntegrationTests --filter "FullyQualifiedName~OpenApiSnapshotTests"; Remove-Item Env:BILLINGRECON_WRITE_OPENAPI_SNAPSHOT
```

```powershell
npm --prefix frontend run api:types
```

The frontend's CI job fails while `schema.d.ts` differs from what `npm run api:types` writes.

**What the build is given** (ADR 0047). The frontend is static files, so its one setting is fixed when it is built:
`VITE_SUPPORT_EMAIL`, the address "Contact support" on the sign-in page writes to. With the one-command run it is
`SUPPORT_EMAIL` in `.env`, passed to the frontend image as a build argument (`docker compose up --build` after a change);
locally, set it in the shell before `npm run dev` or `npm run build`. Empty, or not an e-mail address, leaves the divider
and the link out.

**When `npm audit` fails** in CI or locally: update the package that brings the advisory in to a version that fixes it
(`npm install --save-dev <package>@<version>` for a tool, without `--save-dev` for the app), and say so in the commit,
as for NuGet (section 5). The audit stays on.

**Where the browser keeps what** (ADR 0038 point 9): the session (the token, its expiry, the user's roles) in
`sessionStorage` under `billingrecon.session`, gone when the tab closes or the user signs out; the theme
(`billingrecon.theme`) and the last hospital code (`billingrecon.hospital`) in `localStorage`. A user who reports being
signed out after an hour has met the token's 60 minutes (ADR 0031): there is no refresh.

---

## 13. The one-command local run (ADR 0045)

`docker-compose.yml` runs the whole application on one machine with Docker: SQL Server and Seq; **`migrate`**, which
makes the database and the two logins of section 10, applies every migration as `billingrecon_migrator` and puts
`billingrecon_app` in its role, then stops; **`api`** outside Development, as `billingrecon_app`, not published;
**`frontend`**, nginx with the app, TLS with a local certificate and `/api` passed to the API as its one known proxy
(section 11); and **`demo-seed`**, which fills the demo hospital when it is asked for.

1. Copy `.env.example` to `.env` and fill it in: the two licence acceptances, the `sa` password, the passwords of
   `billingrecon_migrator` and `billingrecon_app`, `JWT_SIGNING_KEY` (base64 of 32 random bytes; `.env.example` says how),
   and for the demo `DEMO_ENABLED=true` with `DEMO_PASSWORD` (12 to 128 characters).
2. From the repository root:

   ```powershell
   docker compose up --build
   ```

3. Open **https://localhost**. The certificate is made on the first start and signed by nobody: the browser warns once;
   accept it for localhost. With the demo, sign in with the hospital code `demo`, `DEMO_PASSWORD` and one of
   `admin@demo.local`, `supervisor@demo.local`, `clerk@demo.local`, `cashier@demo.local`, `viewer@demo.local`. Without
   it, there is no hospital to sign in to: no API makes one (ADR 0045).

**What the demo holds:** five users, one per role; a catalogue of five items; six patients with their own payers; two
bills a day for the last seven days, most of them paid in full or in part; an overpayment, and its refund waiting for a
checker in Approvals. The seed signs in and enters all of it through the API, so the numbers, the ledger and the audit
trail are the API's own; a second `up` finds the patients and does nothing.

**Again and over:** `docker compose up` again keeps everything (the volumes). `docker compose down -v` deletes the
database, Seq's data and the certificate; the next `up` starts from nothing. A changed `.env` password of a login is set
by the next `migrate`; a changed `DEMO_PASSWORD` changes nothing for users that exist.

**Ports:** nginx listens on 443 and 80 of the loopback address (`HTTPS_HOST_PORT`, `HTTP_HOST_PORT` in `.env`). On other
ports, open `https://localhost:<port>` yourself: the redirect from plain HTTP keeps 443. The API answers only through
nginx; Seq stays at http://localhost:5341.

**When `migrate` fails:** `docker compose logs migrate` shows sqlcmd's message. The migration script needs
`QUOTED_IDENTIFIER` on (`sqlcmd -I`, as section 10 has it): without it the filtered indexes are refused.

## 14. The load test and the browser journey (ADR 0046)

Both run against the one-command local run of section 13 **with the demo** (`DEMO_ENABLED=true` and `DEMO_PASSWORD` in
`.env`), and both leave what they enter in the demo hospital: `docker compose down -v` gives a fresh demo afterwards
(it deletes the database, Seq's data and the certificate).

### The load test (`deploy/load/load.mjs`)

What it does and judges: `docs/testing-strategy.md` section 5b. It needs room: about **50 kB of data per bill** (more
than half of it the audit trail's images), and the transaction log grows with it, so leave **at least 2 GB free** on the
disk that holds Docker's data for the default run (10,000 bills of the fill, two minutes judged, one minute of stress).
On the office machine a full run filled the disk on 2026-11-24 (ADR 0046, built note).

1. Start the stack with the timings of the number series switched on (**for a load test only**: `SERVER_TIMING` tells a
   signed-in user how busy their hospital's series is, `docs/security.md` row I14):

   ```powershell
   $env:SERVER_TIMING = 'true'; docker compose up -d --build
   ```

2. Run the load (about ten minutes; it waits out the sign-in limit of section 9 once, about a minute):

   ```powershell
   docker compose --profile load run --rm load
   ```

   The last lines are the verdict, one row per target, and the exit code is 0 when every target is met, 1 when one is
   missed, 2 when the run could not be made. Send the whole output back.

3. Switch the timings off again:

   ```powershell
   Remove-Item Env:SERVER_TIMING; docker compose up -d
   ```

Settings, in `.env` or the shell: `LOAD_USERS` (50), `LOAD_FILL_BILLS` (10000), `LOAD_SECONDS` (120), `LOAD_THINK_MS`
(1000, the pause between one user's requests while judged) and `LOAD_STRESS_SECONDS` (60, no pause, not judged; 0 leaves
it out). A hospital whose approval threshold is 700 or less is refused: every bill of the load would wait for a checker.

Without Docker, against an API on this machine reached without the proxy, the script runs with `node`, `BASE_URL`
(for example `http://localhost:5090`) and `LOAD_FORWARDED_HTTPS=true`, which makes a plain-HTTP request on loopback stand
for one that came over HTTPS (the API believes the loopback, section 11); that is how the office machine ran it.

### The browser journey (`frontend/e2e/journey.spec.ts`)

Ten files: `journey.spec.ts`, the main journey, which needs the API and the demo; since Prompt 9.2 `shell.spec.ts`, the
shell and the patients list with the API's answers given by the test; since Prompt 9.3 `money.spec.ts`, the bill editor, a
posted bill, a credit note and an adjustment the same way (a request it did not expect fails it); since Prompt 9.3b
`cashier.spec.ts`, the payment, allocating, a refund, the statement and the inbox, with the helper both share,
`e2e/stubbed.ts`; since Prompt 9.4 `dashboard.spec.ts`, the dashboard and its tooltips on the same helper; since Prompt 9.4b
`routes.spec.ts`, every route in both themes at two widths with axe's WCAG 2.2 AA checks (its screenshots in
`test-results`), and `print.spec.ts`, print, forced colours and the keyboard; since Prompt 10.2b `search.spec.ts`, a list
answering while its search is typed and a picker as an autocomplete by the keyboard and by a tap, with axe; since Prompt 10.2c `signout.spec.ts`, a sign-out telling the API and
ending the same user's session in a second tab; and, since Prompt 9.1, `signin.spec.ts`,
the sign-in page alone (its layout against `docs/ui/sign-in.png`, nginx's content security policy applied in the test,
reduced motion, the keyboard, narrow windows), which needs no API but does need the **built** app: the one-command run,
or `npm run build` and `npm run preview` with `E2E_BASE_URL=http://localhost:4173` (the Vite dev server's own inline
scripts are rightly refused by the policy). `E2E_SCREENSHOTS=1` also writes the page's screenshots into `docs/ui/`.

Node 22.22 or later on the machine, the stack up with the demo, then from `frontend/`:

```powershell
npm ci
```

```powershell
npx playwright install chromium
```

```powershell
$env:E2E_PASSWORD = '<DEMO_PASSWORD of .env>'; npm run e2e
```

`npx playwright install chromium` downloads Chromium (about 150 MB) once; `$env:E2E_BROWSER_CHANNEL = 'msedge'` (or
`chrome`) uses the installed browser instead. `E2E_BASE_URL` points the suite elsewhere than https://localhost, for
example the Vite dev server (`http://localhost:5173`) with the API in Development. A failure leaves its trace in
`frontend/test-results` (`npx playwright show-trace <file>`) and a report in `frontend/playwright-report`; both are
ignored by git.

## 15. Backup and restore (Prompt 8.3)

The database is the record: every bill, payment, ledger entry and audit record is in it, and nothing can be rebuilt
from elsewhere. Back it up before every deployment of migrations and on a schedule; regular restore drills are R5
(`docs/requirements.md` section 5). Both scripts run as the administrator with sqlcmd and take their values as sqlcmd
variables (`-v name=value`, or environment variables of the same names, which avoids sqlcmd's trouble with the colon of
a Windows drive letter). Checked on LocalDB 2019 on 2026-11-24; the Docker forms below are not run yet (section 8,
row 8.3).

- `deploy/backup/backup.sql` (`Database`, `BackupFile`): a full backup `WITH CHECKSUM`, then `RESTORE VERIFYONLY`.
- `deploy/backup/restore-check.sql` (`Database`, `BackupFile`, `Target`, `DataDir`): restores the backup **under another
  name**, runs `DBCC CHECKDB`, and prints the migrations and the counts of bills, payments, postings and audit records.
  The live database is not touched; drop the target afterwards.

**The recovery model.** A database made with a plain `CREATE DATABASE` takes the model database's: `FULL` on SQL Server
2022 (the compose image), `SIMPLE` on LocalDB. Check with
`SELECT recovery_model_desc FROM sys.databases WHERE name = N'BillingRecon'`. In `FULL` the transaction log keeps
everything until a log backup, so a deployment takes log backups too (`BACKUP LOG [BillingRecon] TO DISK = ...`, every
15 minutes, say), which also allows a restore to a point in time (`RESTORE DATABASE ... WITH NORECOVERY`, then each
`RESTORE LOG ... WITH NORECOVERY`, the last `WITH STOPAT = '<instant>', RECOVERY`). Without log backups the log grows
until the disk is full (section 16.5).

**With the one-command run** (section 13), from the repository root. The backup is written inside the `sql` container,
in the `sql-data` volume, which `docker compose down -v` deletes, so it is copied out at once:

```powershell
docker compose cp deploy/backup/backup.sql sql:/tmp/backup.sql
```

```powershell
docker compose exec sql bash -c 'mkdir -p /var/opt/mssql/backup && SQLCMDPASSWORD=$MSSQL_SA_PASSWORD /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -i /tmp/backup.sql -v Database=BillingRecon BackupFile=/var/opt/mssql/backup/BillingRecon.bak'
```

```powershell
docker compose cp sql:/var/opt/mssql/backup/BillingRecon.bak ./BillingRecon.bak
```

The check of that backup, inside the container (then drop `BillingRecon_Restored`):

```powershell
docker compose cp deploy/backup/restore-check.sql sql:/tmp/restore-check.sql
```

```powershell
docker compose exec sql bash -c 'SQLCMDPASSWORD=$MSSQL_SA_PASSWORD /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -W -i /tmp/restore-check.sql -v Database=BillingRecon BackupFile=/var/opt/mssql/backup/BillingRecon.bak Target=BillingRecon_Restored DataDir=/var/opt/mssql/data'
```

**A real restore** (the database is lost or wrong): stop the API (`docker compose stop api`), then as the administrator
`RESTORE DATABASE [BillingRecon] FROM DISK = N'<file>' WITH CHECKSUM, REPLACE` (plus the log backups, as above). On the
same server the users of the two logins still match them; on another server, map them again
(`ALTER USER [billingrecon_app] WITH LOGIN = [billingrecon_app]`, and the same for the migrator). Start the API, and
before anyone works: `/health/ready` answers 200 (no pending migration), and the reconciliation report
(`GET /api/v1/reports/reconciliation`, any role) is empty. Everything after the backup is lost and must be entered
again; a payment taken meanwhile is entered as new, with a new number.

## 16. Incidents (Prompt 8.3)

Each incident is written down: when it was seen, by whom, what was done, and the correlation ids involved. A correction
of financial data is always a new document of the application (a reversal, a credit note, an adjustment), never a
statement run by hand (CLAUDE.md section 4).

### 16.1 Reconciliation drift (Sev-1)

The reconciliation report has a row, or its error is in the log (`docs/database.md` section 5.6): a cached balance
disagrees with the ledger, or the ledger or a series breaks an identity. That is a defect of the code, never a matter of
data entry.

1. **Stop postings for that hospital.** Suspend it with the operator's command (section 17.2; its `Code` is what users
   type at sign-in), with the incident as the reason. Its sign-ins are refused at once, and a token issued before is
   refused within 30 seconds (ADR 0052), so no key needs changing.
2. **Capture**, before anything changes: the report's rows (`identity`, `subjectId`, `reference`, `cached`, `ledger`);
   the document and its postings (`LedgerPostings` and `LedgerEntries` by `DocumentId`, `SubjectId` or `PayerId`); its
   audit trail (`GET /api/v1/audit-records?entityType=...&entityId=...`), whose records carry the correlation ids; and the
   log events of those correlation ids in Seq.
3. **Never correct a cache by hand.** Find the code path that wrote the wrong figure from the captured requests, fix it,
   and prove the fix with a test that reproduces the drift (`DatabaseTest` runs the reconciliation after every test).
4. A cache is rebuilt from the ledger only by a migration with one hand-written, idempotent data step (ADR 0021),
   approved by the owner, so that it is reviewed, versioned and run like any other change.
5. When the report is empty again, lift the suspension (section 17.2).

### 16.2 A gap in a number series

The reconciliation reports `NumberSeriesGap`: a series gave more numbers than there are documents with them (BR-801).
The design makes this impossible (the number is taken in the transaction of the posting and rolls back with it, ADR
0008), so it is handled as 16.1. Never renumber a document, never fill a gap with a made-up document, and never move
`NextNumber` back: a number given out stays given out. The incident record explains the gap to the auditor.

### 16.3 A request that waits for a checker

Every maker-checker document waits until a second person with Supervisor or TenantAdmin decides (BR-603, ADR 0032). The
pending approvals report (`GET /api/v1/reports/pending-approvals`, with `waitingDays`) and the checker's inbox
(`/approvals`) show them, the oldest first. A checker approves, or rejects with a comment, which returns the document to
its maker; the maker can never decide their own request. If the hospital has no second user who may decide, its
TenantAdmin gives one the role (`PUT /api/v1/users/{id}/roles`). Nothing is decided in the database by hand.

### 16.4 Idempotency keys

A key stuck in progress, and the clean-up of expired keys: section 7.

### 16.5 The disk is full

Seen on 2026-11-24 during a load run (ADR 0046, built note): SQL Server cannot grow a file, the log says "Could not
allocate space for object", and every request that writes answers 500 `internal_error`. Each of them rolls back whole:
nothing half-posted is left, and that run's reconciliation was empty. Free or add space (the transaction log of a `FULL`
database is the usual culprit: take a log backup, section 15), check `/health/ready`, then run the reconciliation report
before the hospital goes on. A client retries its requests with the same `Idempotency-Key`; a fault frees the key
(ADR 0006).

## 17. Onboarding and suspending a hospital (ADR 0052)

A hospital exists only when the operator makes it, on the server, with the API's own image and configuration: there is
no endpoint for it, so nothing reachable from the internet can create a tenant. The commands connect as the API does
(`billingrecon_app`, section 10), write one transaction each, and are audited with no user. They are refused in the
`Demo` environment, where the demo makes its own hospital; and the demo is refused in every environment but `Demo` and
`Development` (`Demo:Enabled` stops the API at startup elsewhere).

With Docker Compose (section 13), set `API_ENVIRONMENT=Production` and `DEMO_ENABLED=false` in `.env` first
(`docker compose up -d` again so the running API takes them). On a host without Compose, run the published API with the
same arguments: `dotnet BillingRecon.Api.dll tenant ...`, with the production configuration of the API.

### 17.1 Onboard a hospital

1. Agree the code with the hospital: lower-case letters and digits with single hyphens, at most 20 characters, which every
   user types at sign-in. Agree the settings (BR-607) and, if they differ from `BIL`, `CN`, `PAY`, `ADJ`, `REF`, the
   prefixes (BR-802; 1 to 3 capital letters or digits, one per document type).
2. Run:

   ```powershell
   docker compose run --rm api tenant create --code city-hospital --name "City Hospital" --admin-email admin@city.example --admin-name "A. Rao" --backdate-days 7 --due-days 15 --threshold 100000 --prefixes "Bill=INV,CreditNote=CRN"
   ```

   `--threshold none` (or leaving it out) means no bill needs approval. The command prints the hospital's id, its
   administrator's id and a one-time password of 20 characters, once; it is stored nowhere and logged nowhere. A refusal
   prints its code (`tenant_invalid` with the option at fault, `tenant_code_taken`) and exits with 1; nothing is written.
3. Give the password to the administrator by a channel other than the e-mail address it signs in with (a call, in person).
   At the first sign-in they must choose their own (ADR 0051) before any screen opens.
4. Check: the administrator signs in at the hospital's code, and the audit trail of the tenant
   (`GET /api/v1/audit-records?entityType=Tenant&entityId=<id>`, as that administrator) begins with `Onboarded`.
5. If the administrator loses the one-time password before using it, a second administrator of the hospital resets it
   (ADR 0051); for the first and only one, there is no command yet: record it as an incident.

### 17.2 Suspend a hospital, and lift the suspension

A suspended hospital keeps everything; nobody of it signs in, and every request with a token issued before is answered
403 `tenant_suspended` within 30 seconds (each instance of the API keeps a hospital's status that long).

```powershell
docker compose run --rm api tenant suspend --code city-hospital --reason "Contract ended 2026-12-31"
```

```powershell
docker compose run --rm api tenant resume --code city-hospital --reason "Contract renewed"
```

The reason is required and is kept in the audit record (`Suspended`, `Resumed`). Suspending a suspended hospital, or
lifting the suspension of an active one, is refused with `tenant_status_unchanged` and changes nothing; an unknown code is
`tenant_not_found`.

## 18. Bringing in a hospital's records and opening balances (ADR 0053)

Done once, on the day a hospital starts here: its patients and guarantors, then what they owed and what they had paid
in advance at the close of the day before the **cut-over date**, the first day on which documents are raised here.
First on staging with the whole export, then on production at the cut-over. The endpoints are in `docs/api.md`
section 8.17. The export of the reference hospital, in these layouts, is `deploy/reference/export/`
(`docs/reference-hospital.md`; `node deploy/reference/generate-export.mjs` writes it, `--check` compares it).

**Never put a real hospital's export in git, in a ticket or in a chat.** It holds patients' names and what they owe.
Keep it on the machine that sends it and delete it there when the batch is approved.

### 18.1 The files

CSV, UTF-8, a comma between fields, the first line the header, double quotes around a field that holds a comma. Dates
are `yyyy-MM-dd`; amounts are plain numbers with at most two decimals (`1250.50`, never `1,250.50`). Columns may be in
any order; a column the layout does not name is ignored. Map the old system's export to these before anything else.

| File | Required columns | Optional columns |
|---|---|---|
| `patients.csv` | `mrn`, `full_name` | `date_of_birth`, `sex` (`Male`, `Female`, `Other`), `phone`, `email`, `address_line1`, `address_line2`, `city`, `state`, `pin_code` |
| `guarantors.csv` | `guarantor_ref`, `name` | `phone`, `email` |
| `bills.csv` | `legacy_number`, `mrn`, `bill_date`, `outstanding_amount` | `guarantor_ref` (who owes it, when not the patient), `due_date` (the bill date when left out) |
| `advances.csv` | `legacy_reference`, `received_date`, `amount`, `method` (`Cash`, `Card`, `UPI`, `BankTransfer`, `Cheque`), and one of `mrn` and `guarantor_ref` | `kind` (`Advance`, or `Credit` for a credit balance) |
| `trial_balance.csv` | `account` (`Receivable`, `Advance`, `Credit`), `amount` | |

`outstanding_amount` is what is still owed on the bill, not its original total. One patients' file holds at most 5,000
rows and one batch at most 2,000 opening balances: split a larger hospital, each part of the balances with a trial
balance of its own.

### 18.2 The procedure

1. **Agree the cut-over date** with the hospital, and that nothing is entered in the old system from that day on.
2. **Patients and guarantors, dry run.** As a TenantAdmin, `POST /api/v1/imports/parties` with `dryRun: true`. Read
   `rejected`: each finding names its file, line and reason. A patient the hospital has already under the same MRN and
   name is *matched*; under another name it is rejected, and you decide which is right. Correct the files, not the
   database.
3. **Patients and guarantors, for real.** The same request without `dryRun`. `written: true` and the counts. One
   rejected row writes nothing. Sent again, every row is matched and nothing changes.
4. **Opening balances, dry run.** `POST /api/v1/opening-batches/dry-run` with the cut-over date and the three files.
   `accepted: true` only when no row is rejected and the three totals equal the trial balance to the paisa. A
   difference is in the files or in the old system's ledger: find it there.
5. **Submit.** `POST /api/v1/opening-batches`, with an `Idempotency-Key`. The batch waits; nothing is posted.
6. **A second person approves** (a Supervisor or another TenantAdmin): the approvals inbox shows the batch with its
   counts and totals, or `POST /api/v1/opening-batches/{id}/approve` with `If-Match`. They compare the totals with the
   old system's closing trial balance, signed by the hospital's accounts, before approving. The approval posts
   everything in one transaction; a patient deactivated meanwhile refuses it with nothing written.
7. **Check.** `GET /api/v1/reports/reconciliation` shows no drift; the outstanding-by-payer report's totals are the
   trial balance; pick ten payers with the hospital and compare each one's statement with the old system.
8. **Keep** the approved batch's id and the hospital's signed trial balance with the go-live record.

The same files cannot be brought in twice (409 `opening_batch_duplicate`), nor a bill or advance whose old number was
brought in before. A batch that is wrong is **rejected** before approval and submitted again when right. After
approval nothing is undone: a wrong opening receivable is corrected with a credit note or an adjustment, a wrong
opening advance is refunded with the mistake as its reason, each under maker-checker like any other.

PowerShell, with a token of a TenantAdmin in `$token` (section 12 for signing in) and the files in the current folder:

```powershell
$body = @{ patientsCsv = (Get-Content patients.csv -Raw); guarantorsCsv = (Get-Content guarantors.csv -Raw); dryRun = $true } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri https://localhost/api/v1/imports/parties -ContentType 'application/json; charset=utf-8' -Headers @{ Authorization = "Bearer $token" } -Body ([Text.Encoding]::UTF8.GetBytes($body))
```
