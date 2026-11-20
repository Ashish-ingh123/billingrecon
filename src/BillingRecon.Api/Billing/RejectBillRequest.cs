using System.Text.Json.Serialization;
using BillingRecon.Application.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Billing;

/// <summary>The body of <c>POST /api/v1/bills/{id}/reject</c>: why the checker rejects (BR-605).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RejectBillRequest
{
    /// <summary>The reason, required, at most 500 characters.</summary>
    public string? Comment { get; init; }

    /// <summary>The command for the bill <paramref name="id"/>, at the version the checker read.</summary>
    public RejectBillPostingCommand ToCommand(BillId id, string expectedVersion) => new(id, expectedVersion, Comment ?? string.Empty);
}
