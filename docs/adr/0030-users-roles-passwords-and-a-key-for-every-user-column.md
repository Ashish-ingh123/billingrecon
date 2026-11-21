# ADR 0030: Users and roles, their passwords, and a key for every column that names a user

- **Status:** Accepted. The owner approved the plan of Prompt 5.1 on 2026-11-21 with the six recommended answers,
  which are points 1 to 6 below; point 7 was settled while building, and the owner confirmed it with the plan of
  Prompt 5.2 on 2026-11-21 (ADR 0031, point 11). Built and tested
  on .NET 10 (SDK 10.0.404, EF Core 10.0.12) on the office machine: build, format gate and the tests without a
  database in the repository, the database tests in a throwaway copy against SQL Server LocalDB 2019. SQL Server 2022
  in Docker is still to come.
- **Date:** 2026-11-21
- **Deciders:** project owner, Claude Code
- **Refines:** ADR 0021 (point 1 left the user columns to this prompt), ADR 0020 (what an audit image holds),
  ADR 0006 (which POSTs take an Idempotency-Key).
- **Implements:** BR-601, US-01; the Users, Roles and UserRoles tables of `docs/database.md` section 3.1.

## Context

Every document records who drafted, posted, voided, allocated, reversed or approved it, and every audit record and
idempotency key records the user of its request. Until this prompt those were bare ids: the users had no table. Prompt
5.1 builds the users of a tenant with their roles and passwords; sign-in and tokens are Prompt 5.2, and what each role
may do is Prompt 5.3.

## Decision

1. **Every column that names a user is a foreign key to `Users`**, by the typed-id convention of ADR 0021, without a
   line of configuration: 34 keys on the tables of earlier prompts (the stamps `CreatedByUserId` and `UpdatedByUserId`,
   `PostedByUserId`, `VoidedByUserId`, `AllocatedByUserId`, `ReversedByUserId`, the maker and checker of an approval
   request, the user of an audit record and of an idempotency key) and five on the new tables. A "who" that names
   nobody is a defect the database now refuses (547). Every key is `ON DELETE NO ACTION`; a user is disabled, never
   deleted.
2. **The development database is disposable.** Migration `0018_Users` adds those keys with the check of existing rows,
   so on a database whose rows name users that do not exist it fails. No database outside the tests holds real data (R1
   is not deployed), so no hand-written data step was written: a developer drops the development database before
   applying `0018` (`docs/runbook.md` section 3), and the API recreates it at startup, the development tenant and the
   development user with it.
3. **Passwords:** PBKDF2 with HMAC-SHA-256 through the static `Rfc2898DeriveBytes.Pbkdf2` of the BCL, 600,000
   iterations (the figure OWASP gives for this function), a random salt of 16 bytes, a hash of 32, stored as
   `pbkdf2-sha256$iterations$salt$hash` so that the iterations can rise later without invalidating older hashes;
   compared with `CryptographicOperations.FixedTimeEquals`. A password has 12 to 128 characters and no rules of
   composition (NIST SP 800-63B). An administrator's reset sets a password the administrator gives; nothing forces a
   change at the next sign-in. The hasher is the port `IPasswordHasher`, which sign-in (5.2) uses too.
4. **Guards:** a user holds at least one role; the tenant's last active administrator can neither lose the role nor be
   disabled (422 `user_last_tenant_admin`); nobody disables themselves (422 `user_cannot_disable_self`). Two
   administrators disabling each other at the same moment could still leave none; that needs a lock the prompt did not
   ask for, and a platform operator can restore one.
5. **No role check yet.** The endpoints are open to whoever the current identity is (the development or test identity;
   no token exists yet). Deny-by-default arrives with Prompt 5.2 and the permission matrix, where users are
   TenantAdmin only, with Prompt 5.3. The roles are stored now; nothing reads them yet.
6. **The development user** is created by `DevelopmentTenantSeeder` with the development tenant: the id of
   `DevelopmentIdentity:UserId`, a TenantAdmin, address `admin@development.local`, and a password hash that matches no
   password (`!`), until 5.2 decides how a developer signs in. An administrator's reset can give it a real one.
