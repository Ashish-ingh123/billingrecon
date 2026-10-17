using System.Diagnostics;
using BillingRecon.Api.Common.Correlation;
using Microsoft.AspNetCore.WebUtilities;

namespace BillingRecon.Api.Common.Errors;

/// <summary>
/// Makes every RFC 9457 response complete, whoever produced it (a controller, model binding, routing or the exception
/// handler): <c>type</c> and <c>title</c> are always present, and so are the three extensions <c>errorCode</c>,
/// <c>traceId</c> and <c>correlationId</c> (CLAUDE.md section 5).
/// </summary>
public static class ProblemDetailsEnricher
{
    public const string ErrorCodeKey = "errorCode";
    public const string TraceIdKey = "traceId";
    public const string CorrelationIdKey = "correlationId";

    // RFC 9457: a problem without a type of its own is "about:blank", and its title is the HTTP status phrase.
    private const string DefaultType = "about:blank";
    private const string DefaultTitle = "Error";

    /// <summary>The <c>CustomizeProblemDetails</c> callback registered in <c>Program.cs</c>.</summary>
    public static void Enrich(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        var httpContext = context.HttpContext;
        var statusCode = problem.Status ?? httpContext.Response.StatusCode;

        // The framework fills these for the statuses it has a table entry for (400, 404, 500, ...), not for the rest
        // (413, 428, 429, 503, ...).
        problem.Type ??= DefaultType;
        if (string.IsNullOrEmpty(problem.Title))
        {
            var reasonPhrase = ReasonPhrases.GetReasonPhrase(statusCode);
            problem.Title = string.IsNullOrEmpty(reasonPhrase) ? DefaultTitle : reasonPhrase;
        }

        // A code set by whoever built the response (a business error, the exception handler) wins over the default.
        problem.Extensions.TryAdd(ErrorCodeKey, ErrorCodes.ForStatus(statusCode, problem is HttpValidationProblemDetails));
        problem.Extensions.TryAdd(TraceIdKey, Activity.Current?.Id ?? httpContext.TraceIdentifier);
        problem.Extensions[CorrelationIdKey] = httpContext.GetCorrelationId();
    }
}
