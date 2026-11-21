using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>A disabled user may sign in again.</summary>
public sealed record UserEnabled(UserId UserId, string Email) : IDomainEvent;
