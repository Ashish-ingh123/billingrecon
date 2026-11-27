using System.Text.Json.Serialization;

namespace BillingRecon.Api.Payments;

/// <summary>One denomination of the count of a closing.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CashCountLineRequest
{
    /// <summary>The value of the note or coin in rupees: 2000, 500, 200, 100, 50, 20, 10, 5, 2 or 1.</summary>
    public int? Denomination { get; init; }

    /// <summary>How many pieces of it the drawer holds.</summary>
    public int? Pieces { get; init; }
}
