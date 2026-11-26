using BillingRecon.Application.Identity;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>The stored images of the tenant's letterhead, through the context and its tenant filter (ADR 0059).</summary>
public sealed class TenantImageRepository(BillingReconDbContext context) : ITenantImageRepository
{
    /// <inheritdoc />
    public Task<TenantImage?> FindAsync(LetterheadImageKind kind, string sha256, CancellationToken ct)
        => context.Set<TenantImage>().AsNoTracking().SingleOrDefaultAsync(image => image.Kind == kind && image.Sha256 == sha256, ct);

    /// <inheritdoc />
    public void Add(TenantImage image) => context.Set<TenantImage>().Add(image);
}
