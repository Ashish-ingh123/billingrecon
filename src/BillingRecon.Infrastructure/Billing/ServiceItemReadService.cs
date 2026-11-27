using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Billing;

/// <summary>Reads service items through the request's context, untracked and filtered by its tenant.</summary>
public sealed class ServiceItemReadService(BillingReconDbContext context) : IServiceItemReadService
{
    /// <inheritdoc />
    public async Task<ServiceItemDto?> GetAsync(ServiceItemId id, CancellationToken ct)
    {
        var item = await Items().SingleOrDefaultAsync(candidate => candidate.Id == id, ct);

        return item?.ToDto();
    }

    /// <inheritdoc />
    public async Task<PageOf<ServiceItemDto>> ListAsync(ListServiceItemsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var items = Items();

        if (query.Status is { } status)
        {
            items = items.Where(item => item.Status == status);
        }

        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            // The code is a value object stored through a converter, which has no Contains to translate: the cast
            // names its column, and LIKE compares the stored text. The search text is data, never a pattern.
            var pattern = LikePattern.Containing(search);
            items = items.Where(item =>
                EF.Functions.Like((string)(object)item.Code, pattern, LikePattern.Escape) || EF.Functions.Like(item.Name, pattern, LikePattern.Escape));
        }

        // The filters of Prompt 12.2 (ADR 0063): each narrows the rows the search and the page are taken from.
        if (query.Category is { } category)
        {
            items = items.Where(item => item.Category == category);
        }

        var totalCount = await items.CountAsync(ct);
        // The order asked for by name, or the list's own; each ends on a key that is one of a kind (ADR 0063).
        IOrderedQueryable<ServiceItem> ordered = query.Order switch
        {
            { Column: "code", Descending: var down } => items.By(item => (string)(object)item.Code, down).Then(item => item.Id, down),
            { Column: "name", Descending: var down } => items.By(item => item.Name, down).Then(item => item.Id, down),
            { Column: "defaultUnitPrice", Descending: var down } => items.By(item => item.DefaultUnitPrice, down).Then(item => item.Id, down),
            null => items
                .OrderBy(item => item.Code)
                .ThenBy(item => item.Id),
            _ => throw new InvalidOperationException($"The order {query.Order} is not one of this list; its validator should have refused it."),
        };
        var page = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PageOf<ServiceItemDto>([.. page.Select(item => item.ToDto())], query.Page, query.PageSize, totalCount);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ServiceItemDto>> GetManyAsync(IReadOnlyCollection<ServiceItemId> ids, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return [];
        }

        var found = await Items().Where(item => ids.Contains(item.Id)).ToListAsync(ct);

        return [.. found.Select(item => item.ToDto())];
    }

    /// <inheritdoc />
    public Task<bool> CodeExistsAsync(ServiceItemCode code, CancellationToken ct) => Items().AnyAsync(item => item.Code == code, ct);

    private IQueryable<ServiceItem> Items() => context.Set<ServiceItem>().AsNoTracking();
}
