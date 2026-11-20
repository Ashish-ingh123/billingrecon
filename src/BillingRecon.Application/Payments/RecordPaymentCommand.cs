using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Application.Payments;

/// <summary>
/// Records money received from a payer (US-30; BR-301, BR-302). Recording is posting: there is no draft payment.
/// A payment date left out is today in India.
/// </summary>
public sealed record RecordPaymentCommand
{
    /// <summary>The payer the money came from.</summary>
    public required PayerId PayerId { get; init; }

    /// <summary>The business date the money was received; today in India when left out (BR-205).</summary>
    public DateOnly? PaymentDate { get; init; }

    /// <summary>How the money was received. Required.</summary>
    public PaymentMethod? Method { get; init; }

    /// <summary>The UPI id, card authorisation, UTR or cheque number. Required unless the method is cash (BR-301).</summary>
    public string? Reference { get; init; }

    /// <summary>The money received, above zero. Required.</summary>
    public decimal? Amount { get; init; }

    /// <summary>A note of the cashier.</summary>
    public string? Notes { get; init; }
}
