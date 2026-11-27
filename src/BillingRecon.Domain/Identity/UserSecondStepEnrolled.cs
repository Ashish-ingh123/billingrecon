using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>A user confirmed their second step with its first code: it is asked for at every sign-in from now on (ADR 0061).</summary>
public sealed record UserSecondStepEnrolled(UserId UserId) : IDomainEvent;
