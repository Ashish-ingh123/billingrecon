using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payments;

/// <summary>The cashier counts the drawer and closes the shift (US-46; BR-313).</summary>
/// <param name="Id">The shift.</param>
/// <param name="ExpectedVersion">The version of the shift the cashier read (<c>If-Match</c>).</param>
/// <param name="Count">The pieces of each denomination; one left out has none.</param>
/// <param name="LooseChange">The cash below one rupee; none when left out.</param>
/// <param name="Reason">Why the count differs from the expected cash; required when it does.</param>
public sealed record CloseCashierShiftCommand(
    CashierShiftId Id,
    string ExpectedVersion,
    IReadOnlyList<CashCountInput>? Count,
    decimal? LooseChange,
    string? Reason);
