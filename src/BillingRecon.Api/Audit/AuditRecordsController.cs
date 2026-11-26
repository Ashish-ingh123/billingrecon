using BillingRecon.Api.Common;
using BillingRecon.Api.Common.Authorization;
using BillingRecon.Application.Audit;
using BillingRecon.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BillingRecon.Api.Audit;

/// <summary>
/// The audit trail (US-43; BR-701 to BR-703): read-only. Every change of a patient, payer, service item or bill left
/// one record here, written in the transaction of the change, with who, when, the correlation id of the request and
/// the record before and after. Nothing here can be created, changed or deleted through the API. Another tenant's
/// record, like an id nobody has, simply has an empty trail (docs/api.md). Since 2026-11-26 (review finding R7) a trail
/// is read by whoever may read its record, and by no one else: <see cref="AuditTrailPolicies"/> names the read policy of
/// each kind of audited record, and a kind it does not name is refused.
/// </summary>
[Route(ApiRoutes.V1 + "/audit-records")]
public sealed class AuditRecordsController(IAuthorizationService authorization) : ApiControllerBase
{
    /// <summary>
    /// Lists the trail of one record, oldest first: <c>entityType</c> and <c>entityId</c> are required. 403 for the
    /// trail of a kind of record the caller's role may not read, and for a kind that is not audited.
    /// </summary>
    [Authorize(Policy = Policies.ReadAudit)]
    [HttpGet]
    [ProducesResponseType<PageOf<AuditRecordDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] ListAuditRecordsRequest request,
        [FromServices] IQueryHandler<ListAuditRecordsQuery, PageOf<AuditRecordDto>> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Compared as the database compares the kind: without regard to case or to the spaces around it.
        var entityType = request.EntityType?.Trim();
        // A request without a kind is the validator's to refuse (400). One with a kind is answered only to who may
        // read that kind of record; a kind nobody named has no reader.
        if (!string.IsNullOrEmpty(entityType)
            && (!AuditTrailPolicies.ByEntityType.TryGetValue(entityType, out var policy)
                || !(await authorization.AuthorizeAsync(User, policy)).Succeeded))
        {
            return Forbid();
        }

        var result = await handler.HandleAsync(request.ToQuery(), ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}
