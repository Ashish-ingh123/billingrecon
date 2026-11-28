using BillingRecon.Api.Common;
using BillingRecon.Api.Common.Authorization;
using BillingRecon.Application.Common;
using BillingRecon.Application.Reconciliation;
using BillingRecon.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BillingRecon.Api.Reconciliation;

/// <summary>
/// The hospital's settlement channels, its bank and gateway accounts (BR-1001, BR-1002; ADR 0065): set up, read, list,
/// change, deactivate and reactivate. A TenantAdmin sets them up; whoever reads the reconciliation reads them. A channel
/// is never deleted. Every response with one channel carries its version as the ETag, and a change needs it in
/// If-Match (BR-804). Another tenant's channel answers 404, exactly like an id nobody has (BR-1024).
/// </summary>
[Route(ApiRoutes.V1 + "/settlement-channels")]
public sealed class SettlementChannelsController : ApiControllerBase
{
    private const string GetSettlementChannelRoute = "GetSettlementChannel";

    /// <summary>Sets up a channel. 409 <c>settlement_channel_name_duplicate</c> when the hospital already has the name.</summary>
    [Authorize(Policy = Policies.ManageTenant)]
    [HttpPost]
    [ProducesResponseType<SettlementChannelDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateAsync(
        CreateSettlementChannelRequest request,
        [FromServices] ICommandHandler<CreateSettlementChannelCommand, SettlementChannelDto> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await handler.HandleAsync(request.ToCommand(), ct);
        return result.IsSuccess
            ? CreatedWithVersion(GetSettlementChannelRoute, result.Value.Id, result.Value, result.Value.Version)
            : Problem(result.Error);
    }

    /// <summary>Reads one channel.</summary>
    [Authorize(Policy = Policies.ReadReconciliation)]
    [HttpGet("{id:guid}", Name = GetSettlementChannelRoute)]
    [ProducesResponseType<SettlementChannelDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(
        Guid id,
        [FromServices] IQueryHandler<GetSettlementChannelQuery, SettlementChannelDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GetSettlementChannelQuery(new SettlementChannelId(id)), ct);
        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>Lists the channels one page at a time, by name.</summary>
    [Authorize(Policy = Policies.ReadReconciliation)]
    [HttpGet]
    [ProducesResponseType<PageOf<SettlementChannelDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] ListSettlementChannelsRequest request,
        [FromServices] IQueryHandler<ListSettlementChannelsQuery, PageOf<SettlementChannelDto>> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await handler.HandleAsync(request.ToQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>
    /// Changes the name, the label, the window, and the mapping of a bank or the bank of a gateway. Lines imported before
    /// keep what was read. Needs If-Match.
    /// </summary>
    [Authorize(Policy = Policies.ManageTenant)]
    [HttpPatch("{id:guid}")]
    [ProducesResponseType<SettlementChannelDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        UpdateSettlementChannelRequest request,
        [FromServices] ICommandHandler<UpdateSettlementChannelCommand, SettlementChannelDto> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(request.ToCommand(new SettlementChannelId(id), version), ct);
        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>Deactivates a channel: it takes no import. Needs If-Match.</summary>
    [Authorize(Policy = Policies.ManageTenant)]
    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType<SettlementChannelDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> DeactivateAsync(
        Guid id,
        [FromServices] ICommandHandler<DeactivateSettlementChannelCommand, SettlementChannelDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(new DeactivateSettlementChannelCommand(new SettlementChannelId(id), version), ct);
        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>Makes an inactive channel active again. Needs If-Match.</summary>
    [Authorize(Policy = Policies.ManageTenant)]
    [HttpPost("{id:guid}/reactivate")]
    [ProducesResponseType<SettlementChannelDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> ReactivateAsync(
        Guid id,
        [FromServices] ICommandHandler<ReactivateSettlementChannelCommand, SettlementChannelDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(new ReactivateSettlementChannelCommand(new SettlementChannelId(id), version), ct);
        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }
}
