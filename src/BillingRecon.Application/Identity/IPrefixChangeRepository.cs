using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>The prefix changes of the current tenant (ADR 0033).</summary>
public interface IPrefixChangeRepository
{
    /// <summary>The change, tracked; null when the tenant has none with that id.</summary>
    public Task<PrefixChange?> GetAsync(PrefixChangeId id, CancellationToken ct);

    /// <summary>Whether a change of <paramref name="documentType"/> waits for a decision.</summary>
    public Task<bool> AnyPendingAsync(DocumentType documentType, CancellationToken ct);

    /// <summary>Adds a new change to the unit of work; its id is assigned here.</summary>
    public void Add(PrefixChange change);
}
