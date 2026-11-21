using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>A user was disabled and can no longer sign in (BR-601).</summary>
public sealed record UserDisabled(UserId UserId, string Email) : IDomainEvent;
