using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Persistence;

/// <summary>
/// Creates contexts for tests that need no database. They use the real SQL Server provider (the in-memory provider
/// is banned) with a connection string nothing listens on: building the model, tracking entities and generating
/// SQL text never open a connection. The contexts are built by hand, as seeding code would build one, so they also
/// show that the guards belong to the context and not to the dependency-injection set-up.
/// </summary>
internal static class TestContexts
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 6, 30, 0, TimeSpan.Zero);

    /// <summary>Options for a context on the unreachable server: enough to build the model, track entities and generate SQL.</summary>
    public static DbContextOptions<TContext> Options<TContext>()
        where TContext : DbContext
        => Options<TContext>(ApiFactory.UnreachableSqlServer);

    /// <summary>Options for a context on <paramref name="connectionString"/>, for the tests that have a database.</summary>
    public static DbContextOptions<TContext> Options<TContext>(string connectionString)
        where TContext : DbContext
        => new DbContextOptionsBuilder<TContext>().UseSqlServer(connectionString).Options;

    /// <summary>The production context with the probe entities, for the tenant and user of <paramref name="identity"/>.</summary>
    public static TestDbContext Create<TIdentity>(TIdentity identity, IClock? clock = null)
        where TIdentity : ITenantContext, ICurrentUser
        => new(Options<TestDbContext>(), identity, identity, clock ?? new FakeClock(Now));

    /// <summary>The production context plus what <typeparamref name="TProbe"/> adds to the model, with a random tenant and user.</summary>
    public static ModelProbeContext<TProbe> CreateProbe<TProbe>()
        where TProbe : IModelProbe, new()
    {
        var identity = FixedIdentity.Random();

        return new(Options<ModelProbeContext<TProbe>>(), identity, identity, new FakeClock(Now));
    }

    /// <summary>The production context exactly as it ships, with no tenant and no user (system work).</summary>
    public static BillingReconDbContext CreateProduction()
        => CreateProduction(SystemIdentity.Instance);

    /// <summary>The production context exactly as it ships, for the tenant and user of <paramref name="identity"/>.</summary>
    public static BillingReconDbContext CreateProduction<TIdentity>(TIdentity identity)
        where TIdentity : ITenantContext, ICurrentUser
        => new(Options<BillingReconDbContext>(), identity, identity, new FakeClock(Now));

    /// <summary>
    /// Tracks <paramref name="entity"/> as a row that already exists in the database and belongs to
    /// <paramref name="tenantId"/>: it gets an id, the tenant, and the state Unchanged.
    /// </summary>
    public static TEntity TrackExisting<TEntity>(this DbContext context, TEntity entity, TenantId tenantId)
        where TEntity : class, ITenantScoped
    {
        var entry = context.Entry(entity);
        var id = entry.Property("Id");

        id.CurrentValue = Activator.CreateInstance(id.Metadata.ClrType, Guid.NewGuid());
        entry.Property(nameof(ITenantScoped.TenantId)).CurrentValue = tenantId;
        entry.State = EntityState.Unchanged;

        return entity;
    }
}
