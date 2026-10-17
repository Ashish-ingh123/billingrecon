using BillingRecon.Api.Common.Errors;
using BillingRecon.Domain.Common;
using Microsoft.AspNetCore.Mvc;

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
}
