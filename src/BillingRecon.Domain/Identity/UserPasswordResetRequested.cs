using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>A link for a forgotten password was asked for and sent to the address of the user (ADR 0061, amended).</summary>
public sealed record UserPasswordResetRequested(UserId UserId) : IDomainEvent;
