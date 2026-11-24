using BillingRecon.Api.Common.Errors;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Api.Common.Authentication;

/// <summary>
/// A request of a suspended hospital answers 403 <c>tenant_suspended</c> (ADR 0052), also when its token was issued
/// before the suspension; the answer of <see cref="ITenantStatusReader"/> is at most 30 seconds old. Anonymous requests
/// (the sign-in, which refuses a suspended hospital itself, and the health checks) are not concerned.
/// </summary>
public sealed class SuspendedTenantGateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext, ITenantStatusReader status)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(status);

        if (context.User.Identity?.IsAuthenticated == true
            && tenantContext.TenantId is { } tenantId
            && !await status.IsActiveAsync(tenantId, context.RequestAborted))
        {
            var error = IdentityErrors.TenantSuspended();
            var problem = ApiProblems.Create(context, StatusCodes.Status403Forbidden, error.Code, error.Message);
            await ApiProblems.WriteAsync(context, problem);
            return;
        }

        await next(context);
    }
}
