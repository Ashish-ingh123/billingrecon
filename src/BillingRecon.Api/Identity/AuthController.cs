using BillingRecon.Api.Common;
using BillingRecon.Api.Common.Authentication;
using BillingRecon.Api.Common.Authorization;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BillingRecon.Api.Identity;

/// <summary>
/// Signing in (US-02; BR-601, BR-602; ADR 0031). The one action of the API that needs no token, since it is how a
/// client gets one. Every refusal answers the same 401 <c>invalid_credentials</c>; why it was refused goes to the log
/// only, with the tenant's code and, when known, the user's id, never the address or the password. No answer of it may
/// be stored by a cache. It takes no Idempotency-Key, because its body holds a password (ADR 0030, point 7). Its two
/// rate limits, per client address and per account, answer 429 <c>too_many_sign_ins</c> (ADR 0035).
/// </summary>
[Route(ApiRoutes.V1 + "/auth")]
public sealed class AuthController(ILogger<AuthController> logger) : ApiControllerBase
{
    /// <summary>Signs in: the token for the user, its type and when it expires.</summary>
    [AllowAnonymous]
    [EnableRateLimiting(SignInLimits.PolicyName)]
    [SignInThrottle]
    [HttpPost("login")]
    [ProducesResponseType<SignedInDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> LoginAsync(
        LoginRequest request,
        [FromServices] ICommandHandler<SignInCommand, SignInOutcome> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        Response.Headers.CacheControl = "no-store";

        var result = await handler.HandleAsync(request.ToCommand(), ct);
        if (result.IsFailure)
        {
            return Problem(result.Error);
        }

        if (result.Value.SignedIn is { } signedIn)
        {
            return Ok(signedIn);
        }

        logger.LogWarning(
            "A sign-in to tenant {TenantCode} was refused: {Refusal}, user {UserId}",
            request.Tenant?.Trim().ToLowerInvariant(),
            result.Value.Refusal,
            result.Value.UserId);

        return Problem(IdentityErrors.InvalidCredentials());
    }

    /// <summary>
    /// The signed-in user sets a password of their own, giving the current one (ADR 0051). The one action a user whose
    /// password change is due may call; the answer is a new token without that mark.
    /// </summary>
    [Authorize(Policy = Policies.SignedIn)]
    [AllowWhilePasswordChangeDue]
    [HttpPost("password")]
    [ProducesResponseType<SignedInDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        [FromServices] ICommandHandler<ChangeOwnPasswordCommand, SignedInDto> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        Response.Headers.CacheControl = "no-store";
        var result = await handler.HandleAsync(request.ToCommand(), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>
    /// Signs the user out (ADR 0057): every token issued to the user so far, on every device, answers 401
    /// <c>session_ended</c> from the next request on. It takes no body, and a user whose password change is due may call it.
    /// </summary>
    [Authorize(Policy = Policies.SignedIn)]
    [AllowWhilePasswordChangeDue]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> LogoutAsync(
        [FromServices] ICommandHandler<SignOutCommand, bool> handler,
        CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var result = await handler.HandleAsync(new SignOutCommand(), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }
}
