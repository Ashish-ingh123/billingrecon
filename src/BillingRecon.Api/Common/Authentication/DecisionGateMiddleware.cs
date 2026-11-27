using BillingRecon.Api.Common.Authorization;
using BillingRecon.Api.Common.Errors;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Identity;
using Microsoft.AspNetCore.Authorization;

namespace BillingRecon.Api.Common.Authentication;

/// <summary>
/// What a decision needs beyond the checker's role (ADR 0061), asked once, in front of every approve and reject
/// action there is or will be: every POST whose policy is <see cref="Policies.Decide"/>.
/// <para>
/// A sign-in whose second step was given: 403 <c>second_step_required</c> for a token without it, wherever the
/// requirement is in force. And a checker who is not on hold: for 24 hours after an administrator reset a user's
/// password or second step, that user decides nothing, 403 <c>approval_on_hold</c>, because who made the reset could
/// sign in as them (review finding R5). The hold is read from the user's row on each decision and kept nowhere.
/// </para>
/// </summary>
public sealed class DecisionGateMiddleware(RequestDelegate next)
{
    /// <summary>Lets the request through, or answers 403.</summary>
    public async Task InvokeAsync(HttpContext context, ISecondStepPolicy policy, IApprovalHolds holds, ICurrentUser currentUser, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(holds);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(clock);

        var decides = HttpMethods.IsPost(context.Request.Method)
            && context.GetEndpoint()?.Metadata.GetOrderedMetadata<AuthorizeAttribute>().Any(attribute => attribute.Policy == Policies.Decide) == true;
        if (decides && context.User.Identity?.IsAuthenticated == true)
        {
            // The development identity is no sign-in and has no second step to give (ADR 0031 point 9).
            var signedInWithToken = context.User.Identity.AuthenticationType != DevelopmentAuthenticationHandler.SchemeName;
            var error = policy.IsRequired && signedInWithToken && TokenClaims.SecondStepOf(context.User) != SecondStepStage.Done
                ? IdentityErrors.SecondStepRequired()
                : currentUser.UserId is { } userId && await holds.HoldUntilAsync(userId, context.RequestAborted) is { } until && until > clock.UtcNow
                    ? IdentityErrors.ApprovalOnHold(until)
                    : null;

            if (error is not null)
            {
                await ApiProblems.WriteAsync(context, ApiProblems.Create(context, StatusCodes.Status403Forbidden, error.Code, error.Message));
                return;
            }
        }

        await next(context);
    }
}
