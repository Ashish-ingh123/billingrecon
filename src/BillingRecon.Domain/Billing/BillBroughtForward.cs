using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>
/// An opening receivable was brought in (ADR 0053): what the payer still owed on a bill of the system before, under an
/// opening number here, with the number it had there.
/// </summary>
public sealed record BillBroughtForward(string DocumentNumber, PayerId PayerId, Money Total, DateOnly BillDate, string LegacyNumber)
    : IDomainEvent, IHasDocumentNumber;
