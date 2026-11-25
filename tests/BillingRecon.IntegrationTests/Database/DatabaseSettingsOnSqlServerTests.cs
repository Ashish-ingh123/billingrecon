using System.Net;
using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.IntegrationTests.Persistence;
using BillingRecon.IntegrationTests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The setting of the database the application relies on (ADR 0034, ADR 0055): outside Development the API refuses to
/// start on a database with <c>READ_COMMITTED_SNAPSHOT</c> off, and starts on the migrated one, which has it on.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class DatabaseSettingsOnSqlServerTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private const string Database = "BillingRecon_NoSnapshot";

    [Fact]
    public async Task The_api_starts_and_is_ready_on_a_database_with_the_snapshot_on()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_api_refuses_to_start_on_a_database_with_the_snapshot_off()
    {
        await SqlServer.CreateEmptyDatabaseAsync(Database);
        try
        {
            await using (var context = new BillingReconDbContext(
                TestContexts.Options<BillingReconDbContext>(SqlServer.ConnectionStringFor(Database)), SystemIdentity.Instance, SystemIdentity.Instance, new FakeClock(Now)))
            {
                await context.Database.MigrateAsync();
            }

            await ExecuteAsync("master", $"ALTER DATABASE [{Database}] SET READ_COMMITTED_SNAPSHOT OFF WITH ROLLBACK IMMEDIATE");
            using var factory = new DatabaseApiFactory(SqlServer, Database);

            var exception = Should.Throw<InvalidOperationException>(() => factory.CreateClient());

            exception.Message.ShouldBe(DatabaseSettingsProbe.SnapshotOffMessage);
            exception.Message.ShouldNotContain(Database);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await ExecuteAsync("master", $"IF DB_ID(N'{Database}') IS NOT NULL BEGIN ALTER DATABASE [{Database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Database}]; END");
        }
    }

    private async Task ExecuteAsync(string database, string sql)
    {
        await using var connection = new SqlConnection(SqlServer.AdminConnectionStringFor(database));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
