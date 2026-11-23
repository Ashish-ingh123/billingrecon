using BillingRecon.Api.Common.Errors;
using Microsoft.Extensions.Options;

namespace BillingRecon.Api.Common.Transport;

/// <summary>
/// Refuses a request the API must not answer, before anything of it is read (ADR 0035):
/// <list type="bullet">
/// <item>one that names a host <c>AllowedHosts</c> does not list: 400 <c>host_not_allowed</c>;</item>
/// <item>outside Development, one that did not arrive over HTTPS, directly or as a proxy of <c>ReverseProxy:KnownProxies</c>
/// says: 400 <c>https_required</c>. There is no redirect: by the time a redirect is answered, the token or the password
/// has crossed the network in clear already, and the client is to be fixed, not sent on.</item>
/// </list>
/// The two health endpoints are answered for any host and over plain HTTP: probes come from inside, by whatever name
/// reaches the container (<see cref="HealthEndpoints"/>). Placed inside the error handling, so a refusal is a
/// ProblemDetails with its correlation id like any other error.
/// </summary>
public sealed class TransportGuardMiddleware(
    RequestDelegate next,
    IOptions<AllowedHostsOptions> hosts,
    IHostEnvironment environment,
    ILogger<TransportGuardMiddleware> logger)
{
    /// <summary>The <c>detail</c> of a refused host.</summary>
    public const string HostNotAllowedDetail = "This API does not answer for the host the request names.";

    /// <summary>The <c>detail</c> of a request over plain HTTP.</summary>
    public const string HttpsRequiredDetail = "This API answers over HTTPS only.";

    private readonly bool _httpsOnly = !environment.IsDevelopment();

    /// <summary>Runs the middleware.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (HealthEndpoints.IsProbe(context.Request.Path))
        {
            await next(context);
            return;
        }

        if (!hosts.Value.Allows(context.Request.Host))
        {
            logger.LogInformation("Refused a request for the host {RequestHost}, which AllowedHosts does not list", context.Request.Host.Value);
            await ApiProblems.WriteAsync(
                context,
                ApiProblems.Create(context, StatusCodes.Status400BadRequest, ErrorCodes.HostNotAllowed, HostNotAllowedDetail));
            return;
        }

        if (_httpsOnly && !context.Request.IsHttps)
        {
            await ApiProblems.WriteAsync(
                context,
                ApiProblems.Create(context, StatusCodes.Status400BadRequest, ErrorCodes.HttpsRequired, HttpsRequiredDetail));
            return;
        }

        await next(context);
    }
}
