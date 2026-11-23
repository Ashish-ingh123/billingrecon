using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Common.Numbering;

/// <summary>
/// A series of the current tenant, tracked, for an approved change of its prefix (ADR 0033). The approval holds the
/// series first (<see cref="INumberSeries.HoldAsync"/>), so no number is taken from it before the save; its row version
/// still guards the change.
/// </summary>
public sealed class NumberSeriesRepository(BillingReconDbContext context) : INumberSeriesRepository
{
    /// <inheritdoc />
    public Task<NumberSeries?> FindAsync(DocumentType documentType, FiscalYear fiscalYear, CancellationToken ct)
        => context.Set<NumberSeries>().SingleOrDefaultAsync(series => series.DocumentType == documentType && series.FiscalYear == fiscalYear, ct);
}
