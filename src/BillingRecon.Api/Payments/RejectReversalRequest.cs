using System.Text.Json.Serialization;

namespace BillingRecon.Api.Payments;

/// <summary>The body of a rejection of a reversal: why the checker rejects (BR-605). Unknown members are refused.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RejectReversalRequest
{
    /// <summary>The reason. Required; the cashier reads it.</summary>
    public string? Comment { get; init; }
}
