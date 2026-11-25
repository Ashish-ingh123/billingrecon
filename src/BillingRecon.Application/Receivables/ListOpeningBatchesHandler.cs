using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Receivables;

/// <summary>Lists the opening batches of the tenant, newest first (ADR 0053).</summary>
public sealed class ListOpeningBatchesHandler(IValidator<ListOpeningBatchesQuery> validator, IOpeningBatchReadService batches)
    : IQueryHandler<ListOpeningBatchesQuery, PageOf<OpeningBatchDto>>
{
    /// <inheritdoc />
    public async Task<Result<PageOf<OpeningBatchDto>>> HandleAsync(ListOpeningBatchesQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (await validator.GetErrorAsync(query, ct) is { } invalid)
        {
            return invalid;
        }

        return await batches.ListAsync(query, ct);
    }
}
