namespace BillingRecon.Application.Reporting;

/// <summary>The day-end report of one business date (US-48; BR-903): every shift and the cash outside them.</summary>
public sealed record DayEndQuery
{
    /// <summary>The business date in India; today when left out.</summary>
    public DateOnly? Date { get; init; }
}
