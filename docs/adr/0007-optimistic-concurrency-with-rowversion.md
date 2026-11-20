# ADR 0007: Optimistic concurrency with rowversion

- **Status:** Proposed (accept on review of the R1 design documents)
- **Date:** 2026-09-29
- **Deciders:** project owner, Claude Code

## Context

Two clerks open the same draft bill; one adds a line and saves, the other changes the due date and
saves. Without a guard the second save silently overwrites the first (a lost update). Worse cases
involve money: a cashier allocates the last ₹500 of a payment while a supervisor approves a refund
of the same ₹500; both read `UnappliedAmount = 500`, both pass the in-memory check, and the cache
goes negative or the CHECK constraint throws an opaque error. CLAUDE.md rule 6 requires
`rowversion` tokens and 409 on conflict; this ADR fixes the mechanics and the HTTP contract.

The conflict window spans two HTTP requests (the read that populated the screen and the write), so
locking inside one request cannot solve it. SQL Server's `rowversion` column is maintained by the
engine on every update and cannot be forgotten by application code.

## Decision

We will use **`rowversion` optimistic concurrency on every mutable aggregate root**, surfaced to
clients as **ETag / If-Match**, with **409 `concurrency_conflict`** on a stale write (BR-804).

- **Schema.** Every mutable aggregate root table (`Tenants`, `Users`, `Patients`, `Payers`,
  `ServiceItems`, `Bills`, `CreditNotes`, `Payments`, `Refunds`, `Adjustments`, `ApprovalRequests`,
  `NumberSeries`) has `RowVersion rowversion NOT NULL`, mapped in EF Core with `.IsRowVersion()`.
  Child rows (`BillLines`, `CreditNoteLines`, allocations) have none; the root's token guards them.
  Roots that own editable children (`Bills`, `CreditNotes`) also carry `UpdatedAtUtc`, set on every
  command, so the token changes even when a child edit leaves every root column unchanged.
- **Not versioned.** Append-only tables (`LedgerPostings`, `LedgerEntries`, `AuditRecords`) are never
  updated. `PayerBalances` is updated with atomic `SET x += @delta` statements and needs no token
  (ADR 0005). `NumberSeries` uses a pessimistic lock on the number path (ADR 0008) because contention
  is certain there and optimistic retries would be wasted work; its token covers admin prefix edits.
- **HTTP contract.** Every `GET` of an aggregate returns `ETag: "<base64 rowversion>"` and the same
  value in the body (`version`). Every request that changes an existing aggregate (`PUT`, `PATCH`,
  and action `POST`s such as `/post`, `/void`, `/allocate`, `/approve`) requires `If-Match`; a
  missing header is 428 `precondition_required`. Creates need no `If-Match`.
- **Conflict handling.** EF Core throws `DbUpdateConcurrencyException` when the token does not
  match; the ProblemDetails mapper turns it into 409 `concurrency_conflict` with the current
  `ETag`. The server never retries automatically and never merges; the client re-reads, shows the
  user what changed, and resubmits with a new idempotency key (ADR 0006).
- **Two-aggregate use cases** (allocation and its relatives, `docs/domain-model.md` §6) send both
  tokens (`If-Match` for the commanded aggregate, the bill's version in the body) and fail as a
  whole if either is stale.
- **Tests.** Integration tests for every mutating endpoint: stale `If-Match` → 409 and no change;
  missing → 428; two concurrent allocations of the same last rupee → exactly one succeeds.

## Alternatives

- **Last writer wins (no token).** Lost updates on drafts and, for money, two writers both passing
  an in-memory check. Rejected.
- **Pessimistic locks (`UPDLOCK`) on aggregate reads.** Protects only within one request; the
  conflict here is between a read in one request and a write in a later one. Pessimistic locking is
  used only where contention is certain and short (number series).
- **An application-maintained `int Version` column.** Portable across databases and readable, but it
  relies on every update path incrementing it. `rowversion` is maintained by the engine and cannot
  be bypassed by raw SQL or a forgotten line.
- **`ModifiedAtUtc` as the token.** Millisecond resolution can collide, and it is application-set.
- **Serializable or snapshot isolation for the whole transaction.** Solves in-transaction anomalies,
  not the cross-request lost update; snapshot isolation may still be enabled later for reporting
  reads, as a separate decision.
- **Optional `If-Match` (only check when supplied).** Every client would omit it and the guard would
  be theoretical. Requiring it costs the front end one header it already has from the `GET`.

## Consequences

- Clients must carry the ETag from read to write and handle 409 by re-reading. The React front end
  gets a shared helper; `docs/api.md` documents the contract.
- Every aggregate endpoint has two more test cases. The architecture tests check that every
  aggregate root entity configuration declares a rowversion property.
- Hot rows that would generate constant 409s (`PayerBalances`, `NumberSeries`) are deliberately
  handled by other mechanisms, so 409 stays rare and meaningful.
- Raw SQL updates against versioned tables must not be written; EF's tracked updates are the only
  path, which the architecture test for `ExecuteUpdate`/`ExecuteSql` usage enforces outside the
  ledger and cache services.

## Built 2026-10-17 (Prompt 2.2, ADR 0018)

The contract is built for patients and payers, the first mutable aggregates with endpoints:

- `GET`, the `201` of a create and the `200` of every change carry `ETag: "<base64 rowversion>"`, and the body
  carries the same value as `version`.
- `PATCH`, `POST /{id}/deactivate` and `POST /{id}/reactivate` need `If-Match`. Missing: 428
  `precondition_required`. Not one strong ETag of eight bytes (a weak tag, `*`, a list, something else): 400
  `validation_failed` with the field `If-Match`. Stale: 409 `concurrency_conflict`, nothing changed.
- The handler compares the version the client sent with the one it loaded before it changes anything, and the
  UPDATE carries the loaded row version, so a change between the load and the save is caught as well.
- **Deviation:** the 409 does not carry the current ETag, as the Decision above says it would. The client re-reads,
  which BR-804 asks for anyway; carrying the ETag would need a second read in the failure path of every handler.
- The rowversion of every mutable aggregate root comes from the context's convention (`IHasRowVersion`, ADR 0015),
  checked by `ConventionCoverageTests` rather than by an architecture test on the configurations.
