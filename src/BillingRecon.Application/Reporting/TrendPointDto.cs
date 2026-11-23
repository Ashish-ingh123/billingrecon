namespace BillingRecon.Application.Reporting;

/// <summary>
/// One step of the trend, a day or a week, <see cref="From"/> to <see cref="To"/>: what was billed and collected in it,
/// and since the period's start up to its end.
/// </summary>
public sealed record TrendPointDto(DateOnly From, DateOnly To, decimal Billed, decimal Collected, decimal CumulativeBilled, decimal CumulativeCollected);
