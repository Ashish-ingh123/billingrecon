using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>The stored images of the current tenant's letterhead (ADR 0059): found by their hash, and added to; never changed.</summary>
public interface ITenantImageRepository
{
    /// <summary>The image of <paramref name="kind"/> whose bytes have the hash <paramref name="sha256"/>, not tracked; null when none is stored.</summary>
    public Task<TenantImage?> FindAsync(LetterheadImageKind kind, string sha256, CancellationToken ct);

    /// <summary>Adds a new image to the unit of work.</summary>
    public void Add(TenantImage image);
}
