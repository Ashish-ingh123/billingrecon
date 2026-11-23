namespace BillingRecon.Application.Reporting;

/// <summary>The steps of the dashboard's trend: business dates, or weeks from Monday to Sunday.</summary>
public enum TrendInterval
{
    /// <summary>One point per business date.</summary>
    Day = 0,

    /// <summary>One point per week, Monday to Sunday; the first and the last may be shorter, cut by the period.</summary>
    Week = 1,
}
