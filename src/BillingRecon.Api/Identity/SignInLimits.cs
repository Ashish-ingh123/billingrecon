using System.Globalization;
using System.Threading.RateLimiting;
using BillingRecon.Api.Common.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BillingRecon.Api.Identity;

/// <summary>
/// How often anyone may try to sign in (ADR 0035; ADR 0031, point 12), with the rate limiter of ASP.NET Core:
/// <list type="bullet">
/// <item><b>Per client address</b>, the one the proxy that ended TLS saw: <see cref="AttemptsPerAddress"/> sign-ins in
/// <see cref="AddressWindow"/>, whatever they name and however they end. Against one source trying a common password on
/// many accounts. The policy <see cref="PolicyName"/>, on the sign-in alone.</item>
/// <item><b>Per account</b>, the hospital's code and the address as typed: after <see cref="RefusalsPerAccount"/> refused
/// sign-ins in <see cref="AccountWindow"/>, every sign-in for it is refused until the window ends, the right password
/// included; a sign-in that succeeds counts nothing. Against many sources guessing one account's password. Keyed by what
/// was typed, so an address nobody has is held exactly like a real one (<see cref="AccountSignInLimiter"/>).</item>
/// </list>
/// Both answer the same 429 <c>too_many_sign_ins</c> with <c>Retry-After</c>, so the answer does not say which limit was
/// reached. The limits are fixed here, like the lifetime of a token, and kept in memory by each instance of the API.
/// </summary>
public static class SignInLimits
{
    /// <summary>The name of the rate limiting policy of the sign-in.</summary>
    public const string PolicyName = "sign-in";

    /// <summary>Sign-ins one client address may send in <see cref="AddressWindow"/>.</summary>
    public const int AttemptsPerAddress = 30;

    /// <summary>Refused sign-ins after which an account is held until <see cref="AccountWindow"/> ends.</summary>
    public const int RefusalsPerAccount = 10;

    /// <summary>The <c>detail</c> of the 429.</summary>
    public const string Detail = "Too many sign-ins. Try again once the number of seconds in the Retry-After header has passed.";

    /// <summary>The window of <see cref="AttemptsPerAddress"/>.</summary>
    public static readonly TimeSpan AddressWindow = TimeSpan.FromMinutes(1);

    /// <summary>The window of <see cref="RefusalsPerAccount"/>.</summary>
    public static readonly TimeSpan AccountWindow = TimeSpan.FromMinutes(15);

    /// <summary>Adds both limits: the policy of the rate limiter and the per-account limiter of <see cref="SignInThrottleFilter"/>.</summary>
    public static IServiceCollection AddSignInLimits(this IServiceCollection services)
    {
        services.AddSingleton<AccountSignInLimiter>();
        services.AddScoped<SignInThrottleFilter>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(PolicyName, context => RateLimitPartition.GetFixedWindowLimiter(
                ClientAddress(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = AttemptsPerAddress,
                    Window = AddressWindow,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));

            // The sign-in's policy is the only one, so a rejection is always a sign-in from an address that sent too many.
            options.OnRejected = OnRejectedAsync;
        });

        return services;
    }

    /// <summary>
    /// The 429 for too many sign-ins: <c>Retry-After</c> in whole seconds, never stored by a cache, and the ProblemDetails
    /// with <c>too_many_sign_ins</c>.
    /// </summary>
    public static ProblemDetails TooManySignIns(HttpContext httpContext, TimeSpan retryAfter)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Response.Headers.RetryAfter = Seconds(retryAfter).ToString(CultureInfo.InvariantCulture);
        httpContext.Response.Headers.CacheControl = "no-store";

        return ApiProblems.Create(httpContext, StatusCodes.Status429TooManyRequests, ErrorCodes.TooManySignIns, Detail);
    }

    /// <summary>A duration as the whole seconds of <c>Retry-After</c>, rounded up and at least one.</summary>
    public static int Seconds(TimeSpan duration) => Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds));

    // The address after the forwarded headers: the client's, as the proxy saw it, or the connection's.
    private static string ClientAddress(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;

    private static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken ct)
    {
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var value) ? value : AddressWindow;
        var httpContext = context.HttpContext;

        var logger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SignInLimits));
        logger.LogWarning(
            "Sign-ins from {ClientAddress} are limited: more than {Attempts} in {WindowSeconds} seconds, refused for {RetryAfterSeconds} seconds",
            ClientAddress(httpContext),
            AttemptsPerAddress,
            Seconds(AddressWindow),
            Seconds(retryAfter));

        await ApiProblems.WriteAsync(httpContext, TooManySignIns(httpContext, retryAfter));
    }
}
