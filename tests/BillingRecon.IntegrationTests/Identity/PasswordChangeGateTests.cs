using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BillingRecon.Api.Common.Authentication;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Identity;
using BillingRecon.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BillingRecon.IntegrationTests.Identity;

/// <summary>
/// A token whose user must change the password first (ADR 0051), on the wire, with no database: it lives 15 minutes and
/// carries <c>pwd_change: required</c>; every endpoint answers it 403 <c>password_change_required</c> but the change of
/// one's own password, which it reaches; a plain token is not stopped.
/// </summary>
public sealed class PasswordChangeGateTests
{
    private static HttpClient ClientWith(ApiFactory factory, bool passwordChangeDue)
    {
        var identity = FixedIdentity.Random();
        var token = factory.Services.GetRequiredService<ITokenIssuer>()
            .Issue(identity.TenantId!.Value, identity.UserId!.Value, [Role.TenantAdmin], passwordChangeDue);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        return client;
    }

    [Fact]
    public void ADR0051_a_token_of_a_due_change_lives_15_minutes_and_carries_the_claim()
    {
        using var factory = new ApiFactory();
        var identity = FixedIdentity.Random();
        var issuer = factory.Services.GetRequiredService<ITokenIssuer>();

        var due = new JsonWebToken(issuer.Issue(identity.TenantId!.Value, identity.UserId!.Value, [Role.Viewer], passwordChangeDue: true).Value);
        var plain = new JsonWebToken(issuer.Issue(identity.TenantId!.Value, identity.UserId!.Value, [Role.Viewer], passwordChangeDue: false).Value);

        (due.ValidTo - due.ValidFrom).ShouldBe(TimeSpan.FromMinutes(15));
        due.GetClaim(TokenClaims.PasswordChangeClaim).Value.ShouldBe(TokenClaims.PasswordChangeRequired);
        (plain.ValidTo - plain.ValidFrom).ShouldBe(TimeSpan.FromMinutes(60));
        plain.TryGetClaim(TokenClaims.PasswordChangeClaim, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("GET", "/api/v1/probe/identity")]
    [InlineData("GET", "/api/v1/users")]
    [InlineData("PATCH", "/api/v1/settings")]
    public async Task ADR0051_while_the_change_is_due_every_endpoint_answers_403_password_change_required(string method, string path)
    {
        using var factory = new ApiFactory();
        using var client = ClientWith(factory, passwordChangeDue: true);

        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, IdentityErrors.PasswordChangeRequiredCode);
    }

    [Fact]
    public async Task ADR0051_while_the_change_is_due_the_change_of_ones_own_password_is_reached()
    {
        using var factory = new ApiFactory();
        using var client = ClientWith(factory, passwordChangeDue: true);

        // An empty body is refused by the validator, which only an endpoint that was reached can answer.
        using var response = await client.PostAsJsonAsync("/api/v1/auth/password", new { });

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        response.Headers.CacheControl?.NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task ADR0051_a_plain_token_is_not_stopped()
    {
        using var factory = new ApiFactory();
        using var client = ClientWith(factory, passwordChangeDue: false);

        using var response = await client.GetAsync("/api/v1/probe/identity");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
