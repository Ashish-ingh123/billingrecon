using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>Reads one prefix change; another tenant's answers as one nobody has.</summary>
public sealed class GetPrefixChangeHandler(IPrefixChangeReadService changes) : IQueryHandler<GetPrefixChangeQuery, PrefixChangeDto>
{
    /// <inheritdoc />
    public async Task<Result<PrefixChangeDto>> HandleAsync(GetPrefixChangeQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var change = await changes.GetAsync(query.Id, ct);

        return change is null ? IdentityErrors.PrefixChangeNotFound() : change;
    }
}
