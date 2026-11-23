namespace BillingRecon.Application.Reporting;

/// <summary>
/// The trend of the dashboard: the period, the interval of its points, billed against collected per point, and the figures
/// of every day of the period for the cards' sparklines.
/// </summary>
public sealed record DashboardTrendDto(ReportPeriodDto Period, TrendInterval Interval, IReadOnlyList<TrendPointDto> Points, IReadOnlyList<DailyFiguresDto> Daily);
