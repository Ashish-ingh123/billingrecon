namespace BillingRecon.Application.Reporting;

/// <summary>
/// The trend of the dashboard (US-44, BR-902): billed and collected per day or per week of a period, cumulative within it,
/// and the daily figures of the cards' sparklines.
/// </summary>
public sealed record DashboardTrendQuery
{
    /// <summary>The longest period answered per day when no interval is asked for.</summary>
    public const int DailyUpToDays = 31;

    /// <summary>The first business date, with <see cref="To"/>; neither means the last 30 days ending today.</summary>
    public DateOnly? From { get; init; }

    /// <summary>The last business date, with <see cref="From"/>; today at the latest.</summary>
    public DateOnly? To { get; init; }

    /// <summary>Per day or per week; per day up to <see cref="DailyUpToDays"/> days and per week beyond when left out.</summary>
    public TrendInterval? Interval { get; init; }
}
