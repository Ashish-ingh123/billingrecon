using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>A wrong code of the second step was given; Locked says it was one too many and the sign-in was ended (ADR 0061).</summary>
public sealed record UserSecondStepFailed(UserId UserId, bool Locked) : IDomainEvent;
