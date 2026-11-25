using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using BillingRecon.Api.Common.Authentication;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Identity;
using BillingRecon.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BillingRecon.IntegrationTests.Identity;

/// <summary>
/// The session of a token (ADR 0057), on the wire, with no database: a token carries the number of the session it was
/// issued in; every request with a token asks whether that session is still the user's, and one whose session has ended
/// answers 401 <c>session_ended</c>, the sign-out included; a token that names no session is refused without asking; the
/// sign-in and the health checks, which read no token, are not concerned.
/// </summary>
public sealed class SessionGateTests
{
    private static HttpClient ClientWith(ApiFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string TokenOf(ApiFactory factory, FixedIdentity identity, int sessionVersion, bool passwordChangeDue = false)
        => factory.Services.GetRequiredService<ITokenIssuer>()
            .Issue(identity.TenantId!.Value, identity.UserId!.Value, [Role.TenantAdmin], passwordChangeDue, sessionVersion).Value;

    [Fact]
    public void ADR0057_a_token_carries_the_number_of_its_session()
    {
        using var factory = new ApiFactory();

        var token = new JsonWebToken(TokenOf(factory, FixedIdentity.Random(), sessionVersion: 7));

        token.GetClaim(TokenClaims.SessionClaim).Value.ShouldBe("7");
    }

    [Fact]
    public async Task ADR0057_every_request_with_a_token_asks_for_the_session_of_that_token()
    {
        using var factory = new ApiFactory();
        var identity = FixedIdentity.Random();
        using var client = ClientWith(factory, TokenOf(factory, identity, sessionVersion: 3));

        using var first = await client.GetAsync("/api/v1/probe/identity");
        using var second = await client.GetAsync("/api/v1/probe/identity");

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        factory.Sessions.Asked.ShouldBe([(identity.UserId!.Value, 3), (identity.UserId!.Value, 3)]);
    }

    [Theory]
    [InlineData("GET", "/api/v1/probe/identity")]
    [InlineData("GET", "/api/v1/users")]
    [InlineData("POST", "/api/v1/auth/logout")]
    [InlineData("POST", "/api/v1/auth/password")]
    [InlineData("GET", "/api/v1/no-such-endpoint")]
    public async Task ADR0057_a_token_whose_session_has_ended_answers_401_session_ended(string method, string path)
    {
        using var factory = new ApiFactory();
        var identity = FixedIdentity.Random();
        using var client = ClientWith(factory, TokenOf(factory, identity, sessionVersion: 0));
        factory.Sessions.End(identity.UserId!.Value);

        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, IdentityErrors.SessionEndedCode);
    }

    [Fact]
    public async Task ADR0057_the_ended_session_of_one_user_leaves_another_users_token_alone()
    {
        using var factory = new ApiFactory();
        var signedOut = FixedIdentity.Random();
        using var client = ClientWith(factory, TokenOf(factory, FixedIdentity.Random(), sessionVersion: 0));
        factory.Sessions.End(signedOut.UserId!.Value);

        using var response = await client.GetAsync("/api/v1/probe/identity");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ADR0057_a_token_that_names_no_session_is_refused_without_asking()
    {
        using var factory = new ApiFactory();
        var identity = FixedIdentity.Random();
        var options = factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        var now = DateTime.UtcNow;
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = JwtOptions.Issuer,
            Audience = JwtOptions.Audience,
            Subject = new ClaimsIdentity(TokenClaims.For(identity.TenantId!.Value, identity.UserId!.Value, [Role.TenantAdmin])),
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(30),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(options.ReadKey()), SecurityAlgorithms.HmacSha256),
        });
        using var client = ClientWith(factory, token);

        using var response = await client.GetAsync("/api/v1/probe/identity");

        await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, IdentityErrors.SessionEndedCode);
        factory.Sessions.Asked.ShouldBeEmpty();
    }

    [Fact]
    public async Task ADR0057_the_health_checks_read_no_token_and_ask_for_no_session()
    {
        using var factory = new ApiFactory();
        var identity = FixedIdentity.Random();
        using var client = ClientWith(factory, TokenOf(factory, identity, sessionVersion: 0));
        factory.Sessions.End(identity.UserId!.Value);

        using var live = await client.GetAsync("/health/live");

        live.StatusCode.ShouldBe(HttpStatusCode.OK);
        factory.Sessions.Asked.ShouldBeEmpty();
    }

    [Fact]
    public async Task ADR0057_a_user_whose_password_change_is_due_may_still_sign_out()
    {
        using var factory = new ApiFactory();
        using var client = ClientWith(factory, TokenOf(factory, FixedIdentity.Random(), sessionVersion: 0, passwordChangeDue: true));

        using var response = await client.PostAsync("/api/v1/auth/logout", content: null);

        // The gate of the due change lets the sign-out through; with no database behind this host the use case then
        // fails, which is not the gate's refusal.
        response.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden);
        response.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);
    }
}
