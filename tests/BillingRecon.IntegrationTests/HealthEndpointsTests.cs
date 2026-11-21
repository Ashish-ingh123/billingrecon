using System.Net;
using BillingRecon.IntegrationTests.Support;

namespace BillingRecon.IntegrationTests;

/// <summary>
/// /health/live answers whenever the process does; /health/ready also needs SQL Server. The factory points the
/// database at a closed port, so these tests cover "alive but not ready". The healthy path of readiness, and the
/// pending-migration path, run against a real SQL Server in <see cref="Database.ReadinessOnSqlServerTests"/>.
/// </summary>
public sealed class HealthEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Live_reports_healthy_without_touching_any_dependency()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Healthy");
    }

    [Fact]
    public async Task Ready_reports_unhealthy_when_sql_server_is_unreachable_and_reveals_nothing_else()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Unhealthy");
        response.GetCorrelationId().ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task The_old_health_path_is_gone()
    {
        using var client = factory.CreateClientFor(FixedIdentity.Random());

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "not_found");
    }
}
