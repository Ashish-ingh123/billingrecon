using System.Net;
using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.IntegrationTests.Persistence;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// /health/ready against a real database: 200 when every migration is applied, 503 while one is pending. The
/// unreachable-server case is covered without a database in <see cref="HealthEndpointsTests"/>.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class ReadinessOnSqlServerTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private static readonly Uri Ready = new("/health/ready", UriKind.Relative);

    [Fact]
    public async Task Ready_answers_200_when_the_database_has_every_migration()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Ready);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Healthy");
    }

    [Fact]
    public async Task Ready_answers_503_while_a_migration_is_pending_and_200_once_it_is_applied()
    {
        const string database = "BillingRecon_Pending";
        await SqlServer.CreateEmptyDatabaseAsync(database);
        using var factory = new DatabaseApiFactory(SqlServer, database);
        using var client = factory.CreateClient();

        using (var notReady = await client.GetAsync(Ready))
        {
            notReady.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            (await notReady.Content.ReadAsStringAsync()).ShouldBe("Unhealthy");
        }

        await using (var context = new BillingReconDbContext(
            TestContexts.Options<BillingReconDbContext>(SqlServer.ConnectionStringFor(database)), SystemIdentity.Instance, SystemIdentity.Instance, new FakeClock(Now)))
        {
            await context.Database.MigrateAsync();
        }

        using var ready = await client.GetAsync(Ready);

        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
