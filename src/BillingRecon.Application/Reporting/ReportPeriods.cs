namespace BillingRecon.Application.Reporting;

/// <summary>
/// The period of a register (ADR 0036): <c>from</c> and <c>to</c> together, both included, at most
/// <see cref="MaxDays"/> days; when neither is given, the current month in India.
/// </summary>
public static class ReportPeriods
{
    /// <summary>The longest period a register covers: a year, a leap year's included.</summary>
    public const int MaxDays = 366;

    /// <summary>The period asked for, or the calendar month of <paramref name="todayIst"/> when none was.</summary>
    public static ReportPeriodDto Resolve(DateOnly? from, DateOnly? to, DateOnly todayIst)
    {
        if (from is { } start && to is { } end)
        {
            return new ReportPeriodDto(start, end);
        }

        var firstOfMonth = new DateOnly(todayIst.Year, todayIst.Month, 1);

        return new ReportPeriodDto(firstOfMonth, firstOfMonth.AddMonths(1).AddDays(-1));
    }
}
