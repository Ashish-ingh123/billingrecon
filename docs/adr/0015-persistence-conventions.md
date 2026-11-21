# ADR 0015: Persistence conventions: the EF Core context, typed ids, tenant isolation on reads and writes, audit columns, banned APIs

- **Status:** Accepted for the points the owner decided on 2026-10-03 (listed under Context). The points
  marked **(addition)** were decided while building the step, several of them after an independent
  review of the first version, and wait for the owner's review.
  Built and tested on .NET 10 on 2026-10-17 (see "Verification" under Consequences). The tenant-row rule in
  section 3 was added on 2026-10-17 after a review and waits for the owner's review like the other additions.
- **Date:** 2026-10-03
- **Deciders:** project owner, Claude Code
- **Refines:** ADR 0007 (rowversion), ADR 0009 (multi-tenancy), ADR 0010 (time). Those stay as written
  except where this ADR says otherwise.

## Context

This step adds the first persistence code: the EF Core context, the base types every entity builds
on, the `Tenants` table, and the guards that ADR 0009 and ADR 0010 describe. Whatever is decided
here is inherited by every table that follows, so it is recorded in one place.

The owner approved the plan with these decisions:

- Names as in ADR 0009: `ITenantContext`, `ITenantScoped`.
- Typed ids through `Entity<TId>`, converters registered by convention, with a test that an added
  entity gets a non-empty sequential id through the converter.
- Two audit interfaces, creation and modification; append-only tables implement creation only. This
  ADR records that, the change to `docs/database.md`, and what `CreatedByUserId` holds when there is
  no user.
- The first migration is `0001_Tenants`.
- Bans through `Microsoft.CodeAnalysis.BannedApiAnalyzers`, with one suppressed site in
  `SystemClock`. `IgnoreQueryFilters` is banned, with one sanctioned location named here.
- Readiness means: the application database is reachable and has no pending migrations.
- The tenant filter reads the tenant through a property of the context instance, never a value
  captured while the model is built.
- The save interceptor stamps `TenantId` on added entities only; on modified and deleted ones it
  requires the current tenant (an empty tenant is a violation) and rejects any change of tenant.
  `ExecuteUpdate` and `ExecuteDelete` skip interceptors, which this ADR has to deal with.
- `SystemClock` resolves India's time zone at startup and fails fast.
- The check for unsized decimal and text columns has an explicit opt-out for deliberately unbounded
  columns.

## Decision

### 1. One context that carries its own rules

`BillingReconDbContext` (`Infrastructure/Common/Persistence`) is the only context. A module adds a
table by adding an `IEntityTypeConfiguration` class in its own Infrastructure folder; the context has
no `DbSet` property and names no module's entity, and repositories use `Set<T>()`. The class is not
sealed, so that tests can derive a context that adds probe entities and still runs every convention.

**The context attaches the two save interceptors to itself (addition)**, in `OnConfiguring`, from the
tenant, user and clock it is constructed with. They are registered nowhere else. A context therefore
cannot exist without the tenant guard and the audit stamps, whether dependency injection created it,
the design-time factory, seeding code or a test. (In the first version the dependency-injection
set-up added them, and a context built by hand had no guard at all.)

An entity configuration states only what is particular to its table (lengths, precision, indexes,
table-specific CHECK constraints). The context applies the rest to every entity type:

