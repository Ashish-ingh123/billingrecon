using BillingRecon.Application.Common;
using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.IntegrationTests.Persistence;
using BillingRecon.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The API hosted against the SQL Server of the test run (ADR 0016): the Production pipeline of <see cref="ApiFactory"/>,
/// with its authentication by tokens, the connection string of one of the fixture's databases (the migrated
/// application database unless another is named), and a <see cref="FakeClock"/> in place of the system clock, which
/// both issues the clients' tokens and checks them. A test that needs a port to fail hands in <c>configure</c>, which
/// runs last and may replace any registration.
/// </summary>
public sealed class DatabaseApiFactory(SqlServerFixture sqlServer, string database = SqlServerFixture.ApiDatabase, Action<IServiceCollection>? configure = null) : ApiFactory
{
    /// <summary>Where <see cref="Clock"/> starts.</summary>
    public static readonly DateTimeOffset InitialInstant = new(2026, 10, 17, 6, 30, 0, TimeSpan.Zero);

    /// <summary>The clock of the hosted API. A test moves it.</summary>
    public FakeClock Clock { get; } = new(InitialInstant);

    /// <inheritdoc />
    protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
    [
        new("Database:ConnectionString", sqlServer.ConnectionStringFor(database)),
        new("Seq:ServerUrl", string.Empty),

        // The fixture applied the migrations; the API never migrates in the tests, as it never does outside Development.
        new("Database:ApplyMigrationsOnStartup", "false"),
    ];

    /// <summary>
    /// A client that acts as <paramref name="identity"/> with <paramref name="roles"/>, once the identity is a user of its
    /// tenant: every column that names a user is a foreign key to <c>Users</c> since Prompt 5.1 (ADR 0030). The tenant's
    /// row must exist already.
    /// </summary>
    public HttpClient CreateClientFor(FixedIdentity identity, params string[] roles)
    {
        ArgumentNullException.ThrowIfNull(identity);

        TestUsers.Ensure(
            new BillingReconDbContext(TestContexts.Options<BillingReconDbContext>(sqlServer.ConnectionStringFor(database)), identity, identity, new FakeClock(InitialInstant)),
            identity,
            roles);

        return TestClientExtensions.CreateClientFor(this, identity, roles);
    }

    /// <inheritdoc />
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);

        services.AddSingleton<IClock>(Clock);

        configure?.Invoke(services);
    }
}
