using System.Text.Json.Serialization;

namespace BillingRecon.Api.Billing;

/// <summary>The body of a request to reverse an application of credit: why (ADR 0028). Unknown members are refused.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReverseCreditApplicationRequest
{
    /// <summary>The reason, at most 500 characters. Required; the checker reads it, and it is kept with the reversal.</summary>
    public string? Reason { get; init; }
}
