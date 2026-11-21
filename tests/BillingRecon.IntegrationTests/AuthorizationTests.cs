using System.Net;
using System.Net.Http.Json;
using BillingRecon.Domain.Common;
using BillingRecon.IntegrationTests.Support;

namespace BillingRecon.IntegrationTests;

/// <summary>
/// The permissions on the wire, without a database (ADR 0032): a role a policy refuses gets 403 <c>forbidden</c> before
/// anything of the request is read; a user with several roles may do what any of them may; the roles of the token are
/// the roles the use cases see. The table-driven walk of every endpoint with every role is
/// <c>Database.PermissionMatrixApiTests</c>.
/// </summary>
public sealed class AuthorizationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly Uri Payments = new("/api/v1/payments", UriKind.Relative);

    [Theory]
    [InlineData("POST", "/api/v1/patients", "Viewer")]
    [InlineData("GET", "/api/v1/users", "Cashier")]
    [InlineData("GET", "/api/v1/approval-requests", "BillingClerk")]
    [InlineData("GET", "/api/v1/refunds", "BillingClerk")]
    [InlineData("GET", "/api/v1/adjustments", "Cashier")]
    public async Task A_role_the_policy_refuses_gets_403_forbidden_as_problem_details(string method, string path, string role)
    {
        using var client = factory.CreateClientFor(FixedIdentity.Random(), role);
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));
        if (method == "POST")
        {
            request.Content = PayersApi.RawJson("{}");
        }

        using var response = await client.SendAsync(request);

        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "forbidden");
    }

    [Theory]
    [InlineData("Viewer", HttpStatusCode.Forbidden)]
    [InlineData("Cashier", HttpStatusCode.BadRequest)]
    public async Task A_role_the_policy_admits_goes_on_to_the_request_itself(string role, HttpStatusCode expected)
    {
        // A payment without an Idempotency-Key: refused for a Viewer by the policy, for a Cashier by the key's filter.
        using var client = factory.CreateClientFor(FixedIdentity.Random(), role);

        using var response = await client.PostAsync(Payments, PayersApi.RawJson("{}"));

        response.StatusCode.ShouldBe(expected);
    }

    [Fact]
    public async Task A_user_with_several_roles_may_do_what_any_of_them_may()
    {
        using var client = factory.CreateClientFor(FixedIdentity.Random(), "Viewer", "Cashier");

        using var response = await client.PostAsync(Payments, PayersApi.RawJson("{}"));

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, CommonErrors.IdempotencyKeyMissingCode);
    }

    [Theory]
    [InlineData("TenantAdmin")]
    [InlineData("Supervisor")]
    [InlineData("BillingClerk")]
    [InlineData("Cashier")]
    [InlineData("Viewer")]
    public async Task Every_role_reads_the_service_information(string role)
    {
        using var client = factory.CreateClientFor(FixedIdentity.Random(), role);

        using var response = await client.GetAsync(new Uri("/api/v1/system/info", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_use_cases_see_the_roles_of_the_token_in_the_order_of_the_roles()
    {
        using var client = factory.CreateClientFor(FixedIdentity.Random(), "Viewer", "Supervisor");

        var body = (await client.GetFromJsonAsync<IdentityResponse>(new Uri("/api/v1/probe/identity", UriKind.Relative))).ShouldNotBeNull();

        body.Roles.ShouldBe(["Supervisor", "Viewer"]);
    }
}
