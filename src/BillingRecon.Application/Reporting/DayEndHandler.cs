using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reporting;

/// <summary>
/// The day-end report of a business date (US-48; BR-903), today in India unless another date is asked for. The
/// cashiers are named through the Identity contract, once each.
/// </summary>
public sealed class DayEndHandler(ICollectionReports reports, IUserNames userNames, IClock clock) : IQueryHandler<DayEndQuery, DayEndDto>
{
    /// <inheritdoc />
    public async Task<Result<DayEndDto>> HandleAsync(DayEndQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var report = await reports.DayEndAsync(query.Date ?? clock.TodayIst, ct);

        var names = new Dictionary<Guid, string?>();
        foreach (var cashier in report.Shifts.Select(shift => shift.CashierUserId).Distinct())
        {
            names[cashier] = await userNames.FindDisplayNameAsync(new UserId(cashier), ct);
        }

        return report with { Shifts = [.. report.Shifts.Select(shift => shift with { CashierName = names[shift.CashierUserId] })] };
    }
}
