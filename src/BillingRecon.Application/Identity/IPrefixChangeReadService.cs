using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Identity;

/// <summary>Reads the prefix changes of the current tenant (ADR 0033).</summary>
public interface IPrefixChangeReadService
{
    /// <summary>One change; null when the tenant has none with that id.</summary>
    public Task<PrefixChangeDto?> GetAsync(PrefixChangeId id, CancellationToken ct);

    /// <summary>One page of changes, the newest first.</summary>
    public Task<PageOf<PrefixChangeDto>> ListAsync(ListPrefixChangesQuery query, CancellationToken ct);
}
