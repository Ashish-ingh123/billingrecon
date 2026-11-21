using System.Text.Json.Serialization;

namespace BillingRecon.Api.Payments;

/// <summary>The body of an approval of a reversal: an optional comment of the checker. The body itself may be left out.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApproveReversalRequest
{
    /// <summary>What the checker has to say, at most 500 characters.</summary>
    public string? Comment { get; init; }
}
