using BillingRecon.Api.Common;
using BillingRecon.Application.Audit;
using BillingRecon.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace BillingRecon.Api.Audit;

/// <summary>
/// The audit trail (US-43; BR-701 to BR-703): read-only. Every change of a patient, payer, service item or bill left
/// one record here, written in the transaction of the change, with who, when, the correlation id of the request and
/// the record before and after. Nothing here can be created, changed or deleted through the API. Another tenant's
/// record, like an id nobody has, simply has an empty trail (docs/api.md).
/// </summary>
[Route(ApiRoutes.V1 + "/audit-records")]
public sealed class AuditRecordsController : ApiControllerBase
{
    /// <summary>Lists the trail of one record, oldest first: <c>entityType</c> and <c>entityId</c> are required.</summary>
    [HttpGet]
    [ProducesResponseType<PageOf<AuditRecordDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] ListAuditRecordsRequest request,
        [FromServices] IQueryHandler<ListAuditRecordsQuery, PageOf<AuditRecordDto>> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(request.ToQuery(), ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}
