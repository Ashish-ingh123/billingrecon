using System.Text.Json.Serialization;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Api.Payments;

/// <summary>The body of <c>POST /api/v1/refunds</c>: one source, the amount and how it is paid back. Unknown members are refused.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateRefundRequest
{
    /// <summary>The payment whose unapplied cash is paid back. Give this or <see cref="CreditNoteId"/>, not both.</summary>
    public Guid? PaymentId { get; init; }

    /// <summary>The credit note whose available credit is paid back. Give this or <see cref="PaymentId"/>, not both.</summary>
    public Guid? CreditNoteId { get; init; }

    /// <summary><c>yyyy-MM-dd</c>; today in India when left out.</summary>
    public DateOnly? RefundDate { get; init; }

    /// <summary>Above zero, at most two decimals.</summary>
    public decimal? Amount { get; init; }

    /// <summary><c>Cash</c>, <c>Card</c>, <c>UPI</c>, <c>BankTransfer</c> or <c>Cheque</c>.</summary>
    public PaymentMethod? Method { get; init; }

    /// <summary>The UPI id, card authorisation, UTR or cheque number of the payout; required unless the method is cash.</summary>
    public string? Reference { get; init; }

    /// <summary>Why the money is paid back, at most 500 characters.</summary>
    public string? Reason { get; init; }

    /// <summary>The command the request stands for.</summary>
    public CreateRefundDraftCommand ToCommand() => new()
    {
        PaymentId = PaymentId is { } paymentId ? new PaymentId(paymentId) : null,
        CreditNoteId = CreditNoteId is { } creditNoteId ? new CreditNoteId(creditNoteId) : null,
        RefundDate = RefundDate,
        Amount = Amount,
        Method = Method,
        Reference = Reference,
        Reason = Reason ?? string.Empty,
    };
}
