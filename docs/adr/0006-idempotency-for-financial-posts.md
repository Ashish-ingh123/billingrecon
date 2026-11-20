# ADR 0006: Idempotency for financial POSTs

- **Status:** Accepted (built with Prompt 3.2 on 2026-11-20, with the owner's approval of its plan; see "Built 2026-11-20" below for where the build is more precise than, or differs from, the text that follows)
- **Date:** 2026-09-29
- **Deciders:** project owner, Claude Code

## Context

A cashier's browser times out while `POST /payments` is in flight and the front end retries. If the
first request actually committed, the retry records a second ₹5,000 payment. The duplicate is a
posted, numbered, immutable document (BR-005, BR-006) that can only be undone by a maker-checker
reversal, and it has consumed a gapless number that can never be reused (BR-801). The same applies
to posting a bill, allocating, approving and refunding. CLAUDE.md rule 5 requires an
`Idempotency-Key` header on every financial POST; this ADR fixes how it works.

Constraints: keys must be scoped to the tenant (BR-002), a replay must return exactly the original
response so the client cannot tell it apart from a first success, two concurrent requests with the
same key must not both execute, and the stored response must become visible in the same commit as
the document, otherwise a crash between the two leaves a retry that duplicates.

## Decision

We will implement idempotency as a **tenant-scoped key store in the database with a
reserve–process–complete lifecycle**, enforced by an action filter on every financial POST.

- **Header contract (BR-803).** `Idempotency-Key: <UUID>` is required on: create/post bill, submit
  for approval, void, record payment, allocate, request and approve reversals, create/post credit
  note, adjustment and refund, approve/reject requests, and any future POST that changes money.
  Missing or malformed → 400 `idempotency_key_missing`. The filter is opt-in per action
  (`[Idempotent]`) and an architecture test asserts every financial controller action carries it.
- **Scope.** The store key is `(TenantId, Key)`. Two tenants may use the same UUID; two users in one
  tenant may not (the second gets 422 or a replay, which is correct: the key identifies the
  *operation*, not the user).
- **Fingerprint.** `RequestHash = SHA-256(method + path + raw body bytes)`. Same key, different hash
  → 422 `idempotency_key_reuse`; the original is never overwritten.
- **Lifecycle** (`docs/database.md` §6):
  1. *Reserve*: insert `(TenantId, Key, RequestHash, Status = InProgress)` in its own short
     transaction. A PK violation means the key exists: compare hash; `Completed` and equal →
     **replay** the stored status code, headers (`Location`, `ETag`) and body, plus
     `Idempotent-Replayed: true`; `InProgress` and equal → 409 `idempotency_in_progress`;
     different → 422.
  2. *Process* the use case in the business transaction.
  3. *Complete*: inside that same business transaction, update the row to `Completed` with the
     response. Document and stored response commit together (BR-007).
  4. On a business failure (4xx) the row is completed with the 4xx response, so a retry gets the
     same answer. On an unhandled failure (5xx) the reservation is deleted in a compensating
     transaction so the client may retry.
- **Retention** 30 days (`ExpiresAtUtc`), swept by a background job; the sweep is the only `DELETE`
  the application login may perform (`docs/database.md` §7). An `InProgress` row left by a crash is
  cleared by the same sweep or by the runbook procedure.
- **Ports.** `IIdempotencyStore` in Application (`TryReserveAsync`, `CompleteAsync`,
  `ReleaseAsync`); EF implementation in Infrastructure; the filter in Api.

## Alternatives

- **No idempotency; rely on the client not to retry.** Every HTTP client retries on timeouts, and the
  cost of a duplicate is a numbered financial document. Rejected.
- **Natural-key uniqueness** (unique index on payer + amount + date + reference). Two genuine ₹500
  cash payments from the same payer on the same day are legitimate and would be rejected; and
  actions that create no row (allocate, approve) have no natural key.
- **A unique `IdempotencyKey` column on each document table.** Prevents duplicate inserts but cannot
  replay the original response, does not cover actions that update rather than insert, and scatters
  the rule over every table.
- **In-memory or distributed cache as the store.** A process restart forgets keys; a distributed
  cache is banned (CLAUDE.md §2) and would be a second source of truth outside the transaction.
- **Complete the row *after* the business transaction commits.** A crash between the two leaves a
  committed document with an `InProgress` key; the retry gets 409 forever or, after the sweep,
  duplicates. Completing inside the business transaction closes that window.
- **No request fingerprint** (replay whatever the key returns). A client bug that reuses a key with a
  new payload would silently get an old response and believe the new operation succeeded.
- **User-scoped keys.** Would let two users create two documents from one client key; the operation,
  not the user, is what must be unique.
- **Indefinite retention.** Table growth with no benefit; clients retry within seconds or minutes,
  and 30 days is generous by three orders of magnitude.

## Consequences

- Every financial POST costs one extra short transaction (the reservation) and a row update inside
  the business transaction.
- Clients (the React front end, integrators) must generate a UUID per operation and reuse it on
  retry; `docs/api.md` documents this with the error codes.
- Concurrent duplicate requests are serialised by the primary key: exactly one executes.
- Integration tests: replay returns a byte-identical body and `Idempotent-Replayed: true`; different
  payload → 422; a simulated crash after reservation → 409 then recovery after release.
- The 30-day window is an accepted risk: a retry of a 31-day-old request would duplicate. No known
  client behaves that way.

## Built 2026-11-20 (Prompt 3.2)

`IdempotencyKey` (Domain, Common), the port `IIdempotencyStore` (Application, Common), `IdempotencyStore` and the
`IdempotencyKeys` table (Infrastructure, Common/Idempotency), and `[Idempotent]` with `IdempotencyFilter` (Api,
Common/Idempotency). Proven on a test-only probe endpoint that takes a document number and saves a record, and
applied to the two financial POSTs that exist: `POST /api/v1/bills` and `POST /api/v1/bills/{id}/void`. What the
build decided, each with the owner's approval of the plan unless marked:

1. **The store owns the business transaction.** The response of a request is only known after the controller has
   written it, which used to be after the use case had committed. So the filter, a resource filter that wraps the
   action and the writing of its result, works in this order: reserve the key; the store opens a transaction on the
   request's context and holds it; the action runs, and its unit of work saves into that transaction without
   committing (`RequestTransaction.IsHeld`); the response is written into a buffer, not to the client; then the
   store writes the response into the key's row and commits. Only after that does the client get the response. A
   request without `[Idempotent]` runs exactly as before.
2. **A refusal (4xx) is not completed inside the business transaction.** The Decision above says the row "is
   completed with the 4xx response". Doing that inside the transaction and committing would make permanent whatever
   the use case did before it refused, including a document number it took (a gap, BR-801). So on a 4xx the store
   rolls the business transaction back and then stores the response in a short transaction of its own
   (`RejectAsync`). On a 5xx, an exception or a request the client abandoned it rolls back and deletes the
   reservation (`ReleaseAsync`); the same happens when storing or committing a success fails, and the client then
   gets a 500, never the success that did not commit.
3. **The fingerprint covers more than the Decision says:** the method, the path *with its query string*, the
   *`If-Match` header*, and the bytes of the body. Both additions change what a request does. Without `If-Match` in
   it, a client that is answered 409 `concurrency_conflict`, reads the record again and retries with the new version
   under the old key would be handed the old 409 for thirty days; with it, the retry is 422
   `idempotency_key_reuse`, which says what is wrong: a new attempt needs a new key.
4. **What must outlive the request runs on its own connection.** Reserving, storing a refusal and releasing each
   use a short-lived context of their own, with the tenant, user and clock of the request. They commit whatever
   becomes of the business transaction, and they never see what a failed use case left in the request's change
   tracker.
5. **A key is one UUID in the hyphenated form**, not the empty UUID. No header, two headers, a list, braces or a
   UUID without hyphens are all 400 `idempotency_key_missing`.
6. **The port** has `ReserveAsync`, `CompleteAsync`, `RejectAsync` and `ReleaseAsync`; the Decision named three.
7. **Which POSTs carry the attribute is decided per controller, by a test.** `IdempotencyConventionTests` lists the
   controllers of financial documents (every POST must carry `[Idempotent]`) and the reference-data controllers
   (none may); a new controller with a POST is in neither list and fails until someone decides. Patients, payers
   and service items stay without a key: their uniqueness rules and `If-Match` protect them.
8. **Retention is recorded, not enforced.** Every row gets `ExpiresAtUtc`, thirty days after it was reserved. No
   job sweeps yet (the owner asked for the clean-up to be stated, not scheduled): `docs/runbook.md` section 7 has the
   two statements, for expired keys and for a key left `InProgress` by a process that died. Until a key is swept it
   is honoured, also after its expiry.

**The table differs from `docs/database.md` as first designed** in three ways the owner approved: the rows are
deletable (the second entity marked `IHardDeletable`, with bill lines; it is a record of requests, not of money);
the expiry index is `IX_IdempotencyKeys_TenantId_ExpiresAtUtc`, tenant first like every index of a tenant-scoped
table, so the sweep goes tenant by tenant; and `UserId` is nullable until the Identity slice.

**Found while building (accepted by the owner on 2026-11-20): the tenant guard needed to learn one thing.**
`IdempotencyKeys` is the first table whose tenant is part of its primary key. Entity Framework tracks such a row,
when it is added, under a *temporary* tenant value until the key is known, and `TenantWriteGuardInterceptor` read
that placeholder as "this row claims another tenant" and refused every reservation. The guard now treats a
temporary value like an empty one and sets the current tenant. A value a caller assigned is never temporary, so a
row that names another tenant is refused as before; two tests in `TenantWriteGuardTests` pin both. This is a
change to the guard of rule 12, small as it is.

**Not built:** the sweep; `Idempotent-Replayed` on anything but a replay; storing of 1xx or 3xx responses (they are
treated like a fault and release the key, and no financial POST returns one).

**Verification.** Pre-checked on 2026-11-20 in the throwaway .NET 9 copy against SQL Server LocalDB 2019: the four
outcomes of BR-803 on the probe endpoint; ten simultaneous requests with one key, of which exactly one runs, in
three rounds; a refusal and a fault that each undo the record and give the document number back; two tenants with
one key; and the two bill endpoints. The idempotency and number series tests ran ten times in a row without a
failure. Still to do on the machine with .NET 10 and Docker: scaffold `0006_IdempotencyKeys` with EF Core 10 and
run the same tests against SQL Server 2022. One thing only that run can show: the response buffering of the filter
on ASP.NET Core 10, which the tests exercise through the test server on ASP.NET Core 9 here.
