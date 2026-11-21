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

## 8. Deferred verification: the running list for the personal machine

On 2026-11-20 the owner decided to go on building and to run the verification on the personal machine later.
Until this list has been run and its output recorded, **nothing after Prompt 2.1 has been built on .NET 10, run on
SQL Server 2022, or had its migration scaffolded with EF Core 10**; every commit made in the meantime says so in
its message. This is the one list of what is owed. A prompt that adds a migration or a check adds its line here,
in order; nothing is removed until the owner has run it and sent the output.

**B and E done on 2026-11-21, in the repository: migrations `0003` to `0015` are committed ("Migrations 0003 to 0015,
scaffolded with EF Core 10"), each scaffolded on its own with EF Core 10.0.12 as B describes, and EF Core reports no
pending model change. Do not run B again.** A, C and D are still owed on a machine with Docker.

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
```

**E. Send back**

- the output of A, C and D, and the names of the migration files B created;
- the migration files themselves (`Common/Migrations/*_0003_*` to `*_0015_*` and the updated
  `BillingReconDbContextModelSnapshot.cs`), which are then committed as "Migrations 0003 to 0015, scaffolded with
  EF Core 10".

**What that run has to show, because nothing here could** (the office machine has .NET 9 and LocalDB 2019 only):

| Prompt | Not proven anywhere yet |
|---|---|
| 2.2 | The run itself; then 2.2-close (CLAUDE.md section 9, item 9). |
| 2.3 | The `LIKE` search on the service item code translates on EF Core 10. |
| 2.4 | Ten simultaneous changes of the lines of one bill, on SQL Server 2022. |
| 3.1 | Fifty parallel takers of a number series, on SQL Server 2022. |
| 3.2 | The response buffering of the idempotency filter on ASP.NET Core 10. |
| 3.4 | The before and after images of `EntitySnapshot` on EF Core 10. |
| 3.3 | The typed-id foreign key convention on EF Core 10 (migrations `0003` to `0007` must hold the same keys as before it; `0008` the four new ones). The locked change of `PayerBalances` and twenty simultaneous postings for one payer, on SQL Server 2022. The computed column `NetOutstanding`. The reconciliation statement. |
| 3.5 | That `BillConfiguration` can leave five properties out of the model and `BillPostingConfiguration` put them back, on EF Core 10 (the split of `0004` and `0009` depends on it). Twenty bills posted at once, two posts of one bill at once, and a posting held open while its key is sent again, on SQL Server 2022. The failure injected at each step of a posting. |
| 3.6 | Two aggregates, the bill and its approval request, saved and audited by one commit on EF Core 10. Two checkers deciding at once, and an approval that fails after the number is taken, on SQL Server 2022. The 202 answer and the optional body of `approve` on ASP.NET Core 10. |
| 4.1 | Twenty payments recorded at once for one payer, and a recording held open while its key is sent again, on SQL Server 2022. The failure injected at the number, the ledger, the audit record and the save, there. The 201 answer of an idempotent POST, replayed with its `Location` and `ETag`, on ASP.NET Core 10. |
| 4.2 | That `PaymentConfiguration` can take the allocations out of the model and `PaymentWithAllocationsConfiguration` put them back, on EF Core 10 (the split of `0011` and `0012` depends on it). Two cashiers on the last rupee of one bill, with and without the gate, and ten payments allocated at once across overlapping bills of one payer, on SQL Server 2022. The refusal of the ledger under its lock, there. The failure injected at the ledger, the audit record and the save. |
| 4.3 | Three aggregates (the credit note, its bill and the approval request) saved and audited by one commit on EF Core 10, and a credit note loaded with two collections by a split query there. Two credit notes approved at once against a paid bill that only one fits, on SQL Server 2022: this is the test that proves the caps are read again under the lock. The failure injected after the second ledger posting and after the audit record. |

A red result is reported as it is, with its output; the fix is its own commit on top, never an edit of history.
