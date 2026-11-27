namespace BillingRecon.Application.Payments;

/// <summary>Opens a shift for the user who asks, with the cash the drawer holds (US-45; BR-310).</summary>
/// <param name="OpeningFloat">The float: zero or more.</param>
public sealed record OpenCashierShiftCommand(decimal? OpeningFloat);
