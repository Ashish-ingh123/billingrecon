namespace BillingRecon.Api.Common.Authentication;

/// <summary>
/// Whether a token is within its lifetime by the application's clock (ADR 0031, point 2): it has an expiry, and the
/// instant is from its <c>nbf</c> to its <c>exp</c>, each widened by the tolerance for clocks, as the library counts
/// them. The library's own check reads the system clock; this one is given the clock that issued the token, so a test
/// that moves the clock moves the expiry too.
/// </summary>
public static class TokenLifetime
{
    /// <summary>True when a token valid from <paramref name="notBefore"/> to <paramref name="expires"/> is valid at <paramref name="now"/>.</summary>
    /// <param name="notBefore">The <c>nbf</c> of the token, in UTC; none means no lower bound.</param>
    /// <param name="expires">The <c>exp</c> of the token, in UTC; a token without one is never valid.</param>
    /// <param name="now">The instant, by the application's clock.</param>
    /// <param name="clockSkew">How far the clocks may disagree.</param>
    public static bool IsCurrent(DateTime? notBefore, DateTime? expires, DateTimeOffset now, TimeSpan clockSkew)
    {
        var instant = now.UtcDateTime;

        return expires is { } until
            && Utc(until) >= instant - clockSkew
            && (notBefore is not { } from || Utc(from) <= instant + clockSkew);
    }

    // The times of a token are read as UTC; a local one, which the library never gives, is converted all the same.
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
}
