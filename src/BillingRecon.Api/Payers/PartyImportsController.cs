using BillingRecon.Api.Common;
using BillingRecon.Api.Common.Authorization;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BillingRecon.Api.Payers;

/// <summary>
/// Bringing in the patients and guarantors of the system before (ADR 0053; BR-101, BR-104), for the TenantAdmin. One
/// action: it answers what it did, or with a dry run what it would do, and every row it rejects with its file, line and
/// reason. One rejected row writes nothing; a patient the hospital has already is matched, never merged or changed, so
/// the same files can be sent again.
/// </summary>
[Route(ApiRoutes.V1 + "/imports/parties")]
public sealed class PartyImportsController : ApiControllerBase
{
    /// <summary>Checks the files and, unless it is a dry run or a row is rejected, writes their patients, payers and guarantors.</summary>
    [Authorize(Policy = Policies.ManageTenant)]
    [HttpPost]
    [ProducesResponseType<PartyImportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ImportAsync(
        ImportPartiesRequest request,
        [FromServices] ICommandHandler<ImportPartiesCommand, PartyImportDto> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await handler.HandleAsync(request.ToCommand(), ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}
