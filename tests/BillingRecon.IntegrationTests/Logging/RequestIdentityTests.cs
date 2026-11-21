using System.Security.Claims;
using BillingRecon.Api.Common.Logging;
using BillingRecon.IntegrationTests.Support;
using Serilog;

namespace BillingRecon.IntegrationTests.Logging;

public sealed class RequestIdentityTests
{
    private const string Tenant = "5b0e3b0a-7d5f-4a59-9c36-2f1a6c0f4d11";
    private const string User = "9c1d2e3f-4a5b-4c6d-8e7f-0a1b2c3d4e5f";

    [Fact]
    public void An_anonymous_principal_has_no_identity_even_if_it_carries_claims()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity([new Claim(RequestIdentity.TenantIdClaim, Tenant), new Claim(RequestIdentity.UserIdClaim, User)]));

        RequestIdentity.Read(anonymous).ShouldBe((null, null));
        RequestIdentity.PushToLogContext(anonymous).ShouldBeNull();
    }

    [Fact]
    public void An_authenticated_principal_yields_its_tenant_and_user()
    {
        var principal = Authenticated(new Claim(RequestIdentity.TenantIdClaim, Tenant), new Claim(RequestIdentity.UserIdClaim, User));

        RequestIdentity.Read(principal).ShouldBe((Tenant, User));
    }

    [Fact]
    public void The_long_name_identifier_claim_is_not_read_as_the_user()
    {
        // The bearer scheme keeps the claims' own names (ADR 0031): a "sub" never arrives renamed, so nothing else is read.
        var principal = Authenticated(new Claim(RequestIdentity.TenantIdClaim, Tenant), new Claim(ClaimTypes.NameIdentifier, User));

        RequestIdentity.Read(principal).ShouldBe((Tenant, null));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Only_the_ids_the_principal_carries_are_pushed_to_the_log_context(bool hasTenant, bool hasUser)
    {
        List<Claim> claims = [];
        if (hasTenant)
        {
            claims.Add(new Claim(RequestIdentity.TenantIdClaim, Tenant));
        }

        if (hasUser)
        {
            claims.Add(new Claim(RequestIdentity.UserIdClaim, User));
        }

        var sink = new InMemoryLogSink();
        using (var logger = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Sink(sink).CreateLogger())
        {
            using (RequestIdentity.PushToLogContext(Authenticated([.. claims])))
            {
                logger.Information("inside the scope");
            }

            logger.Information("after the scope");
        }

        var inside = sink.Events[0];
        inside.Scalar(RequestIdentity.TenantIdProperty).ShouldBe(hasTenant ? Tenant : null);
        inside.Scalar(RequestIdentity.UserIdProperty).ShouldBe(hasUser ? User : null);

        var after = sink.Events[1];
        after.Properties.ContainsKey(RequestIdentity.TenantIdProperty).ShouldBeFalse();
        after.Properties.ContainsKey(RequestIdentity.UserIdProperty).ShouldBeFalse();
    }

    private static ClaimsPrincipal Authenticated(params Claim[] claims)
        => new(new ClaimsIdentity(claims, authenticationType: "Test"));
}
