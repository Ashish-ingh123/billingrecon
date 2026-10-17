using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// The tenant and user outside the Development environment until authentication arrives (Phase 5): there are none,
/// and asking is an error. Nothing can therefore read or write tenant data, or be recorded as "the system", by
/// accident. The API still starts and answers requests that need no identity (health, service information).
/// </summary>
public sealed class UnavailableIdentity : ITenantContext, ICurrentUser
{
    private const string Message =
        "No tenant or user is available: authentication is not implemented yet (Phase 5), and outside the Development environment nothing may act for a tenant until it is.";

    /// <summary>Always throws.</summary>
    public TenantId? TenantId => throw new InvalidOperationException(Message);

    /// <summary>Always throws.</summary>
    public UserId? UserId => throw new InvalidOperationException(Message);
}
