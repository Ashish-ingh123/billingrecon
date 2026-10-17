using System.Net;
using System.Net.Http.Json;
using BillingRecon.IntegrationTests.Support;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The client helper of the database-backed API tests: a client created for a tenant, user and roles acts for them in
/// every request, as the Application ports see it, and an anonymous request acts for nobody.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class ClientIdentityTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private static readonly Uri Identity = new("/api/v1/probe/identity", UriKind.Relative);

    [Fact]
    public async Task A_client_for_a_tenant_and_user_acts_for_them_in_every_request()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var identity = FixedIdentity.Random();
        using var client = factory.CreateClientFor(identity, "Supervisor", "Cashier");

        using var response = await client.GetAsync(Identity);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<IdentityResponse>()).ShouldNotBeNull();
        body.TenantId.ShouldBe(identity.TenantId!.Value.Value);
        body.UserId.ShouldBe(identity.UserId!.Value.Value);
        body.Roles.ShouldBe(["Supervisor", "Cashier"]);
    }

    [Fact]
    public async Task An_anonymous_request_acts_for_nobody_and_fails_when_it_asks()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Identity);

        await response.ShouldBeProblemAsync(HttpStatusCode.InternalServerError, "internal_error");
    }
}
