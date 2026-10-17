using Serilog;
using Serilog.Events;

namespace BillingRecon.Api.Common.Logging;

/// <summary>
/// The one-event-per-request log. The event carries method, path (never the query string, which may hold personal
/// data), status and duration, plus the correlation id and, when known, the tenant and user.
/// </summary>
public static class RequestLogging
{
    private static readonly PathString HealthPath = new("/health");

    /// <summary>Adds the request log to the pipeline. Place it right after the correlation id middleware.</summary>
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app)
    {
        // The Serilog middleware writes to the static Log.Logger unless it is given a logger, and this application
        // leaves the static logger unset on purpose (see AddInfrastructure). It gets the host's own logger instead.
        var logger = app.ApplicationServices.GetRequiredService<Serilog.ILogger>();

        return app.UseSerilogRequestLogging(options =>
        {
            options.Logger = logger;
            options.IncludeQueryInRequestPath = false;
            options.GetLevel = GetLevel;
            options.EnrichDiagnosticContext = Enrich;
        });
    }

    private static LogEventLevel GetLevel(HttpContext httpContext, double elapsedMilliseconds, Exception? exception)
    {
        if (exception is not null || httpContext.Response.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            return LogEventLevel.Error;
        }

        // Probes arrive every few seconds; a healthy answer is not worth a log line.
        return httpContext.Request.Path.StartsWithSegments(HealthPath) ? LogEventLevel.Verbose : LogEventLevel.Information;
    }

    private static void Enrich(IDiagnosticContext diagnosticContext, HttpContext httpContext)
    {
        // The identity scope pushed further down the pipeline is gone by the time this event is written, so the
        // tenant and user are set on the event directly.
        var (tenantId, userId) = RequestIdentity.Read(httpContext.User);
        if (tenantId is not null)
        {
            diagnosticContext.Set(RequestIdentity.TenantIdProperty, tenantId);
        }

        if (userId is not null)
        {
            diagnosticContext.Set(RequestIdentity.UserIdProperty, userId);
        }
    }
}
