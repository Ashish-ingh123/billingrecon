using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using BillingRecon.Api.Common.Authentication;
using BillingRecon.Application.Common;
using BillingRecon.IntegrationTests.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BillingRecon.IntegrationTests;

/// <summary>
/// The tokens of the API on the wire (ADR 0031; BR-602): what one carries, that the API takes only its own, signed with
/// its key, from its issuer, for its audience, within its lifetime by the application's clock, with a tenant and a
/// user; and deny by default: without a token every endpoint but the anonymous ones answers 401 <c>unauthorized</c>.
/// </summary>
public sealed partial class AuthenticationTests
{
    private static readonly Uri Identity = new("/api/v1/probe/identity", UriKind.Relative);
    private static readonly DateTimeOffset Start = new(2026, 10, 17, 6, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_request_without_a_token_is_refused_with_401_unauthorized_and_a_bare_bearer_challenge()
    {
        using var factory = new ClockedApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Identity);

        await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "unauthorized");
        response.Headers.WwwAuthenticate.ToString().ShouldBe("Bearer");
    }

    [Fact]
    public async Task BR602_a_token_of_the_api_acts_for_its_tenant_its_user_and_its_roles()
    {
        using var factory = new ClockedApiFactory();
        var identity = FixedIdentity.Random();
        using var client = factory.CreateClientFor(identity, "Supervisor", "Cashier");

        var body = (await client.GetFromJsonAsync<IdentityResponse>(Identity)).ShouldNotBeNull();

        body.TenantId.ShouldBe(identity.TenantId!.Value.Value);
        body.UserId.ShouldBe(identity.UserId!.Value.Value);
        body.Roles.ShouldBe(["Supervisor", "Cashier"]);
    }

    [Fact]
    public void A_token_carries_the_documented_claims_and_lives_60_minutes()
    {
        using var factory = new ClockedApiFactory();
        var identity = FixedIdentity.Random();

        var token = new JsonWebToken(factory.TokenFor(identity.TenantId!.Value, identity.UserId!.Value, "Supervisor", "Cashier"));
        var again = new JsonWebToken(factory.TokenFor(identity.TenantId!.Value, identity.UserId!.Value, "Supervisor", "Cashier"));

        token.Alg.ShouldBe(SecurityAlgorithms.HmacSha256);
        token.Issuer.ShouldBe(JwtOptions.Issuer);
        token.Audiences.ShouldBe([JwtOptions.Audience]);
        token.Subject.ShouldBe(identity.UserId.Value.ToString());
        token.GetClaim(TokenClaims.TenantIdClaim).Value.ShouldBe(identity.TenantId.Value.ToString());
        token.Claims.Where(claim => claim.Type == TokenClaims.RoleClaim).Select(claim => claim.Value).ShouldBe(["Supervisor", "Cashier"]);
        (token.IssuedAt, token.ValidFrom, token.ValidTo).ShouldBe((Start.UtcDateTime, Start.UtcDateTime, Start.AddMinutes(60).UtcDateTime));
        token.Id.ShouldNotBeNullOrWhiteSpace();
        again.Id.ShouldNotBe(token.Id, "every token has an id of its own");
    }

    [Fact]
    public async Task A_token_is_valid_for_60_minutes_by_the_application_clock_with_one_minute_of_tolerance()
    {
        using var factory = new ClockedApiFactory();
        var identity = FixedIdentity.Random();
        var token = factory.TokenFor(identity.TenantId!.Value, identity.UserId!.Value);
        using var client = factory.CreateClient();

        // The fake clock is weeks behind the machine's: a token the system clock judged would be expired at once.
        (await SendAsync(client, token)).StatusCode.ShouldBe(HttpStatusCode.OK);

        factory.Clock.Advance(TimeSpan.FromMinutes(60.5));
        (await SendAsync(client, token)).StatusCode.ShouldBe(HttpStatusCode.OK, "within the tolerance for clocks");

        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        using var expired = await SendAsync(client, token);
        await expired.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "unauthorized");
        expired.Headers.WwwAuthenticate.ToString().ShouldBe("Bearer", "why a token is refused is not told");
    }

    [Theory]
    [InlineData(0.5, HttpStatusCode.OK)]
    [InlineData(5, HttpStatusCode.Unauthorized)]
    public async Task A_token_that_is_not_valid_yet_is_refused_beyond_the_tolerance(double minutesAhead, HttpStatusCode expected)
    {
        using var factory = new ClockedApiFactory();
        var notBefore = Start.AddMinutes(minutesAhead);
        var token = Craft(factory, descriptor =>
        {
            descriptor.IssuedAt = notBefore.UtcDateTime;
            descriptor.NotBefore = notBefore.UtcDateTime;
            descriptor.Expires = notBefore.AddMinutes(60).UtcDateTime;
        });
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, token);

        response.StatusCode.ShouldBe(expected);
    }

    [Theory]
    [InlineData("not a token")]
    [InlineData("another key")]
    [InlineData("another issuer")]
    [InlineData("another audience")]
    [InlineData("unsigned")]
    [InlineData("HS512")]
    [InlineData("no expiry")]
    [InlineData("no tenant")]
    [InlineData("a tenant that is not an id")]
    [InlineData("no user")]
    [InlineData("a user that is not an id")]
    [InlineData("an empty tenant")]
    public async Task A_token_that_is_not_one_of_the_api_is_refused(string flaw)
    {
        using var factory = new ClockedApiFactory();
        var token = flaw switch
        {
            "not a token" => "not a token",
            "another key" => Craft(factory, descriptor => descriptor.SigningCredentials = Signing(new byte[JwtOptions.MinimumKeyBytes])),
            "another issuer" => Craft(factory, descriptor => descriptor.Issuer = "SomeoneElse"),
            "another audience" => Craft(factory, descriptor => descriptor.Audience = "SomeoneElse.Api"),
            "unsigned" => Craft(factory, descriptor => descriptor.SigningCredentials = null),
            "HS512" => Craft(factory, descriptor => descriptor.SigningCredentials = new SigningCredentials(Key(factory), SecurityAlgorithms.HmacSha512)),
            "no expiry" => Craft(factory, descriptor => descriptor.Expires = null),
            "no tenant" => Craft(factory, descriptor => descriptor.Claims.Remove(TokenClaims.TenantIdClaim)),
            "a tenant that is not an id" => Craft(factory, descriptor => descriptor.Claims[TokenClaims.TenantIdClaim] = "apollo-delhi"),
            "no user" => Craft(factory, descriptor => descriptor.Claims.Remove(TokenClaims.UserIdClaim)),
            "a user that is not an id" => Craft(factory, descriptor => descriptor.Claims[TokenClaims.UserIdClaim] = "asha.rao@example.in"),
            "an empty tenant" => Craft(factory, descriptor => descriptor.Claims[TokenClaims.TenantIdClaim] = Guid.Empty.ToString()),
            _ => throw new ArgumentOutOfRangeException(nameof(flaw), flaw, "No such flaw."),
        };
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, token);

        await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "unauthorized");
    }

    [Fact]
    public async Task A_crafted_token_without_a_flaw_is_accepted_so_each_flaw_above_is_what_is_refused()
    {
        using var factory = new ClockedApiFactory();
        using var client = factory.CreateClient();

        using var response = await SendAsync(client, Craft(factory, _ => { }));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ADR0031_without_a_token_every_endpoint_answers_401_but_the_sign_in_and_the_health_endpoints()
    {
        using var factory = new ClockedApiFactory();
        using var client = factory.CreateClient();
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToArray();

        var anonymous = endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(Describe)
            .Order(StringComparer.Ordinal);
        anonymous.ShouldBe(["* health/live", "* health/ready", "POST api/v1/auth/login"]);

        var refused = 0;
        foreach (var endpoint in endpoints.Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null))
        {
            foreach (var method in endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), new Uri("/" + Fill(endpoint.RoutePattern.RawText!), UriKind.Relative));
                if (method is "POST" or "PUT" or "PATCH")
                {
                    request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                }

                using var response = await client.SendAsync(request);

                response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{method} {endpoint.RoutePattern.RawText} answered without a token");
                refused++;
            }
        }

        // Every controller of the API, and the probes of the tests, so the loop cannot pass by finding nothing.
        refused.ShouldBeGreaterThan(80);
    }

    [Fact]
    public async Task Without_a_token_a_path_that_does_not_exist_is_refused_before_it_is_found_missing()
    {
        using var factory = new ClockedApiFactory();
        using var anonymous = factory.CreateClient();
        using var signedIn = factory.CreateClientFor(FixedIdentity.Random());

        using var refused = await anonymous.GetAsync(new Uri("/api/v1/does-not-exist", UriKind.Relative));
        using var missing = await signedIn.GetAsync(new Uri("/api/v1/does-not-exist", UriKind.Relative));

        await refused.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "unauthorized");
        await missing.ShouldBeProblemAsync(HttpStatusCode.NotFound, "not_found");
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Identity);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await client.SendAsync(request);
    }

    // A token as the API would issue it at the start of the clock, then changed by the test.
    private static string Craft(ClockedApiFactory factory, Action<SecurityTokenDescriptor> change)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = JwtOptions.Issuer,
            Audience = JwtOptions.Audience,
            IssuedAt = Start.UtcDateTime,
            NotBefore = Start.UtcDateTime,
            Expires = Start.AddMinutes(60).UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [TokenClaims.UserIdClaim] = Guid.NewGuid().ToString(),
                [TokenClaims.TenantIdClaim] = Guid.NewGuid().ToString(),
                [TokenClaims.RoleClaim] = "Cashier",
            },
            SigningCredentials = new SigningCredentials(Key(factory), SecurityAlgorithms.HmacSha256),
        };
        change(descriptor);

        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);
    }

    private static SymmetricSecurityKey Key(ApiFactory factory) => new(Convert.FromBase64String(factory.SigningKey));

    private static SigningCredentials Signing(byte[] key) => new(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256);

    private static string Describe(RouteEndpoint endpoint)
        => $"{string.Join(',', endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])} {endpoint.RoutePattern.RawText!.TrimStart('/')}";

    // A value for every route parameter that its constraint accepts.
    private static string Fill(string pattern)
        => RouteParameter().Replace(pattern.TrimStart('/'), match => match.Groups["constraint"].Value switch
        {
            var constraint when constraint.Contains("guid", StringComparison.Ordinal) => Guid.NewGuid().ToString(),
            var constraint when constraint.Contains("int", StringComparison.Ordinal) => "1",
            _ => "probe",
        });

    [GeneratedRegex(@"\{(?<name>[^:}]+)(?<constraint>:[^}]*)?\}")]
    private static partial Regex RouteParameter();

    /// <summary>The API with a clock the test moves, which both issues tokens and checks them.</summary>
    private sealed class ClockedApiFactory : ApiFactory
    {
        public FakeClock Clock { get; } = new(Start);

        protected override void ConfigureTestServices(IServiceCollection services)
        {
            base.ConfigureTestServices(services);

            services.AddSingleton<IClock>(Clock);
        }
    }
}
