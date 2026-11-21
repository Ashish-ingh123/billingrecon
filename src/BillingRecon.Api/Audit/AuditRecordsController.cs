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
/// record, like an id nobody has, simply has an empty trail (docs/api.md). Every role reads it, except the trails of a
/// user, of the tenant, of its prefix changes and of its number series, which are the TenantAdmin's alone, as the users
/// and the settings are (ADR 0032 point 6, ADR 0033 point 9).
/// </summary>
[Route(ApiRoutes.V1 + "/audit-records")]
public sealed class AuditRecordsController(IAuthorizationService authorization) : ApiControllerBase
{
    // The kinds of record whose trail only a TenantAdmin reads, by the name the audit gives them (the class name).
    private static readonly string[] TenantAdminTrails =
    [
        typeof(Domain.Identity.User).Name,
        typeof(Domain.Identity.Tenant).Name,
        typeof(Domain.Identity.PrefixChange).Name,
        typeof(Domain.Common.NumberSeries).Name,
    ];

    /// <summary>
    /// Lists the trail of one record, oldest first: <c>entityType</c> and <c>entityId</c> are required. 403 for the
    /// trail of a user, unless the caller is a TenantAdmin.
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
        if (TenantAdminTrails.Contains(entityType, StringComparer.OrdinalIgnoreCase)
            && !(await authorization.AuthorizeAsync(User, Policies.ManageTenant)).Succeeded)
        {
            return Forbid();
        }

        var result = await handler.HandleAsync(request.ToQuery(), ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}
