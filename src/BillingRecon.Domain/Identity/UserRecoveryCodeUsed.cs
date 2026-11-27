using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>A user signed in with a recovery code, which is spent; Remaining is how many they have left (ADR 0061).</summary>
public sealed record UserRecoveryCodeUsed(UserId UserId, int Remaining) : IDomainEvent;
