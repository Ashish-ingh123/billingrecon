using System.Threading.RateLimiting;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Api.Identity;

/// <summary>
/// The refused sign-ins of each account (ADR 0035): a fixed window of <see cref="SignInLimits.AccountWindow"/> per
/// account that holds <see cref="SignInLimits.RefusalsPerAccount"/> refusals. An account is the hospital's code and the
/// address as a sign-in names them, compared as the sign-in compares them, whether or not they exist, so a held account
/// tells nothing about which accounts exist. One instance per API, in memory; an account nobody tries any more is
/// dropped by the limiter once its window has passed.
/// </summary>
public sealed class AccountSignInLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _refusals = PartitionedRateLimiter.Create<string, string>(
        account => RateLimitPartition.GetFixedWindowLimiter(
            account,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = SignInLimits.RefusalsPerAccount,
                Window = SignInLimits.AccountWindow,
                QueueLimit = 0,
                AutoReplenishment = true,
            }),
        StringComparer.Ordinal);

    /// <summary>The account a sign-in names: the code in lower case and the address as users are found by, both trimmed.</summary>
    public static string AccountOf(string? tenantCode, string? email)
        => string.Concat((tenantCode ?? string.Empty).Trim().ToLowerInvariant(), "\n", User.Normalize(email ?? string.Empty));

    /// <summary>
    /// True when <paramref name="account"/> has used its refusals in the current window; then
    /// <paramref name="retryAfter"/> is how long the limiter says to wait. Counts nothing.
    /// </summary>
    public bool IsHeld(string account, out TimeSpan retryAfter)
    {
        // Asking for no permit only tells whether one is left.
        using var lease = _refusals.AttemptAcquire(account, permitCount: 0);
        if (lease.IsAcquired)
        {
            retryAfter = TimeSpan.Zero;
            return false;
        }

        retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var value) ? value : SignInLimits.AccountWindow;
        return true;
    }

    /// <summary>Counts one refused sign-in of <paramref name="account"/>.</summary>
    public void RecordRefusal(string account) => _refusals.AttemptAcquire(account).Dispose();

    /// <inheritdoc />
    public void Dispose() => _refusals.Dispose();
}
