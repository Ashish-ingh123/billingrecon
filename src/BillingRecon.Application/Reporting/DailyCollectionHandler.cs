using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reporting;

/// <summary>The collection of a business date by method (US-42), today in India unless another date is asked for.</summary>
public sealed class DailyCollectionHandler(ICollectionReports reports, IClock clock) : IQueryHandler<DailyCollectionQuery, DailyCollectionDto>
{
    /// <inheritdoc />
    public async Task<Result<DailyCollectionDto>> HandleAsync(DailyCollectionQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await reports.DailyCollectionAsync(query.Date ?? clock.TodayIst, ct);
    }
}
