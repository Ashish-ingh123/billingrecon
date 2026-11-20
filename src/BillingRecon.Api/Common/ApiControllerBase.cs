using System.Diagnostics.CodeAnalysis;
using BillingRecon.Api.Common.Errors;
using BillingRecon.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace BillingRecon.Api.Common;

/// <summary>Base class for every API controller. Module controllers derive from this.</summary>
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>
    /// Answers with the RFC 9457 response for a business failure: the status follows the error kind and
    /// <c>errorCode</c> carries the error code.
    /// </summary>
    protected ObjectResult Problem(Error error)
    {
        var problem = error.ToProblemDetails(HttpContext);

        return new ObjectResult(problem) { StatusCode = problem.Status };
    }

    /// <summary>200 with <paramref name="value"/>, and its <paramref name="version"/> as the ETag (ADR 0007).</summary>
    protected OkObjectResult OkWithVersion(object value, string version)
    {
        Response.Headers.ETag = EntityTags.Format(version);

        return Ok(value);
    }

    /// <summary>
    /// 201 with the new record: its address, from the route named <paramref name="routeName"/>, in the Location header,
    /// and its <paramref name="version"/> as the ETag (ADR 0007).
    /// </summary>
    protected CreatedAtRouteResult CreatedWithVersion(string routeName, Guid id, object value, string version)
    {
        Response.Headers.ETag = EntityTags.Format(version);

        return CreatedAtRoute(routeName, new { id }, value);
    }

    /// <summary>
    /// The version the client read, from the If-Match header, which every change of an existing record needs (BR-804,
    /// ADR 0007). Without the header the request is refused with 428 <c>precondition_required</c>; with anything but one
    /// strong ETag as this API issues them, with 400 <c>validation_failed</c>.
    /// </summary>
    protected bool TryGetExpectedVersion([NotNullWhen(true)] out string? version, [NotNullWhen(false)] out IActionResult? refusal)
    {
        var values = Request.Headers.IfMatch;
        if (StringValues.IsNullOrEmpty(values))
        {
            version = null;
            refusal = PreconditionRequired();
            return false;
        }

        if (values.Count == 1 && EntityTags.TryParse(values[0], out version))
        {
            refusal = null;
            return true;
        }

        version = null;
        refusal = Problem(CommonErrors.ValidationFailed(new Dictionary<string, string[]>
        {
            [HeaderNames.IfMatch] = ["Send the ETag of the version you read, exactly as the API returned it: one ETag, not a list, not *."],
        }));
        return false;
    }

    private ObjectResult PreconditionRequired()
    {
        var problem = ProblemDetailsFactory.CreateProblemDetails(
            HttpContext,
            StatusCodes.Status428PreconditionRequired,
            detail: "This request changes a record that already exists. Send the ETag of the version you read in the If-Match header (BR-804).");

        return new ObjectResult(problem) { StatusCode = problem.Status };
    }
}
