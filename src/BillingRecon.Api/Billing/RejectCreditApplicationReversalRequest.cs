using System.Text.Json.Serialization;

namespace BillingRecon.Api.Billing;

/// <summary>The body of a rejection of the reversal of an application of credit: why the checker rejects (BR-605). Unknown members are refused.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RejectCreditApplicationReversalRequest
{
    /// <summary>The reason. Required; the cashier reads it.</summary>
    public string? Comment { get; init; }
}
