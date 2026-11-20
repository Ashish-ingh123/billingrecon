using System.Text.Json.Serialization;
using BillingRecon.Application.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Billing;

/// <summary>
/// The body of <c>PUT /api/v1/bills/{id}/lines</c>: every line the draft is to have, in order. The lines it had are
/// replaced; an empty list removes them all. A member the API does not know is refused with 400.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReplaceBillLinesRequest
{
    /// <summary>The lines, in the order they appear on the bill.</summary>
    public IReadOnlyList<BillLineRequest?>? Lines { get; init; }

    /// <summary>The command for bill <paramref name="id"/>, at the version the client read.</summary>
    public ReplaceBillLinesCommand ToCommand(BillId id, string expectedVersion) => new()
    {
        Id = id,
        ExpectedVersion = expectedVersion,
        Lines = Lines.ToInputs(),
    };
}
