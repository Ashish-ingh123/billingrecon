using BillingRecon.Api.Common.Logging;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace BillingRecon.Api.Common.Errors;

/// <summary>
/// The last line of defence: turns an exception nobody handled into a 500 ProblemDetails with
/// <c>errorCode = internal_error</c> and logs it once. Exception details reach the client only in Development.
/// A request the server refused to read keeps its own status (400, 408, 413) and is not logged as an error.
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>Name of the ProblemDetails extension that carries exception details in Development.</summary>
    public const string ExceptionKey = "exception";

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // A request the client abandoned never gets here: the framework answers 499 for it without calling handlers.
        if (exception is BadHttpRequestException badRequest)
        {
            return await RejectAsync(httpContext, badRequest);
        }

        // The identity scope of the request ended when the exception left the pipeline, so it is restored for this event.
        using (RequestIdentity.PushToLogContext(httpContext.User))
        {
            logger.LogError(
                exception,
                "Unhandled exception while handling {RequestMethod} {RequestPath}",
                httpContext.Request.Method,
                httpContext.Request.Path.Value);
        }

        var problem = new ProblemDetails { Status = StatusCodes.Status500InternalServerError };
        problem.Extensions[ProblemDetailsEnricher.ErrorCodeKey] = ErrorCodes.InternalError;

        if (environment.IsDevelopment())
        {
            problem.Extensions[ExceptionKey] = new Dictionary<string, string?>
            {
                ["type"] = exception.GetType().FullName,
                ["message"] = exception.Message,
                ["details"] = exception.ToString(),
            };
        }

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    // The server refused to read the request: too large, malformed or too slow. That is the client's fault and the
    // exception carries the status to answer with (400, 408, 413), so it is neither a 500 nor an error in the log.
    private async Task<bool> RejectAsync(HttpContext httpContext, BadHttpRequestException exception)
    {
        logger.LogInformation(
            "Rejected {RequestMethod} {RequestPath} with status {StatusCode}: {Reason}",
            httpContext.Request.Method,
            httpContext.Request.Path.Value,
            exception.StatusCode,
            exception.Message);

        httpContext.Response.StatusCode = exception.StatusCode;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails { Status = exception.StatusCode },
        });
    }
}