7. **Users are reference data, and their POSTs take no Idempotency-Key** (settled while building; confirmed by the
   owner with the plan of Prompt 5.2). The plan said the POSTs would need a key "as elsewhere"; the convention of ADR 0006 is that only financial
   POSTs do, and patients and payers do not. There is a second reason: the store of keys keeps a SHA-256 fingerprint of
   the method, the path, `If-Match` and the bytes of the body, and the body of a creation or a reset holds the password.
   A stored fast hash of a body with a password in it is a target for offline guessing, so no such fingerprint may
   exist. A retried creation answers 409 `user_email_taken`; a retried reset sets the same password again. A test
   checks that no idempotency key is stored by a reset.
8. **The hash never reaches the audit trail.** `EntitySnapshot` leaves out a column whose configuration carries the
   annotation `BillingRecon:NotAudited`; `Users.PasswordHash` is the only one. A reset is visible as its own action,
   `PasswordReset`, whose event carries neither password nor hash. No answer of the API carries either.
9. **The tables** (`0018_Users`): `Roles` (global, the five codes written from the model by `HasData`, not by a
   hand-written statement; the tests' reset leaves it alone), `Users` (tenant-scoped, the address as entered and
   upper-cased, `UX_Users_TenantId_NormalizedEmail`, status, `LastSignInAtUtc` for 5.2, the stamps and a row
   version), and `UserRoles` (keyed by user and role code, the tenant, who granted the role and when; a role taken away
   is deleted, like a line of a draft, and the user's audit trail keeps every change).
10. **The test identities are users.** A database test acts as an identity whose id must now exist:
    `NewApiTenantAsync`, the probe and API tenant helpers and the test factory's `CreateClientFor` write the identity's
    user row (as the identity itself, the only way the tenant guard lets a tenant-scoped row in, and without audit
    records). Two tests that act as a second user of the same tenant add its row explicitly.

## Alternatives

- **Keys on the creation and update stamps only**, as the prompt's wording said. Rejected with the plan: it needs an
  exclusion list in the convention, and leaves the posted-by, approved-by and audit columns able to name nobody.
- **No keys until 5.2.** Rejected with the plan: the convention would have needed the same exclusion, only to remove it
  a prompt later.
- **A hand-written data step that creates a row for every user id already in the tables.** It would invent users
  without an address or a password; rejected because no database outside the tests needs it.
- **An Idempotency-Key with a keyed (HMAC) fingerprint for these endpoints.** Possible, but a change to the
  idempotency store nobody asked for; reference data does without a key already.

## Consequences

- **Changed expectations in existing tests, none weakened.** `TypedIdForeignKeyTests` expected the user columns to have
  no key ("until Prompt 5.1") and now expects the key to `Users`; its full list of keys, the SQL Server lists of keys of
  `ServiceItemsOnSqlServerTests`, `PayersOnSqlServerTests`, `BillsOnSqlServerTests`, `ReceivablesOnSqlServerTests`,
  `IdempotencyKeysOnSqlServerTests` and `AuditOnSqlServerTests`, the lists of `TenantQueryFilterTests`,
  `HardDeleteGuardTests` and `RelationalConventionTests` (the roles of a user are deleted, through a third
  client-cascading key), `IdempotencyConventionTests` and `MigrationsOnSqlServerTests` (18) name the new tables, keys
  and controller. `BillsOnSqlServerTests` and `AuditStampsOnSqlServerTests` each add the row of the second user they act
  as.
- A row written by a user that does not exist is refused by the database; every use case writes as the current user,
  which in Development is the seeded development user.
- **Verification.** On .NET 10 (SDK 10.0.404): the repository's build with warnings as errors and
  `dotnet format --verify-no-changes` are clean; Domain 604, Application 350 and Architecture 113 tests pass in the
  repository; the integration project passes in full (1039 tests) in a throwaway copy whose fixture points at LocalDB
  2019, and `UsersApiTests` with the 20 concurrency tests named `BR804` ran three more times without a failure. `0018`
  was scaffolded with the repository's `dotnet-ef` 10.0.12 in that copy and copied back; EF Core reports no pending
  model change. Still to do: SQL Server 2022 in Docker.
