using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.IntegrationTests.Support;

/// <summary>
/// The number series with the taking of a number of one document type gated: the series is locked from the first
/// number on until the commit, and every use case that takes a number has read its documents before it does.
/// </summary>
public sealed class RaceGatedNumberSeries(INumberSeries inner, RaceGate gate, DocumentType gated) : INumberSeries
{
    /// <inheritdoc />
    public Task HoldAsync(DocumentType type, FiscalYear fiscalYear, CancellationToken ct) => inner.HoldAsync(type, fiscalYear, ct);

    /// <inheritdoc />
    public Task<DocumentNumber> NextAsync(DocumentType type, FiscalYear fiscalYear, CancellationToken ct)
        => type == gated
            ? gate.AfterAsync(() => inner.NextAsync(type, fiscalYear, ct), ct)
            : inner.NextAsync(type, fiscalYear, ct);
}