| Convention | Effect |
|---|---|
| Typed ids | Every type that implements `ITypedId` is stored as `uniqueidentifier` (`TypedIdConverter`), nullable ones included. |
| `Money`, `UnitPrice`, `Quantity`, `Percent` | Stored as `decimal(18,2)`, `decimal(19,4)`, `decimal(9,3)` and `decimal(5,2)` through one converter each, nullable ones included; no configuration repeats the precision (added 2026-10-17 with ADR 0004's implementation). |
| Ids of `Entity<TId>` | Generated in the application when the entity is added (`TypedIdValueGenerator`, EF Core's sequential GUIDs, which increase in SQL Server's sort order). |
| `DateTimeOffset` | `datetimeoffset(3)`. |
| `DateOnly` | `date`. |
| Enums | Stored by name in a `varchar` column, with a CHECK constraint `CK_<Table>_<Column>` that lists the enum's members **(addition)**. Adding a member therefore produces a migration that replaces the constraint. |
| `IHasRowVersion` | `rowversion`, concurrency token (ADR 0007). |
| `ICreationAudited` | The two creation columns are never part of an UPDATE (section 4). |
| `AggregateRoot<TId>.DomainEvents` | Not mapped. |
| `ITenantScoped` | Query filter by the current tenant; `TenantId` is in the WHERE clause of every UPDATE and DELETE and can never be the target of an UPDATE (section 3). |
| Decimals and text | **No default.** A decimal without an explicit precision and scale fails the model. So does text without a maximum length: a string, an enum, a value object stored as either, or a collection stored as JSON text. An enum's column must also be long enough for its longest name. |

The last row is `ExplicitColumnSizeConvention`. It runs whenever the model is built, so the same
failure appears at startup (`DatabaseStartup` builds the model before the host starts anything
else, in every environment, without needing a database), in tests, and in the EF Core tools. The
opt-out is explicit: `.IsUnbounded()` on the property, for JSON documents and stored response
bodies. A property that is both unbounded and limited is rejected, and there is no opt-out for
decimals.

### 2. Base types and typed ids

In `BillingRecon.Domain.Common`: `Entity<TId>`, `AggregateRoot<TId>` (collects domain events),
`ITypedId`, `ITenantScoped`, `ICreationAudited`, `IModificationAudited`, `IHasRowVersion`, and the
ids every module needs, `TenantId` and `UserId`. A typed id is always
`readonly record struct XId(Guid Value) : ITypedId`; a test enforces the shape.

An id is **empty until the entity is added to the context**, and known from that moment on, before
anything is saved. A domain event must therefore not copy the id inside a factory method.

### 3. Tenant isolation

`ITenantContext.TenantId` is nullable. Null means a scope without a tenant: system work, which may
touch only tables that are not tenant-scoped.

- **Reads.** Every `ITenantScoped` entity gets the filter `TenantId == CurrentTenantId`, where
  `CurrentTenantId` is a property of the context that runs the query. EF Core evaluates it for each
  query, so nothing about the tenant is stored in the model, which is built once and shared. A query
  on a tenant-scoped entity in a scope without a tenant throws `TenantIsolationException`.
  The filter is a **named** filter (`"Tenant"`, an EF Core 10 feature) **(addition)**. A module that
  needs a filter of its own on a tenant-scoped entity adds another named filter and both apply; an
  unnamed one next to it fails the model, where before EF Core 10 the later of two filters silently
  replaced the earlier, in either order.
- **The tenant row itself (addition, 2026-10-17).** ADR 0009 says access to `Tenants` "is restricted to the
  current row", and the first version implemented nothing for it: a request-scoped context could read every
  tenant's settings and prefixes. Now the entity whose id is a `TenantId` gets, by convention, the named filter
  `EF.Parameter(TenantRowScope) == null || Id == CurrentTenant`: a tenant reads its own row and no other, and
  system work (no tenant in scope) reads every row. The null check is on a `Guid?` member of the context and
  goes through `EF.Parameter`, so it reaches SQL as a null test on a parameter, which EF Core resolves from
  the parameter's value when it compiles the query: for system work the predicate is absent from the SQL,
  for a tenant it is `[Id] = @tenant`. (Written as a plain `== null`, the comparison is evaluated in memory
  into a bit parameter and the SQL keeps `@p = 1 OR [Id] = @tenant`; verified on EF Core 9, the same
  funcletizer as 10.) On the write side `TenantWriteGuardInterceptor`
  refuses an update or delete of a tenant row whose id is not the current tenant; system work may write any
  tenant row, which is how tenants are onboarded, and an added tenant is not an isolation question (who may
  onboard is authorisation). The row needs no concurrency token of its own: its id *is* the tenant, so the
  key predicate of every UPDATE and DELETE already names it.
- **Writes.** `TenantWriteGuardInterceptor` inspects what a save is about to write. Added: an empty
  `TenantId` is set to the current tenant, another tenant's id is refused. Modified and deleted: the
  entity must belong to the current tenant (empty is refused), and a changed `TenantId` is refused
  in either direction. No tenant in the scope: refused. A violation throws
  `TenantIsolationException` before anything is sent. That exception derives from `Exception`
  directly, so a handler written for another failure cannot swallow it.
- **The statement itself (addition).** The interceptor sees the entity in memory, not the row in
  the database. An entity attached with `Update()` that carries the id of another tenant's row and
  the current tenant's id passes every check above, and `UPDATE ... WHERE Id = @id` would then change
  the other tenant's row. So `TenantId` is a concurrency token on every tenant-scoped entity: each
  UPDATE and DELETE carries `AND TenantId = @tenant`, matches no row, and fails with
  `DbUpdateConcurrencyException`. The column is also marked as never updatable, so no UPDATE can set
  it. The price is one more predicate on a statement that already seeks by primary key.
- **`IgnoreQueryFilters` is banned** (both overloads). The one sanctioned use is the sign-in lookup
  of Phase 5, which must find a user by e-mail before any tenant is known:
  `src/BillingRecon.Infrastructure/Identity/LoginUserLookup.cs`. The change that creates that file
  adds it to the list of sanctioned suppressions in `BannedSymbolsTests`; no other file may
  suppress the rule.
- **`EntityEntry.Reload`, `ReloadAsync`, `GetDatabaseValues` and `GetDatabaseValuesAsync` are banned
  (addition).** EF Core reads the row back by its key with the query filters switched off, so each
  of them returns another tenant's row when given its id. They are the usual calls in a
  concurrency-conflict handler, and section "the statement itself" routes a forged cross-tenant
  update into exactly that handler. The handler that ADR 0007 describes (409 with the current
  version) reads the current row with a normal query, which is filtered.
- **`ExecuteUpdate` and `ExecuteDelete` are banned** (all four methods), not routed through a
  helper. They skip both interceptors, the tenant predicate above and the row version, and
  `ExecuteDelete` is a hard delete (CLAUDE.md section 4, rule 10). A ban that a helper could lift
  for "unaudited" entities would have to be policed by review; a ban without exceptions is policed
  by the compiler. The two set-based writes the design needs later, the `PayerBalances` deltas
  (ADR 0005) and the idempotency expiry sweep (ADR 0006), are raw SQL inside their own port
  implementations, each with an explicit `TenantId` predicate where the table has one; neither
  table has audit columns.
- **Not covered:** raw SQL (reviewed case by case, ADR 0009; whether to ban the raw-SQL entry points
  as well and sanction them file by file is an open question for the owner) and a login that
  bypasses the application. Row-level security remains the R5 hardening ADR 0009 names.

### 4. Audit columns

Two interfaces, filled by `AuditStampInterceptor`, never by application code:

| Interface | Columns | Written |
|---|---|---|
| `ICreationAudited` | `CreatedAtUtc`, `CreatedByUserId` | On insert only. The model marks both as "ignore after insert", so an UPDATE never contains them, whatever the entity in memory holds. |
| `IModificationAudited` | `UpdatedAtUtc`, `UpdatedByUserId` | On insert (equal to the creation values) and on every update. |

Mutable tables implement both; append-only tables implement creation only. These columns say who
touched the row first and last. The history is in `AuditRecords` (BR-701), which they do not
replace.

**When there is no user**, `CreatedByUserId` and `UpdatedByUserId` are **null**. That is the case
for seeding, migrations and background jobs, which run with `ICurrentUser.UserId == null`. It is
the same convention `AuditRecords.UserId` already has ("null for system actions"), it keeps the
foreign key to `Users`, and it needs no invented user row in every tenant. A request cannot write
null by accident: in a request the user comes from the validated token, and until authentication
exists (Phase 5) the identity registered outside Development throws when it is asked.

`docs/database.md` changes accordingly: in the legend, **Stamp** becomes
`CreatedAtUtc NOT NULL, CreatedByUserId NULL` and a new **Touch** is
`UpdatedAtUtc NOT NULL, UpdatedByUserId NULL`; each mutable table gains the Touch columns in the
migration that creates it. `Tenants` keeps only `CreatedAtUtc`, set by `Tenant.Create`: a tenant is
created by platform onboarding before any of its users exists, and changes to its settings are
audited through `AuditRecords` (BR-607).

Three things are left to the unit of work, which does not exist yet, and are recorded here so they
are not forgotten:

- **One reading of the clock per use case** (ADR 0010). Today the interceptor reads the clock once
  per save, so all rows of a save carry one instant.
- **Stamping before events are dispatched.** `docs/domain-model.md` section 8 dispatches domain
  events before `SaveChanges`, and their handlers (audit) read the tenant and the audit columns,
  which are filled inside `SaveChanges`. `Apply` is public on both interceptors so that the unit of
  work can run them before it dispatches; running them again in the save changes nothing.
- **A command that changes only child rows must still touch its root.** ADR 0007 relies on the
  root's `UpdatedAtUtc` being "set on every command, so the token changes even when a child edit
  leaves every root column unchanged". Here application code no longer sets that column, and the
  interceptor stamps only entries that are already added or modified. So the unit of work has to
  mark the root of every aggregate a command changed as modified (its `UpdatedAtUtc`), which makes
  the interceptor stamp it and the UPDATE check and advance the row version. Until that exists no
  aggregate has children; the first one that does (`Bill`) brings the mechanism and its test.

### 5. Time

`IClock` is an Application port (`Application/Common`). `SystemClock` (Infrastructure) is created
when the services are registered, looks up `Asia/Kolkata` (then `India Standard Time`) and throws
if neither exists, so a container image without time zone data stops at startup. Its instants are
whole milliseconds, the precision of the `datetimeoffset(3)` columns, so an entity in memory equals
its stored row after a save. `FakeClock` lives in the test support folder.

The rule "time comes from `IClock`" is enforced at build time by the banned-API analyzer instead of
the source-scanning test ADR 0010 planned. Banned in production code (`src/BannedSymbols.txt`,
RS0030 as an error): reading the system time (`DateTime.Now`, `DateTime.UtcNow`, `DateTime.Today`,
`DateTimeOffset.Now`, `DateTimeOffset.UtcNow`, `TimeProvider.System`) and using the time zone of
the machine (`TimeZoneInfo.Local`, `TimeProvider.LocalTimeZone`, `TimeProvider.GetLocalNow`,
`DateTime.ToLocalTime`, `DateTimeOffset.ToLocalTime`, `DateTimeOffset.LocalDateTime`). The one
suppression is the constructor of `SystemClock`. Two gaps of such a list are closed by tests: an
entry that names no existing member is silently ignored by the analyzer, and a suppression switches
it off, so `BannedSymbolsTests` checks that every entry resolves and that RS0030 is suppressed only
in the sanctioned files.

### 6. Identity until authentication exists

`ITenantContext` and `ICurrentUser` are Application ports. Three implementations:
`DevelopmentIdentity` (a fixed tenant and user from configuration, registered in Development only),
`UnavailableIdentity` (registered everywhere else: asking for the tenant or the user throws) and
`SystemIdentity` (no tenant, no user; created on purpose by system code such as the EF Core tools,
never registered as the identity of a request).

The Development environment therefore means "no authentication": everyone is the configured tenant
and user. It must never be the environment of a machine that others can reach.

**Refined by ADR 0031 (2026-11-21), with authentication.** `UnavailableIdentity` is gone. In every environment the
identity of a request is the Api's `RequestPrincipalIdentity`, read from the principal its token gave it; asked
outside a request it throws, as `UnavailableIdentity` did. `DevelopmentIdentity` is no longer registered: it is the
identity of the context the development seeder writes its rows with, and in Development a request without a token
gets a principal for the same tenant and user from the Api's development scheme. A request with a token is checked as
everywhere else, so Development still means "no sign-in needed", not "no authentication". `SystemIdentity` is
unchanged.

### 7. Migrations

- Location `Infrastructure/Common/Migrations`, names `NNNN_<Slice>`. The first is `0001_Tenants`
  (not `0001_Identity`): users and roles get their own.
- **CHECK constraints and indexes are declared in the model (addition)**, in the entity
  configuration or by convention, not added with `migrationBuilder.Sql` as `docs/database.md`
  section 8 said. A scaffolded migration then needs no hand editing, the snapshot knows the
  constraints, and a drift between model and migrations is detectable. `migrationBuilder.Sql`
  remains for what EF Core cannot express, such as the permission grants.
- No database defaults. The initial settings of a tenant are set by `Tenant.Create`; a default
  would silently replace an explicit zero.
- Applied at startup only in Development and only when `Database:ApplyMigrationsOnStartup` is true
  (it is, in `appsettings.Development.json`). In any other environment the options validation
  refuses to start with the setting on; migrations are a deployment step there.
- **The EF Core tools use the Infrastructure project as their startup project (addition)** and the
  design-time factory `BillingReconDbContextFactory`. They never start the API, so scaffolding a
  migration needs neither the API's configuration nor a database, and the design-time package stays
  out of the API's output (`PrivateAssets=all` in the Infrastructure project).
- Readiness (`/health/ready`) asks the database which migrations it has applied: an unreachable
  server, a missing database and an unapplied migration all mean "not ready". The login the API
  runs under must be able to read `__EFMigrationsHistory` for that (`docs/database.md` section 7).
- Tests without a database check that the model has no change missing from the migrations and that
  the migrations' script creates `Tenants` as documented.

### 8. The Tenant aggregate

`Tenant` (Domain/Identity) is an aggregate root with a row version and, for now, one operation:
`Create`, which validates the code (lower-case letters, digits, single hyphens, at most 20) and the
name. `PostingApprovalThreshold` is a plain `decimal?` until the `Money` value object exists.

## Alternatives

- **`Guid` ids on entities**, as the prompt had it. Simpler mapping, but a payment id can then be
  passed where a bill id is expected, and retrofitting typed ids later touches every signature.
- **Ids generated in the domain** (in constructors, `Guid.CreateVersion7`). The id would exist
  before `Add`, but version 7 GUIDs are not ordered the way SQL Server sorts `uniqueidentifier`,
  and the clustered index would fragment. `docs/database.md` chose EF Core's generator.
- **One audit interface** with all four columns. Append-only tables would carry two columns that
  can never differ from the creation columns.
- **A "system user" row instead of null.** Keeps the column NOT NULL, but users belong to a tenant,
  so every tenant would need one, created before its first real user.
- **Capturing the tenant when the model is built**, or one model per tenant through a custom cache
  key. The first leaks the first tenant to everyone, the second builds a model per tenant.
- **No concurrency token on `TenantId`**, relying on repositories that always load before they
  update. True today, and exactly the kind of rule that holds until one handler forgets it.
- **A global default precision for decimals** (`decimal(18,2)`). It is what rule 1 forbids: unit
  prices and quantities need other scales, and a default hides the forgotten ones.
- **Interceptors added by the dependency-injection set-up** (the first version), or
  **`SaveChanges` overridden in the context**. The first leaves every hand-built context unguarded.
  The second works, but puts three concerns into one class and the owner asked for interceptors;
  attached by the context they are both always present and testable on their own.
- **A source-scanning architecture test for the bans**, as ADR 0010 planned. It finds text, not
  symbols (aliases, `using static`), and fails after the build instead of in the editor.
- **The API as the startup project of the EF Core tools.** The usual setup, but the tools then build
  the API's service provider (which needs its configuration), and the design-time package and its
  dependencies ship in the API's output.
- **Migrating at startup in every environment.** The API's login would need DDL rights, against
  `docs/database.md` section 7, and two instances starting together would race.

## Consequences

- A new table needs a configuration with lengths and precision and nothing else; the id, the
  tenant filter and guard, the audit columns, the row version and the enum constraints cannot be
  forgotten.
- An unsized decimal or text column is caught when the model is built, not in production data.
- A forged cross-tenant update surfaces as a concurrency conflict (409 once that mapping exists),
  not as a tenant violation. Nothing is written either way.
- Set-based updates and deletes through EF Core are not available, and neither are `Reload` and
  `GetDatabaseValues`. The first use case that needs a set-based write uses raw SQL in its port
  implementation and justifies it in review.
- The context is not sealed, and reflection wires the conventions; both are confined to one class.
- The conventions are written for plain entity types, which is all the design has. Entity
  inheritance and owned or complex types would need them extended (ids, tenant filter and enum
  constraints look at root entity types only; the size check already covers complex types).
- `/health/ready` now needs the application database with every migration applied. Before this
  step it needed only a reachable server.
- EF Core's own log events are recorded from Warning upwards; its Information events (one per SQL
  statement) are not. Sensitive-data logging must stay off: parameter values are patient data.
