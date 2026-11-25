using BillingRecon.Api.Common;
using BillingRecon.Api.Common.Authorization;
using BillingRecon.Application.Common;
using BillingRecon.Application.Receivables;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BillingRecon.Api.Receivables;

/// <summary>
/// The dry run of an opening batch (ADR 0053), for the TenantAdmin: what the files would bring in, and every row that
/// stops them, with its file, line and reason. It writes nothing, so it takes no Idempotency-Key, and it is a controller
/// of its own beside <see cref="OpeningBatchesController"/>, whose every POST is financial.
/// </summary>
[Route(ApiRoutes.V1 + "/opening-batches/dry-run")]
public sealed class OpeningBatchChecksController : ApiControllerBase
{
    /// <summary>Checks the files and answers what they hold.</summary>
    [Authorize(Policy = Policies.ManageTenant)]
    [HttpPost]
    [ProducesResponseType<OpeningBatchCheckDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CheckAsync(
        OpeningBatchFilesRequest request,
        [FromServices] IQueryHandler<CheckOpeningBatchQuery, OpeningBatchCheckDto> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await handler.HandleAsync(request.ToQuery(), ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}
