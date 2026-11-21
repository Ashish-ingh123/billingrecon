using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>The prefix changes of the current tenant, tracked, for the use cases that change them (ADR 0033).</summary>
public sealed class PrefixChangeRepository(BillingReconDbContext context) : IPrefixChangeRepository
{
    /// <inheritdoc />
    public Task<PrefixChange?> GetAsync(PrefixChangeId id, CancellationToken ct)
        => context.Set<PrefixChange>().SingleOrDefaultAsync(change => change.Id == id, ct);

    /// <inheritdoc />
    public Task<bool> AnyPendingAsync(DocumentType documentType, CancellationToken ct)
        => context.Set<PrefixChange>().AnyAsync(change => change.DocumentType == documentType && change.Status == PrefixChangeStatus.Pending, ct);

    /// <inheritdoc />
    public void Add(PrefixChange change) => context.Set<PrefixChange>().Add(change);
}
