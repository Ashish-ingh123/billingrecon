using BillingRecon.Application.Common;
using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.IntegrationTests.Persistence;
using BillingRecon.IntegrationTests.Support;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// What every test class of the Database collection shares: the SQL Server fixture, empty tables before each test,
/// and contexts on either database for a given identity and clock. The contexts are built by hand, as the persistence
/// tests without a database build them, so the guards the context attaches to itself are in force here too. Every
/// tenant-scoped row refers to its tenant's row through a foreign key (ADR 0015), so a test adds the tenant rows it
/// needs first.
/// </summary>
public abstract class DatabaseTest(SqlServerFixture sqlServer) : IAsyncLifetime
{
    /// <summary>The instant the clocks of these tests start at.</summary>
    protected static readonly DateTimeOffset Now = new(2026, 10, 17, 6, 30, 0, TimeSpan.Zero);

    /// <summary>The SQL Server of the test run.</summary>
    protected SqlServerFixture SqlServer { get; } = sqlServer;

    /// <inheritdoc />
    public Task InitializeAsync() => SqlServer.ResetAsync();

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>The test context, probe entities included, on the probe database, for the tenant and user of <paramref name="identity"/>.</summary>
    protected TestDbContext ProbeContext<TIdentity>(TIdentity identity, IClock? clock = null)
        where TIdentity : ITenantContext, ICurrentUser
        => new(TestContexts.Options<TestDbContext>(SqlServer.ProbeConnectionString), identity, identity, clock ?? new FakeClock(Now));

    /// <summary>The production context on the migrated application database, for the tenant and user of <paramref name="identity"/>.</summary>
    protected BillingReconDbContext ApiContext<TIdentity>(TIdentity identity, IClock? clock = null)
        where TIdentity : ITenantContext, ICurrentUser
        => new(TestContexts.Options<BillingReconDbContext>(SqlServer.ApiConnectionString), identity, identity, clock ?? new FakeClock(Now));

    /// <summary>A new tenant, with its row in the application database, and a user of it: who an API test acts as.</summary>
    protected async Task<FixedIdentity> NewApiTenantAsync()
    {
        var identity = FixedIdentity.Random();
        await AddApiTenantRowsAsync(identity);

        return identity;
    }

    /// <summary>
    /// The rows of the tenants of <paramref name="identities"/> in the probe database, which their probe rows refer to, and
    /// the row of each identity's user, which every column that names a user refers to (ADR 0030).
    /// </summary>
    protected async Task AddProbeTenantRowsAsync(params FixedIdentity[] identities)
    {
        await AddTenantRowsAsync(ProbeContext(SystemIdentity.Instance), identities);

        foreach (var identity in identities)
        {
            await TestUsers.EnsureAsync(ProbeContext(identity), identity, []);
        }
    }

    /// <summary>The rows of the tenants of <paramref name="identities"/> in the application database, and of their users.</summary>
    protected async Task AddApiTenantRowsAsync(params FixedIdentity[] identities)
    {
        await AddTenantRowsAsync(ApiContext(SystemIdentity.Instance), identities);

        foreach (var identity in identities)
        {
            await TestUsers.EnsureAsync(ApiContext(identity), identity, []);
        }
    }

    /// <summary>The row of the user of <paramref name="identity"/>, whose tenant row exists, in the application database.</summary>
    protected Task AddApiUserRowAsync(FixedIdentity identity, params string[] roles) => TestUsers.EnsureAsync(ApiContext(identity), identity, roles);

    /// <summary>The row of the user of <paramref name="identity"/>, whose tenant row exists, in the probe database.</summary>
    protected Task AddProbeUserRowAsync(FixedIdentity identity) => TestUsers.EnsureAsync(ProbeContext(identity), identity, []);

    // Onboarding is system work: a context without a tenant may add any tenant row (ADR 0015).
    private static async Task AddTenantRowsAsync(BillingReconDbContext system, FixedIdentity[] identities)
    {
        await using (system)
        {
            foreach (var identity in identities)
            {
                var tenant = TestData.NewTenant(Now);
                system.Entry(tenant).Property(candidate => candidate.Id).CurrentValue =
                    identity.TenantId ?? throw new ArgumentException("An identity without a tenant has no tenant row.", nameof(identities));
                system.Add(tenant);
            }

            await system.SaveChangesAsync();
        }
    }
}