- **Not built yet, on purpose, and due with the tables that need them:**
  - *With the first tenant-scoped table (Users):* the foreign key from `TenantId` to `Tenants` by
    convention; "no ON DELETE CASCADE anywhere" by convention (EF Core's default for a required
    relationship is cascade, and SQL Server would refuse the second cascade path anyway); and a
    model check that every index of a tenant-scoped entity leads with `TenantId`
    (`docs/database.md` section 1).
  - *With the first financial and append-only tables:* a guard at the EF layer for CLAUDE.md
    section 4 rules 10 and 11. Today the tenant guard lets a delete of the current tenant's own row
    through, and an update of a creation-only row; only the database grants, which do not exist
    yet either, would stop them. *Built since:* the guard for rule 10 with the first financial table
    (`HardDeleteGuardInterceptor`, ADR 0019), the guard for rule 11 with the first append-only table
    (`AppendOnlyGuardInterceptor`, ADR 0020), and stamping before the dispatch of domain events (ADR 0020).
  - *With the logins of `docs/database.md` section 7:* the grants, including `SELECT` on
    `__EFMigrationsHistory` for the API's login.
- **Verification.** On 2026-10-17 the step was built and tested on .NET 10 on the personal machine: a clean
  build and 493 tests green, with the migration `0001_Tenants` scaffolded there by EF Core 10.0.12 and
  compared with `docs/database.md` section 3.1. Before that, none of it had been compiled for .NET 10
  (ADR 0014): in a throwaway copy on
  .NET 9 with EF Core 9.0.1 the solution builds without warnings and every test passes, including,
  with a throwaway migration scaffolded there, the three migration tests. In that copy the
  migration was also applied to SQL Server LocalDB 2019 with the tools and by the API at startup,
  the table and its constraints were inspected and exercised, and the context was run against the
  real database (round trip, row version, tenant filter, forged update, audit columns, creation
  columns left alone by an update, id order, a hand-built context without a tenant). One thing the
  copy cannot run at all is the named tenant filter, which EF Core 9 does not have: there the
  filter is registered unnamed.
  Still to do on the machine with Docker: apply the migration to SQL Server 2022 and see `/health/ready`
  answer 200 (`docs/runbook.md` section 2, step 7). The tenant-row rule and the review fixes of 2026-10-17
  were confirmed green on .NET 10 by the owner the same day. The database-backed tests this ADR promised
  exist since Prompt 1.4 (ADR 0016) and await their first run with Docker (CLAUDE.md, section 9).
