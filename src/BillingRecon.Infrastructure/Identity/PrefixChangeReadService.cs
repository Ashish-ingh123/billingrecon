using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>Reads the prefix changes of the current tenant; another tenant's are not there (ADR 0009).</summary>
public sealed class PrefixChangeReadService(BillingReconDbContext context) : IPrefixChangeReadService
{
    /// <inheritdoc />
    public async Task<PrefixChangeDto?> GetAsync(PrefixChangeId id, CancellationToken ct)
    {
        var change = await context.Set<PrefixChange>().AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == id, ct);

        return change?.ToDto();
    }

    /// <inheritdoc />
    public async Task<PageOf<PrefixChangeDto>> ListAsync(ListPrefixChangesQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var changes = context.Set<PrefixChange>().AsNoTracking();
        if (query.Status is { } status)
        {
            changes = changes.Where(change => change.Status == status);
        }

        if (query.DocumentType is { } documentType)
        {
            changes = changes.Where(change => change.DocumentType == documentType);
        }

        var totalCount = await changes.CountAsync(ct);
        var page = await changes
            .OrderByDescending(change => change.CreatedAtUtc)
            .ThenBy(change => change.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PageOf<PrefixChangeDto>([.. page.Select(change => change.ToDto())], query.Page, query.PageSize, totalCount);
    }
}
