using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payments;

/// <summary>
/// An opening advance was brought in (ADR 0053): money of the payer the system before held and had not applied, under
/// an opening number here, with the reference it had there.
/// </summary>
public sealed record PaymentBroughtForward(string DocumentNumber, PayerId PayerId, PaymentMethod Method, Money Amount, DateOnly PaymentDate, string LegacyNumber)
    : IDomainEvent, IHasDocumentNumber;
