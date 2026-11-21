using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Common;

/// <summary>
/// A series of the current tenant, to change its prefix (ADR 0033). Numbers are never taken through it:
/// <see cref="INumberSeries"/> does that, under its lock (ADR 0008).
/// </summary>
public interface INumberSeriesRepository
{
    /// <summary>The series of <paramref name="documentType"/> for <paramref name="fiscalYear"/>, tracked; null when no number of it was taken yet.</summary>
    public Task<NumberSeries?> FindAsync(DocumentType documentType, FiscalYear fiscalYear, CancellationToken ct);
}
