using System.Text.Json.Serialization;

namespace BillingRecon.Api.Billing;

/// <summary>The body of an approval of the reversal of an application of credit: an optional comment. The body itself may be left out.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApproveCreditApplicationReversalRequest
{
    /// <summary>What the checker has to say, at most 500 characters.</summary>
    public string? Comment { get; init; }
}
