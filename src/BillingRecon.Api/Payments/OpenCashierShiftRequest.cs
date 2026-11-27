using System.Text.Json.Serialization;
using BillingRecon.Application.Payments;

namespace BillingRecon.Api.Payments;

/// <summary>The body of <c>POST /api/v1/cashier-shifts</c>.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OpenCashierShiftRequest
{
    /// <summary>The cash the drawer holds at the opening: zero or more.</summary>
    public decimal? OpeningFloat { get; init; }

    /// <summary>The command of the use case.</summary>
    public OpenCashierShiftCommand ToCommand() => new(OpeningFloat);
}