- The migration is in the repository since 2026-10-17; the three tests in `MigrationsTests` that check that
  it exists and matches the model pass.
- **Done in Prompt 1.4 (ADR 0016):** the same rules against real SQL Server, including the two cases
  no test without a database can reach: pending migrations mean not ready, and the tenant predicate in
  an UPDATE and in a DELETE (`tests/BillingRecon.IntegrationTests/Database`).

## Built 2026-10-17 with the first tenant-scoped tables (Prompt 2.2)

`Patients` and `Payers` (migration `0002_Payers`) are the first tenant-scoped tables, so the first item of
"Not built yet, on purpose" is built, by the context, for every entity type:

- **The tenant's foreign key.** Every `ITenantScoped` entity gets a required foreign key from `TenantId` to the tenant
  row (the entity whose id is a `TenantId`), named `FK_<Table>_TenantId`, without a navigation, so that loading a row
  never loads its tenant. A row cannot name a tenant that does not exist.
- **No `ON DELETE CASCADE`.** Every foreign key of the model is `Restrict`, which SQL Server writes as
  `ON DELETE NO ACTION`, whatever a configuration asked for: nothing is ever deleted (CLAUDE.md section 4, rule 10).
- **Every index of a tenant-scoped table leads with `TenantId`.** `TenantLeadingIndexConvention` refuses the model
  otherwise, at startup, in the tests and in the tools, like `ExplicitColumnSizeConvention`.
