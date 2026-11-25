using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Receivables;

namespace BillingRecon.Application.Receivables;

/// <summary>Reads one opening batch (ADR 0053).</summary>
public sealed class GetOpeningBatchHandler(IOpeningBatchReadService batches) : IQueryHandler<GetOpeningBatchQuery, OpeningBatchDto>
{
    /// <inheritdoc />
    public async Task<Result<OpeningBatchDto>> HandleAsync(GetOpeningBatchQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var batch = await batches.GetAsync(query.Id, ct);

        return batch is null ? ReceivablesErrors.OpeningBatchNotFound() : batch;
    }
}
