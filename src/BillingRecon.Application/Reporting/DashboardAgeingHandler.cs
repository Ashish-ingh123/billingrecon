using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reporting;

/// <summary>
/// The ageing of the dashboard (US-44, BR-902): every posted bill that owes something, by how many days its due date is
/// behind today in India. A bill not yet due, or due today, is in <c>0-30</c>.
/// </summary>
public sealed class DashboardAgeingHandler(IDashboardReports reports, IClock clock) : IQueryHandler<DashboardAgeingQuery, AgeingDto>
{
    /// <summary>The buckets: a name and the days past due it holds, both included.</summary>
    public static readonly IReadOnlyList<(string Name, int FromDays, int ToDays)> Buckets =
    [
        ("0-30", int.MinValue, 30),
        ("31-60", 31, 60),
        ("61-90", 61, 90),
        ("90+", 91, int.MaxValue),
    ];

    /// <inheritdoc />
    public async Task<Result<AgeingDto>> HandleAsync(DashboardAgeingQuery query, CancellationToken ct)
    {
        var today = clock.TodayIst;
        var bills = await reports.OpenBillsAsync(ct);
        var total = bills.Sum(bill => bill.BalanceDue);

        AgeingBucketDto[] buckets =
        [
            .. Buckets.Select(bucket =>
            {
                var inside = bills.Where(bill => today.DayNumber - bill.DueDate.DayNumber is var days && days >= bucket.FromDays && days <= bucket.ToDays).ToList();
                var amount = inside.Sum(bill => bill.BalanceDue);

                return new AgeingBucketDto(bucket.Name, inside.Count, amount, DashboardFigures.Share(amount, total));
            }),
        ];

        return new AgeingDto(today, buckets, bills.Count, total);
    }
}
