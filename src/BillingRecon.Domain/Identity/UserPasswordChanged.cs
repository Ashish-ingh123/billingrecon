using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>A user set a password of their own (ADR 0051). The event carries no password and no hash.</summary>
public sealed record UserPasswordChanged(UserId UserId) : IDomainEvent;
