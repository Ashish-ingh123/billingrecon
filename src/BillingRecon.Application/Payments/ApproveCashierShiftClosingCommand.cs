using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payments;

/// <summary>A checker approves the closing of a shift with its difference (US-47; BR-314).</summary>
/// <param name="Id">The shift.</param>
/// <param name="ExpectedVersion">The version of the shift the checker read (<c>If-Match</c>).</param>
/// <param name="Comment">What the checker has to say; optional.</param>
public sealed record ApproveCashierShiftClosingCommand(CashierShiftId Id, string ExpectedVersion, string? Comment);
