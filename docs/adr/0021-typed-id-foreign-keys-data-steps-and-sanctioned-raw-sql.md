# ADR 0021: Typed-id foreign keys by convention, a data step written by hand, and two more files with raw SQL

- **Status:** Accepted. The owner decided each point on 2026-11-20, before Prompt 3.3 was coded: the typed-id
  convention instead of another module exception, both raw-SQL files, the `WHERE NOT EXISTS` data step with its
  hand-edit recorded here. Written on the office machine and pre-checked on .NET 9, with the database tests run
  against SQL Server LocalDB 2019; the first run on .NET 10 and SQL Server 2022 is still to come.
- **Date:** 2026-11-20
- **Deciders:** project owner, Claude Code
- **Refines:** ADR 0003 (how a table refers to a table of another module), ADR 0015 (what a configuration declares;
  migrations are scaffolded and never edited by hand; raw SQL is banned), ADR 0005 (the ledger these rules were
  needed for; its built note has the ledger's own decisions).
- **Changes rules of CLAUDE.md:** section 2 (banned APIs: three files may send raw SQL, not one) and section 5
  (persistence: typed-id foreign keys come from the context; a migration may carry one hand-written data statement).

## Context

The receivables ledger (Prompt 3.3) is the second slice whose tables refer to a table of another module: postings,
entries and payer balances all name a payer. Three rules stood in its way.

- **Foreign keys across modules.** `BillConfiguration` declares the keys of `Bills` to `Patients` and `Payers` by
  naming the two entity types, which needed a listed exception to the module rule (ADR 0003, amendment of
  2026-11-20). That amendment said to weigh a convention again when the next table needs the same. Payments, credit
  notes, adjustments and refunds will all need it too.
- **Raw SQL.** Only `NumberSeriesAllocator` may send SQL of its own (ADR 0008, ADR 0015). The ledger has to change
  a payer's cached balances so that two postings for one payer never lose each other's change, and the
  reconciliation has to sum the whole ledger. Entity Framework has no lock hints, cannot "add to a column" without
  the banned `ExecuteUpdate`, and cannot translate a sum over a column stored through a converter.
- **Data in a migration.** A payer that existed before the ledger needs its row of balances. A scaffold writes
  schema, never data, and ADR 0015 says a migration is not edited by hand.

## Decision

1. **A column that holds the typed id of another entity is a foreign key to that entity's table, by convention of
   the context.** `BillingReconDbContext` adds it for every property whose type is a typed id owned by an entity of
   the model: `FK_<Table>_<Column>`, no navigation, `ON DELETE NO ACTION` like every key. A configuration no longer
   names an entity of another module to declare such a key. Left alone:
   - the entity's own id, and the tenant, which has its own convention;
   - a column a configuration already made a foreign key: its name and shape stay as written, so every key that
     existed before this ADR is unchanged (a test lists them);
   - an id whose table does not exist yet. The user columns (`CreatedByUserId`, `PostedByUserId`, `UserId` of an
     audit record and of an idempotency key) get their keys the day `Users` enters the model (Prompt 5.1), without
     a line of configuration; that prompt decides whether it wants all of them.
   When the id is the whole primary key (`PayerBalances.PayerId`), the key is one-to-one.
   A column that can point at several tables (`LedgerEntries.SubjectId`, `AuditRecords.EntityId`) is a plain `Guid`
   and gets no key.
2. **No new module-dependency exception.** The three Receivables configurations name no type of the Payers module.
   The exception for `BillConfiguration` stays as it is for now: removing it is a change to committed code of
   Prompt 2.4 that nobody asked for, and the convention makes it possible whenever the owner wants it.
3. **Three files may send raw SQL**, each one constant statement with parameters, the tenant taken from the current
   scope and present in every WHERE clause, the banned-API rule suppressed around exactly one call:
   - `NumberSeriesAllocator`: the locked read-and-increment of a number series (ADR 0008);
   - `ReceivablesLedger`: the read of the payer's balances under `UPDLOCK, ROWLOCK` and their change, in one batch;
   - `ReconciliationQuery`: the read-only reconciliation of the ledger with its caches. It contains no statement
     that writes; a test reads the file to make sure.
   `BannedSymbolsTests` lists the three files and checks each of these properties. A fourth file needs an ADR.
4. **A migration may carry a data statement written by hand, when the model cannot express it.** The rule of
   ADR 0015 was written against hand-edited *schema*; it stays for schema. For data:
   - the migration is scaffolded first, unchanged, and the statement is added at the end of `Up()`;
   - the statement is idempotent (`WHERE NOT EXISTS`) and takes every value from the rows it reads;
   - the exact line is in `docs/runbook.md`, and a test fails with a message that names it when it is missing;
   - `Down()` needs nothing when the migration drops the table the statement filled.
   The first use is `0008_Receivables`:
   `INSERT INTO [PayerBalances] (...) SELECT p.[Id], p.[TenantId], 0, 0, 0, SYSUTCDATETIME() FROM [Payers] AS p
   WHERE NOT EXISTS (SELECT 1 FROM [PayerBalances] AS b WHERE b.[PayerId] = p.[Id])`. It runs for every tenant at
   once, which is what a migration is: system work, with the tenant of each row taken from its payer.

## Alternatives

- **A second listed exception** (`Infrastructure.Receivables` to `Domain.Payers`), and one more with every later
  document. Rejected by the owner: the list of exceptions would grow with the schema.
- **A row version on `PayerBalances`** instead of raw SQL. Rejected: two cashiers taking payments from one payer at
  the same moment would fail each other with 409, for a row neither of them edits. `docs/database.md` section 3.7
  chose the queue on the row lock for that reason.
- **The reconciliation through Entity Framework.** It would load every ledger entry of the tenant to sum them in
  memory, and from Prompt 3.5 on it would need the entities of other modules.
- **Opening the balances lazily**, when the first posting finds no row. No data step would be needed, but a payer
  would have balances or not depending on its history, and the statement that changes the balances would become a
  conditional insert under a range lock, like the number series.
- **A separate, empty migration for the data step.** One more migration that holds nothing but one line, to keep
  the other one "untouched". Rejected: the table and its first rows belong together.

## Consequences

- A new table that holds the typed id of another gets its foreign key without anybody writing it. The test
  `The_keys_of_the_production_model_are_the_ones_this_list_names` is where a new key shows; it has to be extended
  on purpose.
- Scaffolding `0008_Receivables` is the first migration step that is not finished when the tool returns. The line
  to paste and the test that checks it are the safeguard; the owner does this on the personal machine, without
  Claude Code (`docs/runbook.md` section 8).
- The raw SQL of the ledger and of the reconciliation is not checked by the compiler against the model. Their
  tests run them against a real database; a renamed column breaks those tests, not the build.
- **Verification.** Pre-checked on 2026-11-20 in the throwaway .NET 9 copy: build, format gate, every unit and
  architecture test apart from the eleven that differ on .NET 9 by design, and the whole integration suite on
  LocalDB 2019, with `0003` to `0008` scaffolded there separately by EF Core 9 and the data step added to `0008`
  by script. Migrations `0003` to `0007`, scaffolded with the convention in place, contain exactly the foreign
  keys they contained before it. Still to do on the machine with .NET 10 and Docker: everything in
  `docs/runbook.md` section 8.
