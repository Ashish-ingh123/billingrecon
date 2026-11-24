using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>An operator suspended the tenant (ADR 0052): nobody of it signs in or acts until the suspension is lifted.</summary>
public sealed record TenantSuspended(string Reason) : IDomainEvent, IHasReason;
