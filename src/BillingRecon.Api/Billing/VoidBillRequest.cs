using System.Text.Json.Serialization;
using BillingRecon.Application.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Billing;

/// <summary>The body of <c>POST /api/v1/bills/{id}/void</c>: why the draft is voided (US-23). A member the API does not know is refused with 400.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record VoidBillRequest
{
    /// <summary>The reason. Required; it is kept with the bill.</summary>
    public string? Reason { get; init; }

    /// <summary>The command for bill <paramref name="id"/>, at the version the client read; a missing reason becomes empty, which the validator reports.</summary>
    public VoidBillCommand ToCommand(BillId id, string expectedVersion) => new(id, expectedVersion, Reason ?? string.Empty);
}
