namespace BillingRecon.Application.Reporting;

/// <summary>Cash that came in and went out, and what is left of it: <paramref name="Net"/> is received less paid out.</summary>
/// <param name="Received">The cash payments recorded.</param>
/// <param name="PaidOut">The cash refunds, and the cash payments reversed.</param>
/// <param name="Net">Received less paid out.</param>
public sealed record DayEndCashDto(decimal Received, decimal PaidOut, decimal Net);
