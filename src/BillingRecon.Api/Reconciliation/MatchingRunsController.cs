using BillingRecon.Api.Common;
using BillingRecon.Api.Common.Authorization;
using BillingRecon.Application.Common;
using BillingRecon.Application.Reconciliation;
using BillingRecon.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BillingRecon.Api.Reconciliation;

/// <summary>
/// A matching run over a bank channel (BR-1010, BR-1011, BR-1029; ADR 0066): run by the import wizard after an import and
/// by "Match now". It posts nothing and a second run at once is safe (the unique indexes keep a line and an item in one
/// active match), so it is not a financial POST and takes no Idempotency-Key.
/// </summary>
[Route(ApiRoutes.V1 + "/settlement-channels/{id:guid}/matching-runs")]
public sealed class MatchingRunsController : ApiControllerBase
{
    /// <summary>Matches what is certain and proposes the rest; answers the counts.</summary>
    [Authorize(Policy = Policies.ImportStatements)]
    [HttpPost]
    [ProducesResponseType<MatchingRunDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RunAsync(
        Guid id,
        [FromServices] ICommandHandler<RunMatchingCommand, MatchingRunDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new RunMatchingCommand(new SettlementChannelId(id)), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}
