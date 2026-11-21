using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>A checker rejected a prefix change, saying why (BR-605); nothing changed.</summary>
public sealed record PrefixChangeRejected(PrefixChangeId PrefixChangeId, string Reason) : IDomainEvent, IHasReason;
