using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>Lists the prefix changes of the current tenant one page at a time, the newest first.</summary>
public sealed class ListPrefixChangesHandler(IValidator<ListPrefixChangesQuery> validator, IPrefixChangeReadService changes)
    : IQueryHandler<ListPrefixChangesQuery, PageOf<PrefixChangeDto>>
{
    /// <inheritdoc />
    public async Task<Result<PageOf<PrefixChangeDto>>> HandleAsync(ListPrefixChangesQuery query, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(query, ct) is { } invalid)
        {
            return invalid;
        }

        return await changes.ListAsync(query, ct);
    }
}
