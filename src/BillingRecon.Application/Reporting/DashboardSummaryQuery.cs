namespace BillingRecon.Application.Reporting;

/// <summary>The six cards of the dashboard for a period, each with the figure of the previous period (US-44, BR-902).</summary>
public sealed record DashboardSummaryQuery
{
    /// <summary>The first business date, with <see cref="To"/>; neither means the last 30 days ending today.</summary>
    public DateOnly? From { get; init; }

    /// <summary>The last business date, with <see cref="From"/>; today at the latest.</summary>
    public DateOnly? To { get; init; }
}
