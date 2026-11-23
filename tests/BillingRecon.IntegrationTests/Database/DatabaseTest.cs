using BillingRecon.Application.Common;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.Infrastructure.Receivables;
using BillingRecon.IntegrationTests.Persistence;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// What every test class of the Database collection shares: the SQL Server fixture, empty tables before each test,
/// and contexts on either database for a given identity and clock. The contexts are built by hand, as the persistence
/// tests without a database build them, so the guards the context attaches to itself are in force here too. Every
/// tenant-scoped row refers to its tenant's row through a foreign key (ADR 0015), so a test adds the tenant rows it
/// needs first.
/// <para>
/// And after every test, the reconciliation of every tenant the test left in the application database must find no drift
/// (BR-404, US-41, Prompt 6.2): a financial test that leaves a cache out of line with the ledger, or a number series with
/// a gap, fails, whether or not it checked itself. A test that leaves a defect on purpose names the identities it breaks
/// with <see cref="ExpectDrift"/>; nothing else is ignored.
/// </para>
/// </summary>
public abstract class DatabaseTest(SqlServerFixture sqlServer) : IAsyncLifetime
{
    /// <summary>The instant the clocks of these tests start at.</summary>
    protected static readonly DateTimeOffset Now = new(2026, 10, 17, 6, 30, 0, TimeSpan.Zero);

    private readonly HashSet<string> _expectedDrift = new(StringComparer.Ordinal);

    /// <summary>The SQL Server of the test run.</summary>
    protected SqlServerFixture SqlServer { get; } = sqlServer;

    /// <inheritdoc />
    public Task InitializeAsync() => SqlServer.ResetAsync();

    /// <inheritdoc />
    public Task DisposeAsync() => ShouldHaveNoDriftAsync();

    /// <summary>
    /// The reconciliation (docs/database.md section 5.6) of every tenant of the application database finds no drift row,
    /// but of the identities the test said it breaks on purpose. Runs after every test; a test may also call it midway.
    /// </summary>
    protected async Task ShouldHaveNoDriftAsync()
    {
        List<TenantId> tenants;
        await using (var system = ApiContext(SystemIdentity.Instance))
        {
            tenants = await system.Set<Tenant>().AsNoTracking().Select(tenant => tenant.Id).ToListAsync();
        }

        foreach (var tenant in tenants)
        {
            var identity = new FixedIdentity(tenant);
            await using var context = ApiContext(identity);
            var drift = await new ReconciliationQuery(context, identity).FindDriftAsync(CancellationToken.None);

            drift.Where(row => !_expectedDrift.Contains(row.Identity) && !DriftLeftOnPurpose.Contains(row.Identity))
                .ShouldBeEmpty($"the reconciliation of tenant {tenant.Value} finds drift the test did not leave on purpose");
        }
    }

    /// <summary>
    /// The identities every test of a class leaves on purpose, for a class that tests one part on its own: the ledger
    /// written through its port without the documents, the number series taken without a document. Empty unless the
    /// class says why.
    /// </summary>
    protected virtual IReadOnlyCollection<string> DriftLeftOnPurpose => [];

    /// <summary>
    /// Says that the test leaves, on purpose, drift of these identities (the constants of <see cref="DriftIdentity"/>): a
    /// defect it seeds to see it found, or postings written through the ledger alone without their documents. The check
    /// after the test then ignores those identities, and only those.
    /// </summary>
    protected void ExpectDrift(params string[] identities) => _expectedDrift.UnionWith(identities);

    /// <summary>The test context, probe entities included, on the probe database, for the tenant and user of <paramref name="identity"/>.</summary>
    protected TestDbContext ProbeContext<TIdentity>(TIdentity identity, IClock? clock = null)
        where TIdentity : ITenantContext, ICurrentUser
        => new(TestContexts.Options<TestDbContext>(SqlServer.ProbeConnectionString), identity, identity, clock ?? new FakeClock(Now));

    /// <summary>
    /// The production context on the migrated application database, for the tenant and user of <paramref name="identity"/>,
    /// connected as the application's login: with the grants the application has (ADR 0034).
    /// </summary>
    protected BillingReconDbContext ApiContext<TIdentity>(TIdentity identity, IClock? clock = null)
        where TIdentity : ITenantContext, ICurrentUser
        => new(TestContexts.Options<BillingReconDbContext>(SqlServer.ApiConnectionString), identity, identity, clock ?? new FakeClock(Now));

    /// <summary>
    /// The same context connected as the administrator, for a defect a test seeds on purpose that the application's login
    /// may not cause: a changed or deleted ledger row, a constraint switched off (ADR 0034). Nothing else uses it.
    /// </summary>
    protected BillingReconDbContext AdministratorApiContext<TIdentity>(TIdentity identity)
        where TIdentity : ITenantContext, ICurrentUser
        => new(TestContexts.Options<BillingReconDbContext>(SqlServer.ApiAdminConnectionString), identity, identity, new FakeClock(Now));

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