- **(addition)** **No index per foreign key by default.** Entity Framework's `ForeignKeyIndexConvention` would add one
  for every foreign key, led by the key's column (`IX_Payers_PatientId`), which the previous rule refuses. It is
  removed; a table declares each index it needs, and the one that serves a foreign key leads with `TenantId`
  (`UX_Payers_TenantId_PatientId`).
- **(addition)** **Entity Framework's own error events are logged at Debug**: `RelationalEventId.CommandError`,
  `CoreEventId.SaveChangesFailed` and `CoreEventId.QueryIterationFailed`, configured by the context itself. The
  exception is the report: the unit of work answers a lost race as an expected outcome (ADR 0018), and anything else
  reaches the exception handler, which logs it once (ADR 0011). Logged by Entity Framework as well, a race lost on a
  unique index would be an error in the log, and SQL Server's message for it names the duplicate key, which can be an
  MRN (ADR 0012).
- **(addition)** **The development tenant.** In the Development environment, after the startup migration,
  `DevelopmentTenantSeeder` (Infrastructure/Identity) adds the row of the configured development tenant if it is
  missing, so that what a developer saves satisfies the foreign key. The development user row waits for the `Users`
  table (Phase 5).
- **A module's value object is a struct.** `Mrn` (Payers) is a `readonly record struct` with its converter in the
  module's entity configuration. As a class, Entity Framework takes a property of its type for a navigation while it
  discovers the entity, before the converter applies, and the model fails ("no suitable constructor for entity type
  Mrn"). The context's own conventions cannot name a business module's types (ADR 0003), so a converter cannot be
  registered there before discovery.
- **Not built, and an open question for the owner:** `FK_Payers_PatientId` refers to `Patients(Id)` alone. The database
  would accept a payer of one tenant that names another tenant's patient; the use case cannot produce one (its lookup
  of the patient is filtered by the tenant), but nothing at the database refuses it. A composite key,
  `(TenantId, PatientId)` to `Patients (TenantId, Id)`, would, at the price of one more unique index per referenced
  table; the same choice comes back with every reference between tenant-scoped tables (bills to patients and payers).

The unit of work exists now (ADR 0018). Of its two items above, one clock reading per use case holds as long as a use
case saves once (the audit interceptor reads the clock once per save), and stamping before events are dispatched waits
for the dispatch, which comes with the first subscriber. The tests: `RelationalConventionTests` (the models),
`PayersSchemaTests` (the DDL), `PayersOnSqlServerTests` (SQL Server); the database tests that write probe rows add their
tenants' rows first.
