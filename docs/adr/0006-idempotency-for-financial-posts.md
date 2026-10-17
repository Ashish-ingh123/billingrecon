# ADR 0006: Idempotency for financial POSTs

- **Status:** Proposed (accept on review of the R1 design documents)
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
