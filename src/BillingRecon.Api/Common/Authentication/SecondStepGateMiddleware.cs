using BillingRecon.Api.Common.Errors;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Api.Common.Authentication;

/// <summary>
/// A token between the two steps of a sign-in reaches its second step and nothing else (ADR 0061), as a token that
/// only changes its password reaches that alone (ADR 0051). One that waits for the code of a second step reaches the
/// actions marked <see cref="AllowWhileSecondStepPendingAttribute"/>: 403 <c>second_step_required</c> otherwise. One
/// that must enrol a second step reaches those marked <see cref="AllowWhileSecondStepEnrolmentAttribute"/>: 403
/// <c>second_step_enrolment_required</c> otherwise. Above all, a token that waits for a code cannot enrol a new second
/// step: the password alone never replaces the one there is. After authorisation, so a request without a token is 401 first.
/// </summary>
public sealed class SecondStepGateMiddleware(RequestDelegate next)
{
    /// <summary>Lets the request through, or answers 403.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var metadata = context.GetEndpoint()?.Metadata;
        var error = TokenClaims.SecondStepOf(context.User) switch
        {
            SecondStepStage.Pending when metadata?.GetMetadata<AllowWhileSecondStepPendingAttribute>() is null => IdentityErrors.SecondStepRequired(),
            SecondStepStage.Enrolment when metadata?.GetMetadata<AllowWhileSecondStepEnrolmentAttribute>() is null => IdentityErrors.SecondStepEnrolmentRequired(),
            _ => null,
        };

        if (error is not null)
        {
            await ApiProblems.WriteAsync(context, ApiProblems.Create(context, StatusCodes.Status403Forbidden, error.Code, error.Message));
            return;
        }

        await next(context);
    }
}
