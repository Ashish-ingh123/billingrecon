using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace BillingRecon.Api.Common.Errors;

/// <summary>
/// The ProblemDetails of a refusal the API makes on its own, before a use case runs: a host it does not answer for, a
/// request over plain HTTP, too many sign-ins (ADR 0035). Built by the factory every other error is built by, so the
/// body carries <c>type</c>, <c>title</c>, <c>errorCode</c>, <c>traceId</c> and <c>correlationId</c> like the rest
/// (ADR 0011).
/// </summary>
public static class ApiProblems
{
    /// <summary>The ProblemDetails for <paramref name="statusCode"/>, with <paramref name="errorCode"/> and <paramref name="detail"/>.</summary>
    public static ProblemDetails Create(HttpContext httpContext, int statusCode, string errorCode, string detail)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var factory = httpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = factory.CreateProblemDetails(httpContext, statusCode, detail: detail);

        // The factory has applied the default code for the status; the refusal's own code replaces it.
        problem.Extensions[ProblemDetailsEnricher.ErrorCodeKey] = errorCode;

        return problem;
    }

    /// <summary>Answers the request with <paramref name="problem"/>, from a middleware, where no action result runs.</summary>
    public static async Task WriteAsync(HttpContext httpContext, ProblemDetails problem)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(problem);

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        var problems = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problems.WriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem });
    }
}
