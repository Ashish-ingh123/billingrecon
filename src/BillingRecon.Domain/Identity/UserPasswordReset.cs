using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>An administrator set a new password for a user. The event carries no password and no hash.</summary>
public sealed record UserPasswordReset(UserId UserId) : IDomainEvent;
