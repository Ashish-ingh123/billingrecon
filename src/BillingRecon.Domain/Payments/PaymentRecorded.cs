using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Payments;

/// <summary>
/// Money was received from a payer and recorded (BR-302, US-30): the payment has its gapless number and all of it is
/// unapplied. No id: the payment gets it when it is added to the unit of work.
/// </summary>
public sealed record PaymentRecorded(string DocumentNumber, PayerId PayerId, PaymentMethod Method, Money Amount, DateOnly PaymentDate) : IDomainEvent, IHasDocumentNumber;
