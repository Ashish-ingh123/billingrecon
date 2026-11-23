using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BillingRecon.Api.Identity;

/// <summary>
/// The per-account limit of the sign-in (ADR 0035). It runs once the body is bound, which the rate limiter's partition
/// cannot wait for: an account that has used its refusals is answered 429 <c>too_many_sign_ins</c> before its password is
/// checked; otherwise the sign-in runs, and a refusal (401) is counted. A body that cannot be read or fails validation
/// counts nothing. The hospital's code goes to the log, never the address.
/// </summary>
public sealed class SignInThrottleFilter(AccountSignInLimiter limiter, ILogger<SignInThrottleFilter> logger) : IAsyncActionFilter
{
    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var request = context.ActionArguments.Values.OfType<LoginRequest>().SingleOrDefault();
        if (request is null)
        {
            await next();
            return;
        }

        var account = AccountSignInLimiter.AccountOf(request.Tenant, request.Email);
        if (limiter.IsHeld(account, out var retryAfter))
        {
            logger.LogWarning(
                "Sign-ins to tenant {TenantCode} for one address are held for {RetryAfterSeconds} seconds after {Refusals} refusals",
                request.Tenant?.Trim().ToLowerInvariant(),
                SignInLimits.Seconds(retryAfter),
                SignInLimits.RefusalsPerAccount);

            context.Result = new ObjectResult(SignInLimits.TooManySignIns(context.HttpContext, retryAfter))
            {
                StatusCode = StatusCodes.Status429TooManyRequests,
            };
            return;
        }

        var executed = await next();
        if (executed.Result is ObjectResult { StatusCode: StatusCodes.Status401Unauthorized })
        {
            limiter.RecordRefusal(account);
        }
    }
}
