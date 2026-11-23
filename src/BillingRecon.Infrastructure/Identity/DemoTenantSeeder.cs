using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// The bootstrap of the demo (ADR 0045 point 1): with <c>Demo:Enabled</c>, makes sure when the API starts that the demo
/// hospital and its administrator exist, the administrator able to sign in with <c>Demo:AdminPassword</c>. No API creates
/// a hospital or its first user, so the demo seed script needs these two rows to sign in and enter the rest through the
/// API. Does nothing when the demo is off or the rows are there. The database is migrated before the API starts.
/// </summary>
public sealed class DemoTenantSeeder(
    IServiceScopeFactory scopeFactory,
    IOptions<DemoOptions> demo,
    IPasswordHasher passwordHasher,
    IClock clock,
    ILogger<DemoTenantSeeder> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!demo.Value.Enabled)
        {
            return;
        }

        // No request is running, so the context is made here for the demo hospital and its administrator.
        await using var scope = scopeFactory.CreateAsyncScope();
        var identity = new DevelopmentIdentity(Options.Create(new DevelopmentIdentityOptions
        {
            TenantId = DemoOptions.TenantId,
            UserId = DemoOptions.AdminUserId,
        }));
        await using var context = new BillingReconDbContext(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<BillingReconDbContext>>(), identity, identity, clock);
        var tenantId = new TenantId(DemoOptions.TenantId);
        var userId = new UserId(DemoOptions.AdminUserId);

        if (!await context.Set<Tenant>().AnyAsync(tenant => tenant.Id == tenantId, cancellationToken))
        {
            var tenant = Tenant.Create(DemoOptions.TenantCode, DemoOptions.TenantName, clock.UtcNow).Value;
            context.Entry(tenant).Property(candidate => candidate.Id).CurrentValue = tenantId;
            context.Add(tenant);
            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Created the demo hospital {TenantId}", tenantId);
        }

        if (!await context.Set<User>().AnyAsync(user => user.Id == userId, cancellationToken))
        {
            var user = User.Create(
                DemoOptions.AdminEmail,
                DemoOptions.AdminDisplayName,
                passwordHasher.Hash(demo.Value.AdminPassword!),
                [Role.TenantAdmin],
                clock.UtcNow,
                grantedBy: null);
            context.Entry(user).Property(candidate => candidate.Id).CurrentValue = userId;
            context.Add(user);
            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Created the demo administrator {UserId}", userId);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
