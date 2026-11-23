# ADR 0034: Database roles, grants and the logins of the application

- **Status:** Accepted. The owner approved the plan of Prompt 5.4 on 2026-11-23 "as recommended", with its four answers,
  which are points 1 to 4 below. Points 5 to 7 were settled while building and are marked so.
- **Date:** 2026-11-23
- **Deciders:** project owner, Claude Code
- **Refines:** ADR 0015 (migrations are scaffolded and never edited by hand; `migrationBuilder.Sql` is for the grants),
  ADR 0021 (a migration may carry a statement written by hand), ADR 0016 (the database tests).
- **Implements:** BR-005, BR-402, BR-702 (the application's login cannot delete financial data or change the ledger
  and the audit trail); CLAUDE.md section 4, rules 10 and 11; `docs/database.md` section 7.
- **Changes rules of CLAUDE.md:** section 5 (a migration of grants, written by hand) and section 9 item 5 (the grants
  and the migration login).

## Context

`docs/database.md` section 7 planned two logins since the design (a migrator and the application), and rules 10 and 11
of CLAUDE.md promise that the application's login has no DELETE on financial tables and no UPDATE or DELETE on the
ledger and the audit trail. Neither login existed: development and the tests ran as `sa`, so only the guards of the EF
layer (`HardDeleteGuardInterceptor`, `AppendOnlyGuardInterceptor`) stood between the code and a forbidden change. A
statement that goes around the context (raw SQL, a tool, a script with the application's connection string) met no
refusal at all.

Logins are objects of the server and carry passwords; a database role and its grants are objects of the database and
carry none. The migrations run wherever a database is made (Development's startup, the tests, the deployment script),
so grants written in a migration exist wherever the tables do.

## Decision

1. **The grants are a migration, `0020_DatabaseRoles`**, scaffolded empty (the model did not change) and written by
   hand with `migrationBuilder.Sql`, idempotent, with a `Down()`. It creates the database role
   **`billingrecon_application`** and its grants. No login, user, or password is ever in a migration or in git: the
   runbook makes them per environment (`docs/runbook.md` section 10). This extends ADR 0021, which allowed one statement
   written by hand at the end of a scaffolded migration, to a migration that is all hand-written statements; its test
   is `MigrationsTests.ADR0034_the_migrations_create_the_applications_role_with_its_grants_and_change_no_table`.
2. **Schema-wide grants, with the exceptions the model's markers call for:**
   - `GRANT SELECT, INSERT, UPDATE ON SCHEMA::[dbo]`: every table, including those later migrations add;
   - `DENY UPDATE, DELETE` on the tables of `IAppendOnly` entities: `LedgerPostings`, `LedgerEntries`, `AuditRecords`;
   - `GRANT DELETE` on the tables of `IHardDeletable` entities and nowhere else: `BillLines`, `CreditNoteLines`,
     `UserRoles`, `IdempotencyKeys` (`docs/database.md` section 7 listed two of the four; it is corrected);
   - `DENY INSERT, UPDATE, DELETE` on `Roles` (reference data that migration `0018` wrote) and on
     `__EFMigrationsHistory` (which the readiness check reads).
   `DatabasePermissionsOnSqlServerTests` reads every table's effective permissions as the application's login and
   compares them with what the markers of the table's entities allow; a new append-only or deletable table that lacks
   its line fails it, and the failure prints the line a migration needs.
3. **The migrator holds `db_ddladmin`, `db_datareader`, `db_datawriter` and `db_securityadmin`**: it changes the
   schema, runs the data steps (ADR 0021) and creates the role and its grants. Section 7 named two of these roles. It
   does not hold `db_owner` and cannot create a database.
4. **The database tests act as the application's login** wherever production code meets the migrated test database:
   the API host and the contexts of the production code. The fixture makes the database as the runbook does: the
   administrator creates it and maps the logins, a login with exactly the migrator's four roles migrates it, and a login
   that is a member of `billingrecon_application` alone is the application's (random passwords per run). Emptying the
   tables (Respawn), reading the catalogue, the probe database, the databases a test creates (Development, Pending), and
   the eight helpers that seed a defect on purpose (a changed ledger row, a constraint switched off) use the
   administrator's connection.
5. *Settled while building:* **the role is owned by `dbo`** (`CREATE ROLE … AUTHORIZATION [dbo]`, which the migrator's
   `db_securityadmin` allows), and its grants are recorded with `dbo` as the grantor, so the migrator's user can be
   dropped or replaced without touching them. A test checks both.
6. *Found while building:* **the application relies on `READ_COMMITTED_SNAPSHOT ON`**, and nothing said so. EF Core
   switches it on when it creates a database (Development, every earlier test database, the probe database); a database
   created with a plain `CREATE DATABASE` has it off, and there a read waits for any writer's open transaction. The
   first run of the tests on a database created by hand showed it: an idempotent request holds its transaction while
   its answer is stored (ADR 0006), and a read of the same table waited until the command timed out. The runbook's
   creation of the database switches it on with the statement EF Core uses, the fixture does the same, and a test
   checks it on the test databases.
7. *Settled while building:* **every test run migrates as the migrator.** Not in the plan, which kept the migration on
   the administrator's connection: proving the migrator's four roles on every run was cheap and found point 5.

## Alternatives

- **The grants in a script outside the migrations**, run after them. Rejected with the plan: a database could have
  its tables without its grants, and Development and the tests would need a second step.
- **Grants per table, each in the migration that creates its table.** Fails closed (a new table without a grant is
  unusable), but every later migration would be edited by hand. Rejected with the plan; the table-driven test catches
  the one dangerous case, a new append-only table that keeps the schema's UPDATE.
- **The migrator as `db_owner`.** Simpler, but it could also change ownerships and grant itself anything. Rejected
  with the plan.
- **Leave the role owned by its creator.** The migrator's user could then not be dropped while it owns the role, and
  a database migrated by different logins would differ. Rejected while building.
- **Leave `READ_COMMITTED_SNAPSHOT` to chance.** A database made by hand would make readers wait for writers, and the
  idempotent requests would hold readers for the length of a request. Rejected.

## Consequences

- At the database, the application's login cannot delete a bill, a payment, a patient or any financial row, nor change
  or delete a ledger row or an audit record, whatever the code above it does (`BR005_BR702_…` in
  `DatabasePermissionsOnSqlServerTests`). The EF guards stay as the first refusal.
- Every test of the API runs with the production grants; a use case that needs a permission the role lacks fails its
  tests.
- Each environment needs the logins of `docs/runbook.md` section 10 before the API can start, and the API's connection
  string names the application's login. Development may keep its administrator's connection, which also applies the
  migrations at startup.
- A test that seeds a defect needs the administrator's context, `AdministratorApiContext`, and says why.
- A later migration that adds an append-only or deletable table adds its line (`DENY UPDATE, DELETE` or
  `GRANT DELETE`); the permission test says which.
- Revisit when the schema stops being `dbo` alone, or when a background job needs grants of its own.

## Verification

On .NET 10 (SDK 10.0.404): in the repository, the Release build and `dotnet format --verify-no-changes` are clean, and
Domain 625, Application 394, Architecture 121 and the 660 integration tests without a database pass. In a throwaway copy
on LocalDB 2019 (mixed mode, so SQL logins work), the whole integration project passes (1127 tests), and the
permission, idempotency, settings, users, readiness and migration tests (70) ran three more times without a failure. With the `DENY` on
`AuditRecords` and the `GRANT DELETE` on `UserRoles` removed from the migration in a copy, four tests fail: the script
check, the user roles test of the API (it can no longer replace roles), the test of the audit record, and the
table-driven test, which prints the line to add. Still owed: SQL Server 2022 in Docker.
