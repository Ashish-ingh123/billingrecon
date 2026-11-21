using System.Security.Claims;
using BillingRecon.Api.Common.Authentication;
using Serilog.Context;
using Serilog.Core;
using Serilog.Core.Enrichers;

namespace BillingRecon.Api.Common.Logging;

/// <summary>
/// The tenant and user of an authenticated request, for log enrichment only: the claims of <see cref="TokenClaims"/>,
/// as text. Nothing is read for an anonymous request. What the request may do is decided through <c>ITenantContext</c>
/// and <c>ICurrentUser</c> (<see cref="RequestPrincipalIdentity"/>), which read the same claims.
/// </summary>
public static class RequestIdentity
{
    /// <summary>JWT claim that carries the tenant id (BR-602).</summary>
    public const string TenantIdClaim = TokenClaims.TenantIdClaim;

    /// <summary>JWT claim that carries the user id (BR-602).</summary>
    public const string UserIdClaim = TokenClaims.UserIdClaim;

    /// <summary>Name of the log event property that carries the tenant id.</summary>
    public const string TenantIdProperty = "TenantId";

    /// <summary>Name of the log event property that carries the user id.</summary>
    public const string UserIdProperty = "UserId";

    /// <summary>The tenant id and user id of an authenticated principal; both null for an anonymous one.</summary>
    public static (string? TenantId, string? UserId) Read(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (user.Identity?.IsAuthenticated != true)
        {
            return (null, null);
        }

        // The bearer scheme keeps the names of the token's claims (MapInboundClaims is off, ADR 0031): "sub" stays "sub".
        return (user.FindFirst(TenantIdClaim)?.Value, user.FindFirst(UserIdClaim)?.Value);
    }

    /// <summary>
    /// Adds <c>TenantId</c> and <c>UserId</c> to every log event written until the returned scope is disposed.
    /// Returns null when the request is anonymous.
    /// </summary>
    public static IDisposable? PushToLogContext(ClaimsPrincipal user)
    {
        var (tenantId, userId) = Read(user);

        return (tenantId, userId) switch
        {
            (null, null) => null,
            (not null, null) => LogContext.PushProperty(TenantIdProperty, tenantId),
            (null, not null) => LogContext.PushProperty(UserIdProperty, userId),
            _ => LogContext.Push(
                new ILogEventEnricher[] { new PropertyEnricher(TenantIdProperty, tenantId), new PropertyEnricher(UserIdProperty, userId) }),
        };
    }
}
