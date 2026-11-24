using BillingRecon.Api.Common.Errors;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Api.Common.Authentication;

/// <summary>
/// While a user's password change is due (their token carries <see cref="TokenClaims.PasswordChangeClaim"/>), every
/// endpoint but the one marked <see cref="AllowWhilePasswordChangeDueAttribute"/> answers 403
/// <c>password_change_required</c> (ADR 0051). Anonymous endpoints (the sign-in, the health checks) carry no token and are
/// not concerned. Runs after authentication, so the principal is the token's.
/// </summary>
public sealed class PasswordChangeGateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var allowed = context.GetEndpoint()?.Metadata.GetMetadata<AllowWhilePasswordChangeDueAttribute>() is not null;
        if (!allowed && TokenClaims.MustChangePassword(context.User))
        {
            var error = IdentityErrors.PasswordChangeRequired();
            var problem = ApiProblems.Create(context, StatusCodes.Status403Forbidden, error.Code, error.Message);
            await ApiProblems.WriteAsync(context, problem);
            return;
        }

        await next(context);
    }
}
