using BillingRecon.Application.Common;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Receivables;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Receivables;

/// <summary>
/// Reads the opening batches (ADR 0053) without their files: only the columns an answer holds are read, so a list does
/// not carry megabytes of CSV it would never show.
/// </summary>
public sealed class OpeningBatchReadService(BillingReconDbContext context) : IOpeningBatchReadService
{
    /// <inheritdoc />
    public async Task<OpeningBatchDto?> GetAsync(OpeningBatchId id, CancellationToken ct)
        => (await ReadAsync(context.Set<OpeningBatch>().AsNoTracking().Where(batch => batch.Id == id), ct)).SingleOrDefault();

    /// <inheritdoc />
    public async Task<PageOf<OpeningBatchDto>> ListAsync(ListOpeningBatchesQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var batches = context.Set<OpeningBatch>().AsNoTracking();
        if (query.Status is { } status)
        {
            batches = batches.Where(batch => batch.Status == status);
        }

        var totalCount = await batches.CountAsync(ct);
        var page = batches
            .OrderByDescending(batch => batch.CreatedAtUtc)
            .ThenBy(batch => batch.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize);

        return new PageOf<OpeningBatchDto>(await ReadAsync(page, ct), query.Page, query.PageSize, totalCount);
    }

    private static async Task<List<OpeningBatchDto>> ReadAsync(IQueryable<OpeningBatch> batches, CancellationToken ct)
    {
        var rows = await batches
            .Select(batch => new
            {
                batch.Id,
                batch.CutOverDate,
                batch.Status,
                batch.Receivables,
                batch.ReceivableTotal,
                batch.Advances,
                batch.AdvanceTotal,
                batch.CreatedAtUtc,
                batch.CreatedByUserId,
                batch.DecidedAtUtc,
                batch.RowVersion,
            })
            .ToListAsync(ct);

        return
        [
            .. rows.Select(row => new OpeningBatchDto(
                row.Id.Value,
                row.CutOverDate,
                row.Status,
                row.Receivables,
                row.ReceivableTotal.Amount,
                row.Advances,
                row.AdvanceTotal.Amount,
                row.CreatedAtUtc,
                row.CreatedByUserId?.Value,
                row.DecidedAtUtc,
                Convert.ToBase64String(row.RowVersion))),
        ];
    }
}
