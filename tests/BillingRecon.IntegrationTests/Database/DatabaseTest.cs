using BillingRecon.Application.Common;
using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.IntegrationTests.Persistence;
using BillingRecon.IntegrationTests.Support;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// What every test class of the Database collection shares: the SQL Server fixture, empty tables before each test,
/// and contexts on either database for a given identity and clock. The contexts are built by hand, as the persistence
/// tests without a database build them, so the guards the context attaches to itself are in force here too.
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
}
