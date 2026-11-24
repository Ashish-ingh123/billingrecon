using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Identity;

/// <summary>
/// What an operator does to a hospital from the server, never through the API (ADR 0052): onboard it with its first
/// administrator, suspend it and lift the suspension. Each is one transaction, audited with no user.
/// </summary>
public interface ITenantOperations
{
    /// <summary>Creates the tenant, its settings and prefixes, and its first TenantAdmin with a one-time password.</summary>
    public Task<Result<OnboardedTenant>> OnboardAsync(OnboardTenant command, CancellationToken ct);

    /// <summary>Suspends the tenant with this code: nobody of it signs in or acts; nothing is deleted.</summary>
    public Task<Result> SuspendAsync(string code, string reason, CancellationToken ct);

    /// <summary>Lifts the suspension of the tenant with this code.</summary>
    public Task<Result> ResumeAsync(string code, string reason, CancellationToken ct);
}
