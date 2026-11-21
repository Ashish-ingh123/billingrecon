using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Application.Payments;

/// <summary>
/// What a refund use case knows about the source of a refund: its payer, date, number and what it has left. For a
/// payment, the tracked aggregate as well, which this module changes itself; a credit note is changed through the
/// Billing module's contract and is never held here.
/// </summary>
internal sealed record RefundSourceFacts(PayerId PayerId, DateOnly Date, string Number, Money Available, Payment? Payment);
