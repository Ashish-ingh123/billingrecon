# ADR 0020: Domain events are dispatched by the unit of work, and the audit trail is their one subscriber

- **Status:** Accepted with the owner's approval of the Prompt 3.4 plan on 2026-11-20, including the three points
  the prompt did not state (the migration number, the five update events, the append-only guard built here).
  Written on the office machine and pre-checked on .NET 9, with the database tests run against SQL Server LocalDB
  2019; the first run on .NET 10 and SQL Server 2022 is still to come.
- **Date:** 2026-11-20
- **Deciders:** project owner, Claude Code
- **Refines:** ADR 0003 (the audit trail handles `IDomainEvent` generically and names no module's event), ADR 0015
  (the two things it left "not built yet, on purpose": stamping before the dispatch, and the guard for rule 11),
  ADR 0018 (the unit of work), ADR 0012 (where patient data may go).
- **Implements:** CLAUDE.md section 4, rule 11; BR-701 to BR-703; `docs/domain-model.md` sections 4.13 and 8;
  `docs/database.md` section 3.8.

## Context

The aggregates have raised domain events since Prompt 2.2 and nothing consumed them. Rule 11 asks for an audit
record of every state change, in the transaction of the change, with who, when, tenant, entity, action, before and
after JSON and the correlation id. `docs/domain-model.md` section 8 already said how: the unit of work dispatches
the events in-process before it saves, and Audit is the only subscriber. Four things were open:

- An event raised by a factory method cannot carry the id of its aggregate, which is assigned when the aggregate is
  added to the unit of work. No event carries the tenant, the user or a before image.
- Corrections (a patient's demographics, a payer's name, a service item's price, a draft bill's header and lines)
  raised no event at all, so they would have left no trace.
- The audit columns were stamped inside `SaveChanges`. A subscriber that runs before the save would have seen rows
  without them, and stamping twice would have read the clock twice (CLAUDE.md section 9, item 5).
- Nothing at the Entity Framework layer stopped an update or a delete of an audit row.

## Decision

1. **The unit of work dispatches, in this order, inside its `try`:** make the roots of changed children part of the
   save (ADR 0019); begin the commit, which reads the clock once and stamps tenant and audit columns
   (`BillingReconDbContext.BeginCommit`); take every event off every tracked aggregate
   (`IHasDomainEvents`, implemented by `AggregateRoot<TId>`), together with the aggregate's kind, id and its images
   before and after; hand each to every `IDomainEventSubscriber`; save; commit. The context ends the commit in the
   `finally` (`EndCommit`). A subscriber that throws fails the commit like any other fault.
2. **One clock reading per commit.** `AuditStampInterceptor` holds the instant from `BeginCommit` to `EndCommit`, so
   the stamp before the dispatch, the stamp of the save and `AuditRecord.OccurredAtUtc` are the same instant. A save
   outside a unit of work reads the clock for itself, as before.
3. **What a subscriber receives is a `DomainEventOccurrence`:** the event, `EntityType` (the name of the aggregate's
   class), `EntityId` (the Guid of its key), `OccurredAtUtc`, `BeforeJson` and `AfterJson`. The unit of work knows
   these; the event does not have to.
4. **The audit subscriber is generic** (`AuditSubscriber`, Application, Audit module). It writes one `AuditRecord`
   per occurrence through `IAuditWriter`, which adds it to the same context, so the record is saved by the same
   save and rolled back with it. It names no event:
   - the action is the name of the event without the name of its aggregate (`PatientRegistered` is `Registered`,
     `BillLinesReplaced` is `LinesReplaced`); an action longer than its column throws;
   - the reason comes from an event that implements `IHasReason` (`BillVoided`);
   - the user comes from `ICurrentUser`, the correlation id from the new port `ICorrelationContext`, which the API
     implements from the id its middleware gave the request (a scope without a request has an id of its own);
   - `DocumentNumber` stays null until a document has a number (Prompt 3.5).
5. **The images are the stored row** (`EntitySnapshot`): one JSON member per column, named like the property, with
   the value as the column holds it (a typed id as its Guid, money as a number, an enum by name), built from Entity
   Framework's original values (before) and current values (after). Collections of children are included, so a
   bill carries its lines, and a line that was removed is in the before image only. Left out: the row version, the
   tenant, and in a child the key of its parent. Before is null when the commit creates the aggregate.
6. **Every state change raises an event, and only a real change does.** Five events were added:
   `PatientUpdated`, `PayerUpdated`, `ServiceItemUpdated`, `BillHeaderUpdated`, `BillLinesReplaced`. Each is raised
   when a value differs after the change; a correction that sends what is already stored changes nothing, raises
   nothing, writes no row and leaves the version as it was.
7. **Audit is the only subscriber.** An effect in another module stays an explicit call through that module's port
   in the use case. An architecture test lists the classes that implement `IDomainEventSubscriber`.
8. **The append-only guard is built here** (`IAppendOnly`, `AppendOnlyGuardInterceptor`, attached by the context
   like the other guards): a save that would update or delete a row of a marked entity throws. Only `AuditRecord`
   carries the marker today; a test lists the marked entities. Prompt 3.3 was to bring this guard with the ledger;
   3.4 was built first, so the ledger will reuse it.
9. **`AuditRecords` has a `bigint IDENTITY` key**, as `docs/database.md` section 3.8 always said. It is the one key
   in the model that is not a typed Guid id: records are only appended, and the number is the order they were
   written in. `AuditRecord` therefore does not derive from `Entity<TId>`; it is tenant-scoped like any other row.
10. **The trail is read through `GET /api/v1/audit-records?entityType=&entityId=`** (US-43), paged, oldest first.
    There is no other verb. Another tenant's record has an empty trail, like an id nobody has.
11. **The migration is `0007_Audit`.** The prompt named it `0008_Audit`, after `0007_Receivables`; Prompt 3.3 has
    not been given, so the numbers follow the order the migrations apply in, and the receivables migration becomes
    `0008_Receivables` (`docs/build-prompts.md` is updated).

## Alternatives

- **Dispatch from a `SaveChanges` interceptor.** Rejected: a save outside a unit of work (seeding, the idempotency
  store's own short contexts) would dispatch too, and an interceptor cannot be handed the subscribers of the scope
  without the context knowing the dependency-injection container.
- **Events that carry everything the audit needs.** Rejected: every event would repeat the id, and a factory method
  does not have it; the before image would have to be collected by every aggregate method.
- **Diff-only images (changed columns only).** Rejected for now: the whole row before and after answers "what did
  it look like" without replaying the trail, and the rows are small. A bill with 200 lines makes a large record;
  if that hurts, the lines can move to a diff later without changing the table.
- **Audit by scanning the change tracker, without events.** Rejected: the action would be `Modified`, not
  `Deactivated` or `Voided`, and the reason of a void would be lost.
- **A frozen clock for the whole request**, so that a handler's own reading and the commit's are one. Not built:
  it changes what `IClock` means for every caller. A handler that needs the instant reads it once; the commit
  reads it once.

## Consequences

- An aggregate is audited by raising an event. A new state change that forgets its event leaves no trace, which is
  why the API tests count one record per use case; the next aggregates need the same test.
- **Patient data is copied into the audit table**: the images hold the row, names, phone numbers and e-mail
  addresses included. It is the same database, behind the same tenant filter, and it never reaches a log
  (ADR 0012). It is also a copy that is never changed or deleted, which a later erasure or retention rule will have
  to take into account. Recorded as an open point for the owner.
- `UnitOfWork` takes the subscribers as an optional third constructor argument; built by hand without them, as the
  persistence tests build it, it saves, clears the events and writes no audit record.
- The interceptors are now created once per context and held by it, instead of being created in `OnConfiguring`.
- The guard is at the Entity Framework layer only. The grants that refuse `UPDATE` and `DELETE` on `AuditRecords`
  for the application login (BR-702) come with the logins (`docs/database.md` section 7, Prompt 5.4); until then
  raw SQL from outside the application could still change a row.
- A changed expectation in existing tests, called out as the working agreement asks:
  `PatientTests.Updating_the_demographics_...` expected no event and now expects `PatientUpdated`.
- **Verification.** Pre-checked on 2026-11-20 in the throwaway .NET 9 copy: build, format gate, every unit and
  architecture test apart from the eleven that differ on .NET 9 by design, and the whole integration suite on
  LocalDB 2019, with `0003` to `0007` scaffolded there separately by EF Core 9. Still to do on the machine with
  .NET 10 and Docker: scaffold the five migrations with EF Core 10, build, and run every test.
