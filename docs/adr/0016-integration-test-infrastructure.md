# ADR 0016: Integration test infrastructure: one SQL Server per test run, two databases, Respawn, a test identity

- **Status:** Accepted (owner's approval of the Prompt 1.4 plan, 2026-10-17). Written on the office
  machine, which has no Docker: the database tests were not run before the owner's machine did
  (see "Verification" under Consequences).
- **Date:** 2026-10-17
- **Deciders:** project owner, Claude Code
- **Refines:** ADR 0009, ADR 0015 (what their tests run against); CLAUDE.md section 2 (the test tools).

## Context

The persistence foundation (ADR 0015) was tested without a database: the model, the SQL Entity
Framework generates and the change tracker. Four things cannot be reached that way and were only
checked by hand on LocalDB: the tenant predicate in UPDATE and DELETE statements, the row version
the engine assigns, the CHECK constraints and the unique index, and readiness against a database
with a pending migration. The blueprint's Prompt 1.4 asks for the infrastructure that reaches them:
a real SQL Server for the test run, data reset between tests, the API hosted against it, a way to
act for a tenant and user before authentication exists, a controllable clock, and realistic data.

Two machine facts shape the design. The office machine, where code is written, has no Docker and
no .NET 10 SDK; the owner's personal machine has both and is where `dotnet test` runs for real.
And the repository bans the EF Core in-memory provider (CLAUDE.md section 2): it enforces no
constraint, no transaction and no row version, which is exactly what these tests are about.

## Decision

### One container per test run, the image of docker-compose.yml

`SqlServerFixture` starts one SQL Server 2022 container with Testcontainers.MsSql for the whole
test run, shared by every class of the xUnit collection `Database` through a collection fixture.
The image tag is read from `docker-compose.yml` at run time, so the tests and local development
can never drift apart. The container accepts the SQL Server licence terms the way the MsSql module
does; they are the terms the owner accepted in `.env` for the same image.

Tests of the collection run one after another, the other collections in parallel with them. The
container is removed by Testcontainers when the run ends.

### Two databases

- **`BillingRecon_Api`** is created by the migrations, exactly as the real database is: the fixture
  runs `MigrateAsync` once with the production context and the system identity. The API under test
  is pointed at it, with `Database:ApplyMigrationsOnStartup` off, as it is outside Development.
- **`BillingRecon_Probe`** is created from the *test* model with `EnsureCreated`: the production
  tables plus the probe entities (`Widget`, `LogLine`, `Setting`) that exist only in tests, because
  the production model has no tenant-scoped table yet. The isolation, audit, row-version and typed-id
  tests run here. The probe schema is not managed by migrations on purpose; it is not a product
  schema. When real tenant-scoped tables exist, the same tests can move to them and the probe
  database can go.

A test that needs a database in another state (readiness with a pending migration) creates its own
empty one on the same container.

### Empty tables before every test

Respawn deletes the data of every table of both databases before each test, in dependency order,
and leaves the schema and `__EFMigrationsHistory` alone (and `Roles`, once that reference table
exists). Nothing is rolled back through a wrapping transaction: the tests commit for real, which is
the only way to see what the engine does with row versions and constraints.

### The API against the container, acting for a tenant

`DatabaseApiFactory` is the existing `ApiFactory` (Production pipeline) with the container's
connection string, a `FakeClock` in place of the system clock, and `TestRequestIdentity` in place of
the identity the API registers outside Development (`UnavailableIdentity`, which throws).
`TestRequestIdentity` reads the tenant, user and roles that `TestPrincipalStartupFilter` puts on the
request from the `X-Test-Tenant`, `X-Test-User` and `X-Test-Roles` headers; `CreateClientFor`
sets those headers on a client. It stands in for what Phase 5 resolves from the validated token and
goes away with it. Outside a request, or for an anonymous one, it throws: nothing acts for a tenant
by accident, as in production.

*Superseded on 2026-11-21 by ADR 0031, point 10:* the stand-in is gone with the headers. The API
keeps its own identity, read from the token, and `CreateClientFor` gives a client a delegating
handler that puts on every request a bearer token minted for it by the API's own issuer, with the
factory's clock; so every test request goes through the whole validation of tokens.

### Test data

`TestData` holds one Bogus generator with a fixed seed (Indian locale), behind a lock because test
classes run in parallel. The same tenants and names come out in every run, so a failing test can
be reproduced; the values look like what the system will hold.

### Without Docker

The fixture fails on first use with one message that says what is needed; the tests of the
collection fail, every other test runs. Nothing is skipped silently, so a run without Docker is
visibly incomplete rather than quietly green.

## Alternatives

- **The EF Core in-memory provider.** Banned, and for cause. Three things it would have passed that
  SQL Server refuses: a lower-case number prefix (the CHECK with the binary collation does not
  exist there), two writers with the same stale row version (no `rowversion`, no
  `DbUpdateConcurrencyException`, the second write silently wins), and a forged update of another
  tenant's row (no `TenantId` predicate in a statement that does not exist). It also stores a
  `decimal` without rounding to the column's scale, so a `2.345` that SQL Server would round is
  kept, and the rounding rules of BR-001 would never be tested against the store.
- **SQLite in memory.** Fast, but a different engine: no `rowversion`, different collations, no
  `datetimeoffset`, different CHECK semantics. It would test a database the product never runs on.
- **LocalDB or a developer's own SQL Server.** No Docker needed, but a shared mutable state that
  differs per machine, and not SQL Server 2022 on the office machine. Kept for manual checks.
- **A transaction per test, rolled back at the end.** Faster resets, but row versions, identity
  values and the behaviour of a committed write are exactly what some tests look at, and the API
  under test opens its own connections.
- **A container per test class.** Simpler isolation, ten times the start-up cost. The collection
  with Respawn gives the same isolation at one container.
- **Probe tables through a migration of their own.** Would put test-only tables into the product's
  migration history. `EnsureCreated` from the test model keeps them out.
- **A fixed development tenant and user in the API tests** (the Development stub). Enough for one
  tenant, useless for isolation tests, which need two.

## Consequences

- `dotnet test` needs a running Docker engine for the Database collection; the first run on a
  machine pulls the image unless `docker compose up` already did. The run takes some tens of seconds
  longer for the container start.
- The office machine runs every test but the Database collection, which fails there with the
  fixture's message. The pre-check in the .NET 9 copy (CLAUDE.md section 2) therefore counts those
  failures as expected.
- The tests promised by ADR 0015 for this step exist: the tenant predicate in UPDATE and DELETE, the
  row version, the CHECK constraints and the unique index, the audit columns and the creation columns
  an update cannot touch, the tenant-row filter, readiness against pending and applied migrations.
- A later Users table brings the first real tenant-scoped entity; the isolation tests can then be
  written against it and the probe database retired.
- **Verification.** The solution and the tests compile in the .NET 9 pre-check copy and every test
  outside the Database collection passes there; the collection itself fails there without Docker, by
  design. The first real run of the collection is on the owner's machine (CLAUDE.md section 9).
