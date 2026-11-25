using BillingRecon.Api.Common.Errors;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Api.Common.Authentication;

/// <summary>
/// A request whose token belongs to a session that has ended answers 401 <c>session_ended</c> (ADR 0057): the user signed
/// out, changed the password or had it reset, lost or gained a role, was disabled, or the token names no session at all.
/// <see cref="IUserSessionReader"/> is asked on every request, so a sign-out holds from the next one on. Runs after
/// authentication and before authorization. Anonymous requests carry no token and are not concerned, and neither is the
/// development identity, which is no token (ADR 0031, point 9).
/// </summary>
public sealed class SessionGateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IUserSessionReader sessions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sessions);

        var identity = context.User.Identity;
        if (identity?.IsAuthenticated == true
            && identity.AuthenticationType != DevelopmentAuthenticationHandler.SchemeName
            && !await IsCurrentAsync(context, sessions))
        {
            var error = IdentityErrors.SessionEnded();
            var problem = ApiProblems.Create(context, StatusCodes.Status401Unauthorized, error.Code, error.Message);
            await ApiProblems.WriteAsync(context, problem);
            return;
        }

        await next(context);
    }

    private static async Task<bool> IsCurrentAsync(HttpContext context, IUserSessionReader sessions)
        => TokenClaims.TryRead(context.User, out _, out var userId)
            && TokenClaims.TryReadSession(context.User, out var sessionVersion)
            && await sessions.IsCurrentAsync(userId, sessionVersion, context.RequestAborted);
}
