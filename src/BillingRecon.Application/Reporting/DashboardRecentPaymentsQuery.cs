namespace BillingRecon.Application.Reporting;

/// <summary>The newest payments of the dashboard's period (US-44, BR-902): the payment register, newest first, first page.</summary>
public sealed record DashboardRecentPaymentsQuery
{
    /// <summary>How many payments the panel shows.</summary>
    public const int Count = 5;

    /// <summary>The first business date, with <see cref="To"/>; neither means the last 30 days ending today.</summary>
    public DateOnly? From { get; init; }

    /// <summary>The last business date, with <see cref="From"/>; today at the latest.</summary>
    public DateOnly? To { get; init; }
}
