namespace BillingRecon.Api.Common.Logging;

/// <summary>
/// Adds <c>TenantId</c> and <c>UserId</c> to the log events of an authenticated request. It must sit after
/// authentication in the pipeline, because that is what establishes the user.
/// </summary>
public sealed class RequestIdentityLoggingMiddleware(RequestDelegate next)
{
    /// <summary>Runs the middleware.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        using (RequestIdentity.PushToLogContext(context.User))
        {
            await next(context);
        }
    }
}
