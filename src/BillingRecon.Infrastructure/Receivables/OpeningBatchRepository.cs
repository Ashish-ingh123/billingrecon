using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Receivables;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Receivables;

/// <summary>The opening batches of the tenant, through the context and its tenant filter (ADR 0053).</summary>
public sealed class OpeningBatchRepository(BillingReconDbContext context) : IOpeningBatchRepository
{
    /// <inheritdoc />
    public Task<OpeningBatch?> GetAsync(OpeningBatchId id, CancellationToken ct)
        => context.Set<OpeningBatch>().SingleOrDefaultAsync(batch => batch.Id == id, ct);

    /// <inheritdoc />
    public Task<bool> AnyLiveWithHashAsync(string contentHash, CancellationToken ct)
        => context.Set<OpeningBatch>().AnyAsync(batch => batch.ContentHash == contentHash && batch.Status != OpeningBatchStatus.Rejected, ct);

    /// <inheritdoc />
    public void Add(OpeningBatch batch) => context.Set<OpeningBatch>().Add(batch);
}
