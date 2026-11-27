using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>A user gave the right code of their second step (ADR 0061).</summary>
public sealed record UserSecondStepPassed(UserId UserId) : IDomainEvent;
