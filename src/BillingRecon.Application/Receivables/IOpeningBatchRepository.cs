using BillingRecon.Domain.Common;
using BillingRecon.Domain.Receivables;

namespace BillingRecon.Application.Receivables;

/// <summary>The opening batches of the tenant, for the use cases that change them (ADR 0053).</summary>
public interface IOpeningBatchRepository
{
    /// <summary>The batch with that id, tracked; null when the tenant has none.</summary>
    public Task<OpeningBatch?> GetAsync(OpeningBatchId id, CancellationToken ct);

    /// <summary>
    /// Whether a batch of the tenant that waits or was approved has <paramref name="contentHash"/>. The filtered unique
    /// index is the last word; this answers the ordinary case without a failed save.
    /// </summary>
    public Task<bool> AnyLiveWithHashAsync(string contentHash, CancellationToken ct);

    /// <summary>Adds a new batch to the unit of work, which gives it its id.</summary>
    public void Add(OpeningBatch batch);
}
