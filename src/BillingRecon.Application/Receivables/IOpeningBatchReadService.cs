using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Receivables;

/// <summary>The opening batches of the tenant, for the queries (ADR 0053). Nothing it answers holds a file.</summary>
public interface IOpeningBatchReadService
{
    /// <summary>The batch with that id; null when the tenant has none.</summary>
    public Task<OpeningBatchDto?> GetAsync(OpeningBatchId id, CancellationToken ct);

    /// <summary>A page of the batches, newest first.</summary>
    public Task<PageOf<OpeningBatchDto>> ListAsync(ListOpeningBatchesQuery query, CancellationToken ct);
}
