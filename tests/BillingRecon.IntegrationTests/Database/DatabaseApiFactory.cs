using BillingRecon.Application.Common;
using BillingRecon.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The API hosted against the SQL Server of the test run (ADR 0016): the Production pipeline of <see cref="ApiFactory"/>,
/// the connection string of one of the fixture's databases (the migrated application database unless another is
/// named), the test identity that reads the test headers in place of the identity the API registers outside
/// Development, and a <see cref="FakeClock"/> in place of the system clock. A test that needs a port to fail hands in
/// <c>configure</c>, which runs last and may replace any registration.
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

    /// <inheritdoc />
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);

        services.AddSingleton<IClock>(Clock);

        services.AddHttpContextAccessor();
        services.AddScoped<TestRequestIdentity>();
        services.AddScoped<ITenantContext>(provider => provider.GetRequiredService<TestRequestIdentity>());
        services.AddScoped<ICurrentUser>(provider => provider.GetRequiredService<TestRequestIdentity>());

        configure?.Invoke(services);
    }
}
