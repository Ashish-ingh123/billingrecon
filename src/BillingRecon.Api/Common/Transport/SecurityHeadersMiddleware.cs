using Microsoft.Extensions.Primitives;

namespace BillingRecon.Api.Common.Transport;

/// <summary>
/// The headers that keep a browser or a cache in front of it from misusing an answer of the API (ADR 0035):
/// <list type="bullet">
/// <item><c>X-Content-Type-Options: nosniff</c> on every response: a JSON body is never read as a script or a page;</item>
/// <item>on the answers of <c>/api</c>, <c>Cache-Control: no-store</c> unless the action set its own: patient and
/// financial data is never kept by a browser or a proxy; and <c>Content-Security-Policy: default-src 'none';
/// frame-ancestors 'none'</c>: an answer loads nothing and is shown in no frame.</item>
/// </list>
/// Written when the response starts, so an error answer carries them too, after the exception handler cleared the
/// response. The OpenAPI document and the Scalar page of Development are outside <c>/api</c> and keep what they need.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    /// <summary>The value of <c>X-Content-Type-Options</c>.</summary>
    public const string NoSniff = "nosniff";

    /// <summary>The value of <c>Cache-Control</c> on an answer of <c>/api</c> that sets none.</summary>
    public const string NoStore = "no-store";

    /// <summary>The value of <c>Content-Security-Policy</c> on an answer of <c>/api</c>.</summary>
    public const string ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

    private static readonly PathString ApiPath = new("/api");

    /// <summary>Runs the middleware.</summary>
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.OnStarting(AddHeaders, context);

        return next(context);
    }

    private static Task AddHeaders(object state)
    {
        var context = (HttpContext)state;
        var headers = context.Response.Headers;

        headers.XContentTypeOptions = NoSniff;

        if (context.Request.Path.StartsWithSegments(ApiPath))
        {
            if (StringValues.IsNullOrEmpty(headers.CacheControl))
            {
                headers.CacheControl = NoStore;
            }

            headers.ContentSecurityPolicy = ContentSecurityPolicy;
        }

        return Task.CompletedTask;
    }
}
