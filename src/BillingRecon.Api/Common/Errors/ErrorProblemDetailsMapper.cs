using BillingRecon.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BillingRecon.Api.Common.Errors;

/// <summary>Maps a business <see cref="Error"/> to its RFC 9457 response (CLAUDE.md section 5).</summary>
public static class ErrorProblemDetailsMapper
{
    /// <summary>The HTTP status for an error kind.</summary>
    public static int ToStatusCode(this ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.BusinessRule => StatusCodes.Status422UnprocessableEntity,
        ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown error kind."),
    };

    /// <summary>
    /// Builds the ProblemDetails for a business failure: status from the kind, <c>detail</c> from the message,
    /// <c>errorCode</c> from the code and, for a validation failure, <c>errors</c> per field.
    /// </summary>
    public static ProblemDetails ToProblemDetails(this Error error, HttpContext httpContext)
    {
        var factory = httpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var statusCode = error.Kind.ToStatusCode();

        ProblemDetails problem = error.Kind == ErrorKind.Validation
            ? CreateValidationProblem(factory, httpContext, error, statusCode)
            : factory.CreateProblemDetails(httpContext, statusCode, detail: error.Message);

        // The factory has already applied the default code for the status; the business code replaces it.
        problem.Extensions[ProblemDetailsEnricher.ErrorCodeKey] = error.Code;

        return problem;
    }

    // The factory supplies the defaults; the field errors are copied straight across. Passing them through a
    // ModelStateDictionary would stop at 200 errors and merge field names that differ only in case.
    private static ValidationProblemDetails CreateValidationProblem(
        ProblemDetailsFactory factory, HttpContext httpContext, Error error, int statusCode)
    {
        var problem = factory.CreateValidationProblemDetails(httpContext, new ModelStateDictionary(), statusCode, detail: error.Message);
        foreach (var (field, messages) in error.FieldErrors)
        {
            problem.Errors[field] = [.. messages];
        }

        return problem;
    }
}
