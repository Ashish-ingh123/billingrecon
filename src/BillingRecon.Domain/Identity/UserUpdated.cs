using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>The e-mail address or the display name of a user changed.</summary>
public sealed record UserUpdated(UserId UserId, string Email, string DisplayName) : IDomainEvent;
