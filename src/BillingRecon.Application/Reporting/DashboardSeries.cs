using BillingRecon.Application.Receivables;

namespace BillingRecon.Application.Reporting;

/// <summary>
/// The figures of every business date of a window, from the ledger's balances before it and its movements of each day
/// (BR-902, ADR 0037): the balance at the end of a day is the balance before the window plus every movement up to that
/// day. Pending approvals at the end of a day in India are the requests asked for on or before it and not decided by it.
/// </summary>
public static class DashboardSeries
{
    /// <summary>One <see cref="DailyFiguresDto"/> for every date of <paramref name="window"/>, oldest first.</summary>
    public static IReadOnlyList<DailyFiguresDto> Daily(
        ReportPeriodDto window,
        LedgerBalances before,
        IReadOnlyList<LedgerDay> days,
        IReadOnlyList<ApprovalSpanDto> approvals,
        Func<DateTimeOffset, DateOnly> istDate)
    {
        var byDate = days.ToDictionary(day => day.BusinessDate);
        var spans = approvals
            .Select(span => (Requested: istDate(span.RequestedAtUtc), Decided: span.DecidedAtUtc is { } decided ? istDate(decided) : (DateOnly?)null))
            .ToList();

        var figures = new DailyFiguresDto[DashboardPeriods.Days(window)];
        var outstanding = before.Receivable;
        var unapplied = before.UnappliedCash;
        for (var index = 0; index < figures.Length; index++)
        {
            var date = window.From.AddDays(index);
            var day = byDate.GetValueOrDefault(date);
            outstanding += day?.ReceivableChange ?? 0m;
            unapplied += day?.UnappliedCashChange ?? 0m;
            var waiting = spans.Count(span => span.Requested <= date && (span.Decided is null || span.Decided > date));

            figures[index] = new DailyFiguresDto(date, day?.Billed ?? 0m, day?.Collected ?? 0m, outstanding, unapplied, waiting);
        }

        return figures;
    }

    /// <summary>The instants that bound every request that may have waited at the end of a day of <paramref name="window"/>, generously: the exact test is by date in India.</summary>
    public static (DateTimeOffset NotDecidedBefore, DateTimeOffset Before) ApprovalBounds(ReportPeriodDto window)
        => (UtcMidnight(window.From.AddDays(-1)), UtcMidnight(window.To.AddDays(2)));

    private static DateTimeOffset UtcMidnight(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
