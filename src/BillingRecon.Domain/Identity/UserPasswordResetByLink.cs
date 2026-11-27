using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>A user set a new password with the link of the mail: every session they had ends (ADR 0061, amended).</summary>
public sealed record UserPasswordResetByLink(UserId UserId) : IDomainEvent;
