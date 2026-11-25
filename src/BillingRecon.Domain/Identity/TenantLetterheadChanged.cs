using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>The letterhead of the tenant's printed documents changed (ADR 0054). The audit images hold what it was and is.</summary>
public sealed record TenantLetterheadChanged(string? LegalName, string? State, string? Gstin) : IDomainEvent;
