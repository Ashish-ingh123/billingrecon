using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.Infrastructure.Common.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// In the Development environment, makes sure the row of the development tenant exists (<see cref="DevelopmentIdentity"/>):
/// every tenant-scoped row refers to its tenant through a foreign key, so without the row nothing could be saved
/// (ADR 0015). It runs only where the database is migrated at startup, after the migrations (they run while the host
/// is starting, this when it starts), and does nothing when the row is there. Registered in Development only.
/// </summary>
public sealed class DevelopmentTenantSeeder(
    IServiceScopeFactory scopeFactory,
    IOptions<DatabaseOptions> database,
    IOptions<DevelopmentIdentityOptions> identity,
    IClock clock,
    ILogger<DevelopmentTenantSeeder> logger) : IHostedService
{
    /// <summary>The code of the development tenant.</summary>
    public const string TenantCode = "development";

    /// <summary>The name of the development tenant.</summary>
    public const string TenantName = "Development tenant";

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!database.Value.ApplyMigrationsOnStartup)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingReconDbContext>();
        var tenantId = new TenantId(identity.Value.TenantId);

        if (await EnsureTenantAsync(context, tenantId, clock.UtcNow, cancellationToken))
        {
            logger.LogInformation("Created the row of the development tenant {TenantId}", tenantId);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Adds the tenant row <paramref name="tenantId"/> unless <paramref name="context"/> already sees it. The context
    /// acts for that tenant or for no tenant: a tenant sees its own row only (ADR 0015).
    /// </summary>
    /// <returns>True when the row was added.</returns>
    public static async Task<bool> EnsureTenantAsync(BillingReconDbContext context, TenantId tenantId, DateTimeOffset createdAtUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (await context.Set<Tenant>().AnyAsync(tenant => tenant.Id == tenantId, ct))
        {
            return false;
        }

        var tenant = Tenant.Create(TenantCode, TenantName, createdAtUtc).Value;
        context.Entry(tenant).Property(candidate => candidate.Id).CurrentValue = tenantId;
        context.Add(tenant);
        await context.SaveChangesAsync(ct);

        return true;
    }
}
