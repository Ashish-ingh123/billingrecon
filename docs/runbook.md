# Runbook

- **Status:** sections 1 and 2 written 2026-10-03 with the infrastructure step, section 3 with the
  DbContext step, section 4 with the test infrastructure and sections 5 and 6 with the CI step
  (2026-10-17). Still to be written: rolling back a migration, deploying migrations outside
  Development, backup and restore, and incident procedures (balance drift versus the ledger,
  number-series recovery, stuck maker-checker approvals, idempotency store cleanup).

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
| `DevelopmentIdentity:TenantId`, `DevelopmentIdentity:UserId` | `appsettings.Development.json` | The fixed tenant and user every request acts for in Development, until authentication exists (Phase 5). Not secrets. Outside Development there is no identity at all, and code that asks for one fails. |
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
   `BillingRecon` database on first start and applies the migrations (one line says so).

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

12. **Record the result.** When steps 3, 7 and 10 pass, remove every "results still to be recorded" note.
    There are five: the "Record the container checklist results" item of "Open items" in `CLAUDE.md` §9; the
    words "(first run 2026-10-17; checklist results pending, see section 9)" in the project list of
    `CLAUDE.md` §3; the note at the top of `docker-compose.yml`; the last sentence of "Getting started" in
    `README.md`; and the first paragraph of this section. If something had
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
dotnet ef migrations add 0001_Tenants --project src/BillingRecon.Infrastructure --output-dir Common/Migrations
```

Then build and run the tests: three of them check that the first migration exists, that the model
has no change missing from the migrations, and that their script creates what `docs/database.md`
describes. (They fail as long as the repository has no migration.) Review the generated `Up` and
`Down` before committing; they should need no hand editing (ADR 0015).

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
with the migration login (`docs/database.md` section 7); the procedure is still to be written.

**Undo the last migration** that has not been applied anywhere yet:

```bash
dotnet ef migrations remove --project src/BillingRecon.Infrastructure
```

A migration that has been applied to a shared database is never removed or edited; it is corrected
by a new one.

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

`.github/workflows/ci.yml` runs on every push to `main` and every pull request (ADR 0017), as two jobs
on GitHub's Ubuntu runners, which have Docker. Every gate has a local equivalent:

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
deliberate decision (ADR 0014).

**Acceptance of the CI step** (CLAUDE.md section 9): the first green run once the repository is on
GitHub, and a pull request with one deliberately broken assertion that goes red and is closed unmerged.

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
docker run --rm -p 8080:8080 -e "Database__ConnectionString=Server=host.docker.internal,1433;Database=BillingRecon;User Id=sa;Password=<your-sa-password>;TrustServerCertificate=True" billingrecon-api
```

Then `http://localhost:8080/health/live` answers 200, and `/health/ready` answers 200 once the
database has every migration. The container runs as Production and never migrates: apply the script
of section 3, or let a Development run of the API do it first. Without `Database__ConnectionString`
the container stops at startup with the message that names the setting. The health check is visible
with `docker inspect --format "{{json .State.Health}}" <container>`.

The image tags follow the major version (`sdk:10.0`, `aspnet:10.0`), and `global.json` must accept the
SDK the build image carries. If a build fails on the SDK version, pass the exact tag:

```bash
docker build -f src/BillingRecon.Api/Dockerfile --build-arg SDK_TAG=10.0.402 -t billingrecon-api .
```
