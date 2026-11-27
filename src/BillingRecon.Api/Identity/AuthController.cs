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
    [AllowWhileSecondStepPending]
    [AllowWhileSecondStepEnrolment]
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
    /// <summary>
    /// The second step of a sign-in (ADR 0061): the code of the authenticator app, or a recovery code, with the token the
    /// password gave. Answers a sign-in with a full token. A wrong code is 422, and the fifth in a row ends the sign-in.
    /// </summary>
    [Authorize(Policy = Policies.SignedIn)]
    [AllowWhileSecondStepPending]
    [HttpPost("second-step")]
    [ProducesResponseType<SignedInDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SecondStepAsync(
        SecondStepCodeRequest request,
        [FromServices] ICommandHandler<VerifySecondStepCommand, SignedInDto> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        Response.Headers.CacheControl = "no-store";
        var result = await handler.HandleAsync(new VerifySecondStepCommand(request.Code), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>
    /// Begins the enrolment of a second step for the user who asks (ADR 0061): answers the secret for an authenticator
    /// app, as text and as the address a QR code holds. Open to a token that only enrols, and to any full token of a
    /// user who has none.
    /// </summary>
    [Authorize(Policy = Policies.SignedIn)]
    [AllowWhileSecondStepEnrolment]
    [HttpPost("second-step/enrolment")]
    [ProducesResponseType<SecondStepEnrolmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> BeginSecondStepEnrolmentAsync(
        [FromServices] ICommandHandler<BeginSecondStepEnrolmentCommand, SecondStepEnrolmentDto> handler,
        CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var result = await handler.HandleAsync(new BeginSecondStepEnrolmentCommand(), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>
    /// Confirms the enrolment with the first code of the authenticator app (ADR 0061): answers the recovery codes, this
    /// once, and the sign-in of a new session whose second step is given. Every other session of the user ends.
    /// </summary>
    [Authorize(Policy = Policies.SignedIn)]
    [AllowWhileSecondStepEnrolment]
    [HttpPost("second-step/enrolment/confirm")]
    [ProducesResponseType<SecondStepEnrolledDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ConfirmSecondStepEnrolmentAsync(
        SecondStepCodeRequest request,
        [FromServices] ICommandHandler<ConfirmSecondStepEnrolmentCommand, SecondStepEnrolledDto> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        Response.Headers.CacheControl = "no-store";
        var result = await handler.HandleAsync(new ConfirmSecondStepEnrolmentCommand(request.Code), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
    /// <summary>
    /// What the sign-in page may offer here (ADR 0061, amended): whether a forgotten password can be reset by a link in a
    /// mail. Needs no token, and says nothing about any hospital or user.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("options")]
    [ProducesResponseType<SignInOptionsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> OptionsAsync(
        [FromServices] IQueryHandler<SignInOptionsQuery, SignInOptionsDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new SignInOptionsQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>
    /// Asks for a link to reset a forgotten password (ADR 0061, amended). Always 202 with no body, whether or not the
    /// hospital and the address exist, the user may sign in, or a mail server is set up: the answer tells nothing.
    /// Limited per client address like the sign-in, and to five links an hour for one user.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting(SignInLimits.PolicyName)]
    [HttpPost("forgot-password")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ForgotPasswordAsync(
        ForgotPasswordRequest request,
        [FromServices] ICommandHandler<RequestPasswordResetCommand, bool> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        Response.Headers.CacheControl = "no-store";
        var result = await handler.HandleAsync(request.ToCommand(), ct);
        return result.IsSuccess ? Accepted() : Problem(result.Error);
    }

    /// <summary>
    /// Sets a new password with the link of the mail (ADR 0061, amended): good once, for 30 minutes. 204 on success, and
    /// every session of the user ends; 422 <c>password_reset_link_invalid</c> for a link that is not good, whatever the reason.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting(SignInLimits.PolicyName)]
    [HttpPost("reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResetPasswordAsync(
        ResetPasswordRequest request,
        [FromServices] ICommandHandler<ResetPasswordByLinkCommand, bool> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        Response.Headers.CacheControl = "no-store";
        var result = await handler.HandleAsync(request.ToCommand(), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }
}
