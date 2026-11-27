using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>An administrator took the second step of a user away (ADR 0061).</summary>
public sealed record UserSecondStepReset(UserId UserId) : IDomainEvent;
