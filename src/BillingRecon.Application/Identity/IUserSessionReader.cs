using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Identity;

/// <summary>
/// Whether a token still belongs to a session of its user (ADR 0057). Asked on every request that carries a token, and
/// never answered from a cache: a sign-out holds from the next request on.
/// </summary>
public interface IUserSessionReader
{
    /// <summary>
    /// True when user <paramref name="userId"/> exists in the tenant of the request, is active, and has
    /// <paramref name="sessionVersion"/> as the number of its sessions.
    /// </summary>
    public Task<bool> IsCurrentAsync(UserId userId, int sessionVersion, CancellationToken ct);
}
