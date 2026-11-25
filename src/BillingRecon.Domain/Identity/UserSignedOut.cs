using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>A user signed out: every token issued to the user before is refused from now on (ADR 0057).</summary>
public sealed record UserSignedOut(UserId UserId) : IDomainEvent;
