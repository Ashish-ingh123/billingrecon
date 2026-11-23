using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reporting;

/// <summary>
/// The period of the dashboard (BR-902): <c>from</c> and <c>to</c>, business dates in India, both included, given together
/// or not at all; the last <see cref="DefaultDays"/> days ending today when neither is; at most
/// <see cref="ReportPeriods.MaxDays"/> days (the validators' rule), and ending today at the latest. The previous period is
/// the one of the same length that ends the day before it starts.
/// </summary>
public static class DashboardPeriods
{
    /// <summary>The days the dashboard covers when no period is asked for.</summary>
    public const int DefaultDays = 30;

    /// <summary>The message of a period that ends after today.</summary>
    public const string EndsAfterTodayMessage = "The period ends today in India at the latest.";

    /// <summary>The period asked for, or the last <see cref="DefaultDays"/> days; refused when it ends after <paramref name="todayIst"/>.</summary>
    public static Result<ReportPeriodDto> Resolve(DateOnly? from, DateOnly? to, DateOnly todayIst)
    {
        if (from is { } start && to is { } end)
        {
            return end > todayIst
                ? CommonErrors.ValidationFailed(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["to"] = [EndsAfterTodayMessage] })
                : new ReportPeriodDto(start, end);
        }

        return new ReportPeriodDto(todayIst.AddDays(1 - DefaultDays), todayIst);
    }

    /// <summary>How many days <paramref name="period"/> covers.</summary>
    public static int Days(ReportPeriodDto period)
    {
        ArgumentNullException.ThrowIfNull(period);

        return period.To.DayNumber - period.From.DayNumber + 1;
    }

    /// <summary>The period of the same length that ends the day before <paramref name="period"/> starts.</summary>
    public static ReportPeriodDto Previous(ReportPeriodDto period)
    {
        ArgumentNullException.ThrowIfNull(period);

        return new ReportPeriodDto(period.From.AddDays(-Days(period)), period.From.AddDays(-1));
    }
}
