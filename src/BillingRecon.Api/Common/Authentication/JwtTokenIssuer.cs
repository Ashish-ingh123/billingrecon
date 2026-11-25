using System.Globalization;
using System.Security.Claims;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BillingRecon.Api.Common.Authentication;

/// <summary>
/// Issues the API's tokens (ADR 0031, points 1 to 3): a JWT signed with HS256, from <see cref="JwtOptions.Issuer"/> to
/// <see cref="JwtOptions.Audience"/>, valid for <see cref="JwtOptions.Lifetime"/> from now by the application's clock,
/// with the claims of <see cref="TokenClaims"/> and a <c>jti</c> of its own. One instance for the application.
/// </summary>
public sealed class JwtTokenIssuer(IOptions<JwtOptions> options, IClock clock) : ITokenIssuer
{
    // No time is taken from the system clock: every time of the token is set below, from the application's clock.
    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    private readonly SigningCredentials _credentials = new(new SymmetricSecurityKey(options.Value.ReadKey()), SecurityAlgorithms.HmacSha256);

    /// <inheritdoc />
    public AccessToken Issue(TenantId tenantId, UserId userId, IReadOnlyCollection<Role> roles, bool passwordChangeDue, int sessionVersion)
    {
        ArgumentNullException.ThrowIfNull(roles);

        // The times of a token are whole seconds; the instant is cut to the second, so the expiry answered is the token's.
        var now = clock.UtcNow;
        now = now.AddTicks(-(now.UtcTicks % TimeSpan.TicksPerSecond));
        var expires = now + (passwordChangeDue ? JwtOptions.PasswordChangeLifetime : JwtOptions.Lifetime);

        var subject = new ClaimsIdentity(TokenClaims.For(tenantId, userId, roles));
        subject.AddClaim(new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()));

        // The number of the user's sessions (ADR 0057): the token is refused once the user has another.
        subject.AddClaim(new Claim(TokenClaims.SessionClaim, sessionVersion.ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer32));
        if (passwordChangeDue)
        {
            subject.AddClaim(new Claim(TokenClaims.PasswordChangeClaim, TokenClaims.PasswordChangeRequired));
        }

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = JwtOptions.Issuer,
            Audience = JwtOptions.Audience,
            Subject = subject,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = _credentials,
        });

        return new AccessToken(token, expires);
    }
}
