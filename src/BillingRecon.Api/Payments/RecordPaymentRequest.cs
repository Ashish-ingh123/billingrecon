using System.Text.Json.Serialization;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Api.Payments;

/// <summary>The body of <c>POST /api/v1/payments</c> (BR-301). Unknown members are refused.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecordPaymentRequest
{
    /// <summary>The payer the money came from. Required.</summary>
    public Guid? PayerId { get; init; }

    /// <summary>The date the money was received, <c>yyyy-MM-dd</c>; today in India when left out.</summary>
    public DateOnly? PaymentDate { get; init; }

    /// <summary>Cash, Card, UPI, BankTransfer or Cheque. Required.</summary>
    public PaymentMethod? Method { get; init; }

    /// <summary>The UPI id, card authorisation, UTR or cheque number; required unless the method is Cash.</summary>
    public string? Reference { get; init; }

    /// <summary>The amount received, above zero, with at most two decimals. Required.</summary>
    public decimal? Amount { get; init; }

    /// <summary>A note, at most 500 characters.</summary>
    public string? Notes { get; init; }

    /// <summary>The command. A payer left out becomes the empty id, which the validator refuses.</summary>
    public RecordPaymentCommand ToCommand() => new()
    {
        PayerId = new PayerId(PayerId ?? Guid.Empty),
        PaymentDate = PaymentDate,
        Method = Method,
        Reference = Reference,
        Amount = Amount,
        Notes = Notes,
    };
}
