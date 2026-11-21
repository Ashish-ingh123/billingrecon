using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>
/// A user was created in the tenant (US-01). The id is still empty here: it is assigned when the user joins
/// the unit of work, so the audit record takes it from the entity.
/// </summary>
public sealed record UserCreated(string Email, string DisplayName, string Roles) : IDomainEvent;
