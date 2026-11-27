using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payments;

/// <summary>A checker rejects the closing of a shift, saying why (US-47; BR-605).</summary>
/// <param name="Id">The shift.</param>
/// <param name="ExpectedVersion">The version of the shift the checker read (<c>If-Match</c>).</param>
/// <param name="Comment">Why; required.</param>
public sealed record RejectCashierShiftClosingCommand(CashierShiftId Id, string ExpectedVersion, string? Comment);
