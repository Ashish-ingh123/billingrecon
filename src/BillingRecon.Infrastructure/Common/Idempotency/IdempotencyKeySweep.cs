using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Common.Idempotency;

/// <summary>
/// Deletes the idempotency keys whose time has passed (ADR 0006, amended 2026-11-26; review 2026-11, finding R3): a
/// key is kept for <see cref="IdempotencyKey.Retention"/> and its row, with the stored answer, is of no use after
/// that. This is the system's housekeeping, not the work of a user: it goes through the tenants one by one, each in a
/// context of that tenant, so that every statement carries the tenant as every other statement of the application
/// does, and is answered by the index on the tenant and the expiry.
/// </summary>
public sealed class IdempotencyKeySweep(DbContextOptions<BillingReconDbContext> options, IClock clock)
{
    /// <summary>How many keys one save deletes, so that no transaction holds its locks for long.</summary>
    public const int BatchSize = 500;

    /// <summary>Deletes every key, of every tenant, that has expired by now.</summary>
    /// <param name="ct">Cancels the work between two batches.</param>
    /// <returns>How many keys were deleted.</returns>
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var deleted = 0;

        foreach (var tenantId in await TenantsAsync(ct))
        {
            deleted += await SweepAsync(tenantId, now, ct);
        }

        return deleted;
    }

    private async Task<List<TenantId>> TenantsAsync(CancellationToken ct)
    {
        await using var system = new BillingReconDbContext(options, SystemIdentity.Instance, SystemIdentity.Instance, clock);

        return await system.Set<Tenant>().AsNoTracking().Select(tenant => tenant.Id).ToListAsync(ct);
    }

    private async Task<int> SweepAsync(TenantId tenantId, DateTimeOffset now, CancellationToken ct)
    {
        var identity = new OperatorIdentity(tenantId);
        var deleted = 0;

        while (true)
        {
            // A context for each batch: what was deleted is not kept in memory.
            await using var context = new BillingReconDbContext(options, identity, identity, clock);
            var expired = await context.Set<IdempotencyKey>()
                .Where(key => key.ExpiresAtUtc < now)
                .OrderBy(key => key.ExpiresAtUtc)
                .Take(BatchSize)
                .ToListAsync(ct);

            if (expired.Count == 0)
            {
                return deleted;
            }

            context.RemoveRange(expired);

            try
            {
                deleted += await context.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another instance of the application swept the same rows; the next batch reads what is left.
            }

            if (expired.Count < BatchSize)
            {
                return deleted;
            }
        }
    }
}
