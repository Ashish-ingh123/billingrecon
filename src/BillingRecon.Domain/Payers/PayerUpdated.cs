using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payers;

/// <summary>The name or the contact details of a payer were corrected. Raised only when a value really changed.</summary>
public sealed record PayerUpdated(PayerId PayerId, PayerKind Kind, string Name) : IDomainEvent;
