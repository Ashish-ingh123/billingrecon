# Testing Strategy

- **Status:** written 2026-10-17 with the test infrastructure (Prompt 1.4, ADR 0016). The financial integrity
  tests, the frontend tests and the CI gates are described as they are planned and are marked so.
- **Rules cited:** CLAUDE.md section 4 (the twelve rules) and section 6 (the working agreement); BR-nnn from
  `docs/requirements.md`.

---

## 1. The pyramid, and where each kind of rule is proven

| Layer | What it proves | Tools | Where |
|---|---|---|---|
| Domain unit | Invariants and business rules of aggregates and value objects, `Result`/`Error`, typed ids. Named after the rule they test where one exists (`BR-nnn`). | xUnit, Shouldly | `tests/BillingRecon.Domain.Tests` |
| Application unit | Use-case handlers with ports replaced by stubs (what they check, in which order, and that nothing is written when a check fails); validators, case by case, with the field each message is reported under; the conventions the registration by scanning relies on (one handler per message, a handler takes its message's validator exactly when one exists, ADR 0018). | xUnit, Shouldly, NSubstitute | `tests/BillingRecon.Application.Tests` |
| Persistence without a database | The EF Core model and its conventions, the interceptors on the change tracker, the SQL text of queries (`ToQueryString`). The SQL Server provider against a server nothing listens on; the in-memory provider is banned. | xUnit, Shouldly, EF Core | `tests/BillingRecon.IntegrationTests/Persistence` |
| Database | What only the engine can show: migrations applied to SQL Server 2022, CHECK constraints, unique and filtered indexes, foreign keys, the row version, the tenant predicate of UPDATE and DELETE, the audit columns as stored, readiness against pending and applied migrations, and what the unit of work makes of a lost race. Every tenant-scoped row needs its tenant's row first (the foreign key on `TenantId`): `NewApiTenantAsync`, `AddProbeTenantRowsAsync`. | Testcontainers.MsSql, Respawn, Bogus | `tests/BillingRecon.IntegrationTests/Database` (the `Database` collection) |
| API | The pipeline that ships: routing and versioning, the error contract, correlation id, logging and PII masking, health, OpenAPI. In-process host, Production environment unless a test says otherwise. The endpoints of a resource end to end (status and error codes, ETag and If-Match, paging and search, simultaneous requests, and tenant isolation with two tenants) run in the `Database` collection on `DatabaseApiFactory`. | WebApplicationFactory | `tests/BillingRecon.IntegrationTests` (root), `DatabaseApiFactory` for endpoints that need the database and an acting tenant |
| Architecture | The layer rule, the module rule with its listed exceptions, every type in a known module, banned packages in every project file, the banned-API list, controller conventions, no minimal APIs. | NetArchTest, file scans | `tests/BillingRecon.ArchitectureTests` |
| Financial integrity (Phase 4) | After scripted and random sequences of operations (seeded, several seeds), every identity of `docs/database.md` section 5.5 holds: cache equals ledger, gapless numbers, balanced postings. | xUnit, Bogus | with the receivables ledger |
| Frontend (Phase 8) | Components and the critical flows (payment and allocation form). | Vitest, Testing Library | `frontend/` |

A test belongs to the lowest layer that can prove the point. The model and the SQL text answer most
questions about persistence without a database; a test needs the database only when the engine's
behaviour is the question.

## 2. The database tests (ADR 0016)

- **One SQL Server per test run.** `SqlServerFixture` starts one container with Testcontainers,
  the image `docker-compose.yml` names, shared by every class of the `Database` collection. The
  collection's tests run one after another; the other collections run in parallel with them.
- **Two databases.** `BillingRecon_Api` is created by the migrations and is what the API under
  test is pointed at. `BillingRecon_Probe` is created from the test model (`EnsureCreated`) and holds
  the probe entities (`Widget`, `LogLine`, `Setting`) that exist only in tests, until real
  tenant-scoped tables exist.
- **Empty tables before every test.** Respawn deletes the data of both databases; the schema and
  `__EFMigrationsHistory` stay. Writes commit for real; nothing is rolled back behind the test's back.
- **Acting for a tenant.** A client made with `CreateClientFor` sends with every request a bearer
  token minted for it by the API's own issuer (ADR 0031), for the tenant, user and roles it names; a
  client that names no role is a TenantAdmin, like the user row behind it. A client made with
  `CreateClient` is anonymous and gets 401 everywhere but the sign-in and the health endpoints. A
  context built by hand takes a `FixedIdentity` (tenant and user) or `SystemIdentity.Instance` (none).
- **Time.** `FakeClock` everywhere; the API's clock is the factory's `Clock`, which a test moves.
- **Data.** `TestData` draws from one seeded Bogus generator: the same tenants and names every run.
- **Without Docker** the fixture fails with one message; the collection fails, every other test
  runs. Nothing is skipped silently. To run only the rest on such a machine:

  ```bash
  dotnet test --filter "FullyQualifiedName!~IntegrationTests.Database."
  ```

  (The trailing dot matters: without it the filter also drops `DatabaseStartupTests`, which needs no Docker.)

## 3. Why not the EF Core in-memory provider

It enforces nothing the financial rules depend on. Three things it would pass that SQL Server refuses:

1. A number prefix in lower case: the CHECK constraint with its binary collation does not exist in
   memory (`CK_Tenants_BillPrefix`, ADR 0008).
2. Two cashiers saving the same row from the same stale version: there is no `rowversion`, so no
   `DbUpdateConcurrencyException`, and the second write silently wins (BR-804).
3. A forged update of another tenant's row: there is no statement, so no `TenantId` predicate in it;
   the in-memory store would overwrite the row (BR-002, ADR 0015).

It also stores a `decimal` without the column's scale, so a value SQL Server would round or refuse is
kept as is (BR-001). A green run on it would say nothing about any of this.

## 4. Rules of the house

- **Never weaken a test** to make it pass: no deleting, skipping, loosening or widening (CLAUDE.md
  section 6). Change the code, or raise the disagreement with the owner and record the decision.
- **Name tests as sentences** that state the rule (`A_forged_update_of_another_tenants_row_changes_no_row`).
  Where a business rule has an id, the test cites it in its name or its summary.
- **One reason per test.** A test that would pass for two different reasons proves neither; split it.
- **No silent skips.** A test that cannot run on a machine fails there and says why.
- **Tests with every feature** (CLAUDE.md section 6): a feature without tests is not done, and a
  rule in `docs/requirements.md` without a test is an open item for Phase 7.

## 5. Running the tests

| Command | What runs |
|---|---|
| `dotnet test` | Everything. Needs a running Docker engine for the `Database` collection (the first run pulls the SQL Server image unless `docker compose up` already did). |
| `dotnet test --filter "FullyQualifiedName!~IntegrationTests.Database."` | Everything but the database tests, for a machine without Docker (the trailing dot keeps `DatabaseStartupTests` in). |
| `dotnet test tests/BillingRecon.ArchitectureTests` | The architecture rules alone; they read source files, so run from the repository. |

The office machine (CLAUDE.md section 2) cannot run the database tests at all; they run on the
owner's machine and in CI.

## 6. CI quality gates (Prompt 1.5, to come)

Build with warnings as errors; `dotnet format --verify-no-changes`; every test including the
`Database` collection (the hosted runners have Docker); a coverage report of the Domain and
Application unit tests as an artifact (a guide, not a goal; the integration tests run
uninstrumented, see ADR 0017); no vulnerable package; the idempotent migration script generates; the
API image builds and starts. Written in `.github/workflows/ci.yml` with Prompt 1.5; the gates and
their local equivalents are in `docs/runbook.md` section 5.
