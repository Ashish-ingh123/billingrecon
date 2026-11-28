using BillingRecon.Api.Common;
using BillingRecon.Api.Common.Authorization;
using BillingRecon.Application.Common;
using BillingRecon.Application.Reconciliation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BillingRecon.Api.Reconciliation;

/// <summary>
/// The lines of the hospital's bank statements (BR-1004, BR-1005): listed, filtered and sorted, as they were read. In
/// 13.1 they are only read; matching changes their status from Prompt 13.2.
/// </summary>
[Route(ApiRoutes.V1 + "/statement-lines")]
public sealed class StatementLinesController : ApiControllerBase
{
    /// <summary>Lists the lines one page at a time, the latest first.</summary>
    [Authorize(Policy = Policies.ReadReconciliation)]
    [HttpGet]
    [ProducesResponseType<PageOf<StatementLineDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] ListStatementLinesRequest request,
        [FromServices] IQueryHandler<ListStatementLinesQuery, PageOf<StatementLineDto>> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = request.ToQuery();
        if (query.IsFailure)
        {
            return Problem(query.Error);
        }

        var result = await handler.HandleAsync(query.Value, ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}
