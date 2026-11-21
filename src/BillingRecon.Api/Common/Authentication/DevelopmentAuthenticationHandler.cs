using System.Text.Encodings.Web;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace BillingRecon.Api.Common.Authentication;

/// <summary>
/// Development only (ADR 0031, point 9): a request without an <c>Authorization</c> header acts as the development
/// user of <c>DevelopmentIdentity</c>, a tenant administrator, so that the Scalar page and local tools work without a
/// sign-in. A request with the header goes to the bearer scheme, as everywhere else; an anonymous endpoint gets nobody.
/// Never registered outside Development.
/// </summary>
public sealed class DevelopmentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<DevelopmentIdentityOptions> identity) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>The name of the scheme.</summary>
    public const string SchemeName = "Development";

    /// <summary>The roles of the development user.</summary>
    public static readonly IReadOnlyList<Role> Roles = [Role.TenantAdmin];

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (AnonymousEndpoints.IsAnonymous(Context))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var principal = TokenClaims.Principal(new TenantId(identity.Value.TenantId), new UserId(identity.Value.UserId), Roles, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
