using Microsoft.Extensions.Primitives;
using Serilog.Context;

namespace BillingRecon.Api.Common.Correlation;

/// <summary>
/// Gives every request a correlation id (BR-703). A well-formed <c>X-Correlation-Id</c> request header is honoured,
/// anything else is replaced by a new id, the id is always returned in the response header, and every log event
/// written while the request runs carries it as <c>CorrelationId</c>.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    /// <summary>Request and response header that carries the correlation id.</summary>
    public const string HeaderName = "X-Correlation-Id";

    /// <summary>Name of the log event property that carries the correlation id.</summary>
    public const string LogPropertyName = "CorrelationId";

    /// <summary>Runs the middleware.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ReadFromRequest(context.Request.Headers[HeaderName]) ?? Guid.NewGuid();
        context.SetCorrelationId(correlationId);

        // Written when the response starts, so it survives the exception handler clearing the response.
        context.Response.OnStarting(WriteResponseHeaderAsync, context);

        using (LogContext.PushProperty(LogPropertyName, correlationId))
        {
            await next(context);
        }
    }

    // The id is written to logs and to uniqueidentifier audit columns, so only a canonical GUID is accepted.
    private static Guid? ReadFromRequest(StringValues header)
        => header.Count == 1 && Guid.TryParseExact(header[0], "D", out var correlationId) && correlationId != Guid.Empty
            ? correlationId
            : null;

    private static Task WriteResponseHeaderAsync(object state)
    {
        var context = (HttpContext)state;
        context.Response.Headers[HeaderName] = context.GetCorrelationId().ToString();

        return Task.CompletedTask;
    }
}
