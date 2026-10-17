using Microsoft.Extensions.Options;

namespace BillingRecon.Api.Common.Errors;

/// <summary>
/// The built-in writers decline when the request's Accept header rules out JSON, which would leave an error with an
/// empty body. This writer is registered last and answers with <c>application/problem+json</c> anyway, so an error
/// from this API is always a ProblemDetails.
/// </summary>
public sealed class FallbackProblemDetailsWriter(IOptions<ProblemDetailsOptions> options) : IProblemDetailsWriter
{
    private const string ProblemJson = "application/problem+json";

    /// <inheritdoc />
    public bool CanWrite(ProblemDetailsContext context) => true;

    /// <inheritdoc />
    public ValueTask WriteAsync(ProblemDetailsContext context)
    {
        var httpContext = context.HttpContext;
        var problem = context.ProblemDetails;

        // The registered callback (ProblemDetailsEnricher) supplies type, title and the extensions.
        problem.Status ??= httpContext.Response.StatusCode;
        options.Value.CustomizeProblemDetails?.Invoke(context);

        return new ValueTask(httpContext.Response.WriteAsJsonAsync(
            problem,
            problem.GetType(),
            options: null,
            contentType: ProblemJson,
            httpContext.RequestAborted));
    }
}
