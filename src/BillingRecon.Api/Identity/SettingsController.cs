using BillingRecon.Api.Common;
using BillingRecon.Api.Common.Authorization;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BillingRecon.Api.Identity;

/// <summary>
/// The settings of the tenant (BR-607, US-03; ADR 0033): the approval threshold, the back-date window and the default
/// due days, and the default prefix of each document type. A TenantAdmin's alone. A change is audited and applies from
/// the next request; the prefixes change only through a prefix change, which a second person decides.
/// </summary>
[Route(ApiRoutes.V1 + "/settings")]
public sealed class SettingsController : ApiControllerBase
{
    /// <summary>Reads the settings, with the tenant's version as the ETag.</summary>
    [Authorize(Policy = Policies.ManageTenant)]
    [HttpGet]
    [ProducesResponseType<TenantSettingsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync(
        [FromServices] IQueryHandler<GetTenantSettingsQuery, TenantSettingsDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GetTenantSettingsQuery(), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>Changes the threshold, the back-date window and the default due days. Needs If-Match.</summary>
    [Authorize(Policy = Policies.ManageTenant)]
    [HttpPatch]
    [ProducesResponseType<TenantSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> UpdateAsync(
        UpdateSettingsRequest request,
        [FromServices] ICommandHandler<UpdateTenantSettingsCommand, TenantSettingsDto> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(request.ToCommand(version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }
    /// <summary>
    /// What the hospital prints at the head and foot of its documents (ADR 0054), for anyone who is signed in: whoever
    /// may print a document needs it, and it says nothing a printed page does not.
    /// </summary>
    [Authorize(Policy = Policies.SignedIn)]
    [HttpGet("letterhead")]
    [ProducesResponseType<LetterheadDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLetterheadAsync(
        [FromServices] IQueryHandler<GetLetterheadQuery, LetterheadDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GetLetterheadQuery(), ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>
    /// Sets the letterhead (ADR 0054): every part is sent, one left out is cleared. Needs If-Match with the version of the
    /// settings, which the letterhead shares; answers the settings with their new version.
    /// </summary>
    [Authorize(Policy = Policies.ManageTenant)]
    [HttpPut("letterhead")]
    [ProducesResponseType<TenantSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> UpdateLetterheadAsync(
        UpdateLetterheadRequest request,
        [FromServices] ICommandHandler<UpdateLetterheadCommand, TenantSettingsDto> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(request.ToCommand(version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }
}
