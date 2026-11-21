using System.Text.Json.Serialization;

namespace BillingRecon.Api.Payments;

/// <summary>
/// The body of a request to reverse a payment or one of its allocations: why (BR-305, BR-306). Unknown members are
/// refused.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequestReversalRequest
{
    /// <summary>The reason, at most 500 characters. Required; the checker reads it, and it is kept with the reversal.</summary>
    public string? Reason { get; init; }
}
