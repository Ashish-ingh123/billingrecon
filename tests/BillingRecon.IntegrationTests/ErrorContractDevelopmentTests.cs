using System.Net;
using BillingRecon.IntegrationTests.Support;

namespace BillingRecon.IntegrationTests;

public sealed class ErrorContractDevelopmentTests(DevelopmentApiFactory factory) : IClassFixture<DevelopmentApiFactory>
{
    [Fact]
    public async Task In_development_the_500_also_carries_the_exception_details()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/probe/throw", UriKind.Relative));

        var problem = await response.ShouldBeProblemAsync(HttpStatusCode.InternalServerError, "internal_error");
        var exception = problem.GetProperty("exception");
        exception.GetProperty("type").GetString().ShouldBe(typeof(InvalidOperationException).FullName);
        exception.GetProperty("message").GetString().ShouldBe(ProbeController.SecretExceptionMessage);
        exception.GetProperty("details").GetString().ShouldNotBeNull().ShouldContain(nameof(ProbeController));
    }
}
