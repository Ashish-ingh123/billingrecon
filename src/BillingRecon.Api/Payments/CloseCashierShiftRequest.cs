using System.Text.Json.Serialization;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Payments;

/// <summary>The body of <c>POST /api/v1/cashier-shifts/{id}/close</c>.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CloseCashierShiftRequest
{
    /// <summary>The pieces of each denomination; one left out has none.</summary>
    public IReadOnlyList<CashCountLineRequest?>? Count { get; init; }

    /// <summary>The cash below one rupee; none when left out.</summary>
    public decimal? LooseChange { get; init; }

    /// <summary>Why the count differs from the expected cash; required when it does.</summary>
    public string? Reason { get; init; }

    /// <summary>The command of the use case.</summary>
    public CloseCashierShiftCommand ToCommand(CashierShiftId id, string expectedVersion) => new(
        id,
        expectedVersion,
        Count is null ? null : [.. Count.Select(line => new CashCountInput(line?.Denomination, line?.Pieces))],
        LooseChange,
        Reason);
}
