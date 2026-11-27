using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>A user began to enrol a second step for their sign-in (ADR 0061).</summary>
public sealed record UserSecondStepEnrolmentBegun(UserId UserId) : IDomainEvent;
