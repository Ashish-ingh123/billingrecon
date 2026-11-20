# ADR 0019: Bill drafts: replaceable lines, one version for the whole bill, and the guard against deletes

- **Status:** Accepted for the points the owner approved with the Prompt 2.4 plan on 2026-11-20 (points 1 to 4 and
  6 below), and for the amendment of ADR 0003 it links to, which the owner accepted the same day. Point 5 was
  decided while building and awaits the owner's review. Written on the office machine and pre-checked on .NET 9, with the database tests run against SQL Server
  LocalDB 2019; the first run on .NET 10 and SQL Server 2022 is still to come.
- **Date:** 2026-11-20
- **Deciders:** project owner, Claude Code
- **Refines:** ADR 0007 (the row version of an aggregate with children), ADR 0015 (foreign keys delete nothing; the
  guard that was "not built yet, on purpose"), ADR 0006 (which POSTs carry an `Idempotency-Key`, and from when).
- **Changes a rule of CLAUDE.md:** section 4, rule 10 (no hard deletes) gains one stated exception.

## Context

Prompt 2.4 built the first financial aggregate with child rows, `Bill` with `BillLine`, in its two draft states. A
draft is edited freely until it is posted (BR-204), and the prompt asked for "replace lines". Three things the
earlier documents left open had to be decided:

- `docs/database.md` section 7 gave the application login no `DELETE` on any table but `IdempotencyKeys`, and rule 10
  says nothing is deleted. Replacing three lines by two has to do something with the third row.
- ADR 0007 puts the row version on the aggregate root. A change that touches only lines changes no column of the
  bill's own row, so Entity Framework would send no UPDATE for it, check no version and advance none: two clerks
  editing the lines of one bill at once would both succeed.
- ADR 0015 deferred the guard for rule 10 "with the first financial tables". This is the first.

## Decision

1. **A line of a draft bill may be deleted; nothing else may.** A draft is not yet a financial record: it has no
   number and no ledger entry (BR-204). `Bill.ReplaceLines` keeps the row of every line that keeps its position,
   adds rows for new positions and removes the rows beyond the new count. Only `Bill` can do this, and only while
   it is a draft; a voided bill keeps its lines. When posting arrives, a posted bill's lines never change (BR-006).
   Soft-deleted lines were the alternative; every query, total and report would have had to skip them for ever.
2. **The guard is built, and it is opt-out.** `HardDeleteGuardInterceptor`, attached by the context like the tenant
   guard, refuses a save that would delete a row of any entity that does not carry the marker `IHardDeletable`
   (Domain, Common). In the production model only `BillLine` carries it, and a test says so. A refused delete is a
   bug, so it throws. The update guard for append-only tables arrived with the audit trail (Prompt 3.4, ADR 0020:
   `AppendOnlyGuardInterceptor`); the ledger will use it.
3. **The database still deletes nothing.** The foreign key from a line to its bill is `ClientCascade`: `ON DELETE
   NO ACTION` in SQL Server, like every other key, and an instruction to Entity Framework to delete a line that was
   taken out of its bill's collection. The convention of ADR 0015 that turns every key into `Restrict` now leaves
   `ClientCascade` alone; the test of that convention names the one key that has it.
4. **The grant, when the logins are created:** `DELETE` on `BillLines` for the application login, and on no other
   table but `IdempotencyKeys` (`docs/database.md` section 7). The database cannot tell a draft's line from a posted
   bill's; that is the aggregate's rule today, and the posting slice decides whether a trigger should repeat it.
5. **The unit of work makes the root part of the save when only children changed** (ADR 0007). Before it saves,
   `UnitOfWork` looks at every added, modified or deleted entity that is reached through a collection of a versioned,
   audited entity, and marks that root's `UpdatedAtUtc` as modified when the root itself is unchanged. The audit
   stamp then writes the instant and the user, the UPDATE carries the row version the root was read at, and the
   version advances. Nothing in the domain knows about it, and the next aggregate with children gets it for free.
6. **Only what a draft needs is in the table.** `Bills` has no number, fiscal year, sequence number or posted stamps
   yet, `BillStatus` has `Draft` and `Voided` only, and `CK_Bills_NumberIffPosted` and `CK_Bills_StatusMatchesBalance`
   are not there: the posting slices add them by migration, with the code that sets them. `BalanceDue` is there and
   equals `Total` on a draft.

Also decided with the plan:

- **The patient and the payer of a bill never change.** A draft for the wrong one is voided and written again.
- **Any active payer may owe a bill for any active patient** of the tenant (BR-106 asks for one of each, not for a
  link). Billing asks the Payers module through `IBillableParties` and passes its answer on.
- **Creating a draft is not idempotent yet.** Rule 5 asks for an `Idempotency-Key` on every POST that creates a
  financial document; the store is Prompt 3.2, which adds the header to `POST /api/v1/bills`. Until then a retried
  POST can create a second draft, which is voided. *Closed on 2026-11-20 by Prompt 3.2:* `POST /api/v1/bills` and
  `POST /api/v1/bills/{id}/void` carry `[Idempotent]` (ADR 0006, built note).
- **Limits:** at most 200 lines per bill, and a line amount of at most 9,999,999,999.99, so that totals stay far
  inside `decimal(18,2)`.
- **The bill date is checked against BR-205 when it is entered or changed**, not on every edit: a draft that has
  grown older than the tenant's back-dating limit can still be corrected, and posting will check the date again.
- **A service item deactivated since may stay on the lines of the draft that already has it**; a new line cannot
  name it (`service_item_inactive`).

## Alternatives

- **Add, update and remove single lines** (`docs/domain-model.md` section 4.4 listed them). Not built: the prompt asks
  for one "replace lines", which is one request per edit of the bill, one version check, and no partial state.
- **Let the aggregate bump a counter column** on every command, so that the root is always modified. Rejected: a
  column that exists only to provoke an UPDATE, in every aggregate that has children.
- **Give lines their own row version.** Rejected by ADR 0007: the bill is edited and versioned as one document.

## Consequences

- Rule 10 now reads "nothing is deleted, except a line of a draft bill". The exception is visible in three places:
  the marker on `BillLine`, the test that lists every deletable entity, and the grant table.
- `Widget`, the probe entity of the persistence tests, carries the marker too, so that the tests which prove the
  tenant guard on a DELETE still reach the database.
- A lines-only change costs one extra UPDATE of the bill's row. That is the price of the version check.
- `BillConfiguration` names two entities of the Payers module for the foreign keys of `Bills`: a new listed
  exception to the module rule (ADR 0003, amendment of 2026-11-20), accepted by the owner on 2026-11-20.
- **Verification.** Pre-checked on 2026-11-20 in the throwaway .NET 9 copy: build, format gate, every test outside
  the database collection apart from the eleven that differ on .NET 9 by design, and the whole database collection
  on LocalDB 2019 (180 tests), with migrations scaffolded there by EF Core 9. Still to do on the machine with
  .NET 10 and Docker: scaffold `0003_ServiceItems` and `0004_Bills` with EF Core 10, build, and run every test.
