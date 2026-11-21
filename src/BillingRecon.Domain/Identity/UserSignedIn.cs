using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>A user signed in and was given a token (US-02; ADR 0031). Nothing of the credentials is in it.</summary>
public sealed record UserSignedIn(UserId UserId, DateTimeOffset SignedInAtUtc) : IDomainEvent;
