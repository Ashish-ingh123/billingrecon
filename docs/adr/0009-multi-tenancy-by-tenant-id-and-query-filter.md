# ADR 0009: Multi-tenancy by TenantId and EF global query filter

- **Status:** Proposed (accept on review of the R1 design documents)
- **Date:** 2026-09-29
- **Deciders:** project owner, Claude Code

## Context

BillingRecon serves many hospitals from one deployment and one database (ADR 0003). A tenant seeing
another tenant's patients or bills is a data breach, so isolation is a security requirement
(CLAUDE.md rule 12, BR-002), not a convenience. The tenant must be established from the
authenticated identity, never from anything the client can type, and every query path, including
ones written months from now, must apply it without the developer remembering to.

The choice is between physical isolation (database or schema per tenant) and logical isolation
(a discriminator column). R1 tenants are small clinics and mid-size hospitals; the operator is one
small team.

## Decision

We will use **shared tables with a `TenantId` discriminator column, the tenant resolved once per
request from the JWT, and an EF Core global query filter applied by convention to every
tenant-scoped entity**.

- **Resolution.** The JWT carries a `tenant_id` claim (BR-602). Api middleware validates the token,
  builds an `ITenantContext` (`TenantId`) and `ICurrentUser` for the request scope, and rejects any
  request whose token lacks the claim. The tenant is **never** read from the route, query string,
  body or a custom header; an architecture test asserts that no controller binds a `TenantId`
  parameter.
- **Schema.** Every tenant-scoped table has `TenantId uniqueidentifier NOT NULL` with an FK to
  `Tenants`, and `TenantId` is the leading column of every non-clustered and unique index
  (`docs/database.md` §1). Unfiltered table: `Roles` (global reference data). `Tenants` is the tenant
  itself: a tenant reads and writes its own row only, system work every row (ADR 0015 §3).
- **Query filter by convention.** Tenant-scoped entities implement `ITenantScoped { TenantId }`. In
  `OnModelCreating` a loop applies `HasQueryFilter(e => e.TenantId == _tenant.TenantId)` to every
  such entity; no entity is configured by hand, so a new entity cannot be missed. A test enumerates
  the model and fails if any entity with a `TenantId` property lacks the filter.
- **Write guard.** A `SaveChangesInterceptor` sets `TenantId` from `ITenantContext` on every `Added`
  tenant-scoped entity and throws if a `Modified` or `Added` entity carries a different tenant.
- **Banned:** `IgnoreQueryFilters()` (architecture test scans for it); raw SQL without a
  `TenantId = @tenant` predicate (code review; the reconciliation queries in `docs/database.md`
  are the model). Reporting queries use the same `DbContext` and inherit the filter.
- **Background and system work** (idempotency sweep, migrations) runs with an explicit
  `ITenantContext` per tenant or with a system context that touches only unscoped tables.
- **Tests.** Integration tests create two tenants and assert that tenant A's token cannot read,
  update, allocate against or approve tenant B's records, including by direct id (US-04). Any
  cross-tenant success is a defect.

## Alternatives

- **Database per tenant.** The strongest isolation and a clean backup boundary, but N connection
  strings, N migration runs, N Testcontainers in tests, and cross-tenant platform operations become
  fan-outs. Disproportionate for R1 tenants; remains an option for a large customer later by
  routing a tenant to its own database behind the same code (`TenantId` stays in the schema).
- **Schema per tenant.** Same operational cost as database-per-tenant with weaker isolation and an
  awkward EF model per schema.
- **SQL Server Row-Level Security.** A good second layer, but it needs `SESSION_CONTEXT` set on
  every pooled connection and a predicate function per table; on its own it does not stop a query
  from omitting the tenant, it only hides the rows. Planned as R5 hardening on top of, not instead
  of, the query filter.
- **TenantId from a route segment or header.** Spoofable by the client; explicitly forbidden by
  BR-002.
- **Manual `.Where(x => x.TenantId == …)` in every query.** The first forgotten `Where` is a leak;
  the global filter makes the safe path the default.

## Consequences

- Isolation is enforced at three points: the filter on reads, the interceptor on writes, and the
  cross-tenant integration tests. A missed filter is caught by the model test before any data exists.
- All indexes lead with `TenantId`, which matches every real query pattern (a request is always
  about one tenant).
- Noisy-neighbour risk: one tenant's load affects others; acceptable at R1 scale, mitigated by
  per-tenant rate limits if needed (R5).
- Backup and restore are whole-database; restoring one tenant is a data-movement procedure
  (runbook, R5).
- `IgnoreQueryFilters()` cannot be used even for legitimate platform-admin reporting; such reporting
  runs per tenant or through a separate unscoped context that an ADR would have to introduce.
