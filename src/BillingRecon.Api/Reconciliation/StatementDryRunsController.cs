using BillingRecon.Api.Common;
using BillingRecon.Api.Common.Authorization;
using BillingRecon.Application.Common;
using BillingRecon.Application.Reconciliation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BillingRecon.Api.Reconciliation;

/// <summary>
/// The dry run of a bank's statement (BR-1003; the pattern of ADR 0053): what the file would bring in, what the channel
/// already holds, and every row it refuses with its row number and reason. It stores nothing, so it takes no
/// Idempotency-Key, and it is a controller of its own beside <see cref="StatementImportsController"/>, whose POST imports.
/// </summary>
[Route(ApiRoutes.V1 + "/statement-imports/dry-run")]
public sealed class StatementDryRunsController : ApiControllerBase
{
    /// <summary>Reads the file with the channel's mapping and answers what importing it would do.</summary>
    [Authorize(Policy = Policies.ImportStatements)]
    [HttpPost]
    [ProducesResponseType<StatementDryRunDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> DryRunAsync(
        StatementFileRequest request,
        [FromServices] IQueryHandler<DryRunStatementQuery, StatementDryRunDto> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await handler.HandleAsync(request.ToQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}
