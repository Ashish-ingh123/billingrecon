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
/// In the Development environment, makes sure the row of the development tenant and the row of the development user
/// exist (<see cref="DevelopmentIdentity"/>): every tenant-scoped row refers to its tenant, and every column that names a
/// user refers to that user, through foreign keys, so without the two rows nothing could be saved (ADR 0015,
/// ADR 0030). It runs only where the database is migrated at startup, after the migrations (they run while the host is
/// starting, this when it starts), and does nothing when the rows are there. Registered in Development only.
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

    /// <summary>The address of the development user.</summary>
    public const string UserEmail = "admin@development.local";

    /// <summary>The display name of the development user.</summary>
    public const string UserDisplayName = "Development administrator";

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!database.Value.ApplyMigrationsOnStartup)
        {
            return;
        }

        // No request is running, so the context is made here for the development tenant and user (ADR 0031, point 9).
        await using var scope = scopeFactory.CreateAsyncScope();
        var development = new DevelopmentIdentity(identity);
        await using var context = new BillingReconDbContext(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<BillingReconDbContext>>(), development, development, clock);
        var tenantId = new TenantId(identity.Value.TenantId);
        var userId = new UserId(identity.Value.UserId);

        if (await EnsureTenantAsync(context, tenantId, clock.UtcNow, cancellationToken))
        {
            logger.LogInformation("Created the row of the development tenant {TenantId}", tenantId);
        }

        if (await EnsureUserAsync(context, userId, clock.UtcNow, cancellationToken))
        {
            logger.LogInformation("Created the row of the development user {UserId}", userId);
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

    /// <summary>
    /// Adds the development user <paramref name="userId"/>, a tenant administrator, unless <paramref name="context"/>
    /// already sees it. Its password hash matches no password (<see cref="Pbkdf2PasswordHasher.Unusable"/>): nobody signs
    /// in as it until a password is set (ADR 0030). The context acts for the development tenant.
    /// </summary>
    /// <returns>True when the row was added.</returns>
    public static async Task<bool> EnsureUserAsync(BillingReconDbContext context, UserId userId, DateTimeOffset createdAtUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (await context.Set<User>().AnyAsync(user => user.Id == userId, ct))
        {
            return false;
        }

        var user = User.Create(UserEmail, UserDisplayName, Pbkdf2PasswordHasher.Unusable, [Role.TenantAdmin], createdAtUtc, grantedBy: null);
        context.Entry(user).Property(candidate => candidate.Id).CurrentValue = userId;
        context.Add(user);
        await context.SaveChangesAsync(ct);

        return true;
    }
}
