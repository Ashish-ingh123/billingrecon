using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>An operator lifted the tenant's suspension (ADR 0052).</summary>
public sealed record TenantResumed(string Reason) : IDomainEvent, IHasReason;
