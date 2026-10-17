using System.Security.Claims;
using Serilog.Context;
using Serilog.Core;
using Serilog.Core.Enrichers;

namespace BillingRecon.Api.Common.Logging;

/// <summary>
/// The tenant and user of an authenticated request, for log enrichment only. Nothing is read for an anonymous request.
/// Authorisation never uses these values: it will go through <c>ITenantContext</c> and <c>ICurrentUser</c> when the
/// Identity slice lands, and this class will then read from them instead of from the claims.
/// </summary>
public static class RequestIdentity
{
    /// <summary>JWT claim that carries the tenant id (BR-602).</summary>
    public const string TenantIdClaim = "tenant_id";

    /// <summary>JWT claim that carries the user id (BR-602).</summary>
    public const string UserIdClaim = "sub";

    /// <summary>Name of the log event property that carries the tenant id.</summary>
    public const string TenantIdProperty = "TenantId";

    /// <summary>Name of the log event property that carries the user id.</summary>
    public const string UserIdProperty = "UserId";

    /// <summary>The tenant id and user id of an authenticated principal; both null for an anonymous one.</summary>
    public static (string? TenantId, string? UserId) Read(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return (null, null);
        }

        // The JWT handler may rename "sub" to the long name-identifier claim type, so both are accepted.
        return (
            user.FindFirst(TenantIdClaim)?.Value,
            user.FindFirst(UserIdClaim)?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value);
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
