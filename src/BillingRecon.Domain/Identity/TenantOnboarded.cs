using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>An operator created the tenant with the onboarding command (ADR 0052); the audit record holds its first row.</summary>
public sealed record TenantOnboarded(string Code, string Name) : IDomainEvent;
