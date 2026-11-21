using System.Text.Json.Serialization;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Api.Payments;

/// <summary>The body of <c>PUT /api/v1/refunds/{id}</c>: every value of the draft again; the source cannot change. Unknown members are refused.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateRefundRequest
{
    /// <summary><c>yyyy-MM-dd</c>. Required.</summary>
    public DateOnly? RefundDate { get; init; }

    /// <summary>Above zero, at most two decimals. Required.</summary>
    public decimal? Amount { get; init; }

    /// <summary>How the money is paid back. Required.</summary>
    public PaymentMethod? Method { get; init; }

    /// <summary>The reference of the payout; required unless the method is cash.</summary>
    public string? Reference { get; init; }

    /// <summary>Why, at most 500 characters. Required.</summary>
    public string? Reason { get; init; }

    /// <summary>The command for refund <paramref name="id"/>, at the version the client read.</summary>
    public UpdateRefundCommand ToCommand(RefundId id, string expectedVersion) => new(id, expectedVersion, RefundDate, Amount, Method, Reference, Reason ?? string.Empty);
}
