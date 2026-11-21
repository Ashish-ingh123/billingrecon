using BillingRecon.Api.Common;
using BillingRecon.Api.Common.Authorization;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BillingRecon.Api.Billing;

/// <summary>
/// The tenant's service catalogue (docs/domain-model.md, section 4.3): create, read, list, correct, deactivate and
/// reactivate. An item is never deleted, and its code never changes. Every response that returns one item carries its
/// version as the ETag; every change of an existing item needs that version in If-Match (BR-804). Another tenant's
/// item answers 404, exactly like an id nobody has (docs/api.md).
/// </summary>
[Route(ApiRoutes.V1 + "/service-items")]
public sealed class ServiceItemsController : ApiControllerBase
{
    private const string GetServiceItemRoute = "GetServiceItem";

    /// <summary>Adds an item to the catalogue. 409 <c>service_item_code_duplicate</c> when the tenant already has the code.</summary>
    [Authorize(Policy = Policies.WriteReferenceData)]
    [HttpPost]
    [ProducesResponseType<ServiceItemDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        CreateServiceItemRequest request,
        [FromServices] ICommandHandler<CreateServiceItemCommand, ServiceItemDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(request.ToCommand(), ct);

        return result.IsSuccess
            ? CreatedWithVersion(GetServiceItemRoute, result.Value.Id, result.Value, result.Value.Version)
            : Problem(result.Error);
    }

    /// <summary>Reads one service item.</summary>
    [Authorize(Policy = Policies.ReadReferenceData)]
    [HttpGet("{id:guid}", Name = GetServiceItemRoute)]
    [ProducesResponseType<ServiceItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(
        Guid id,
        [FromServices] IQueryHandler<GetServiceItemQuery, ServiceItemDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GetServiceItemQuery(new ServiceItemId(id)), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>Lists the catalogue one page at a time, by code; <c>search</c> matches part of a code or of a name.</summary>
    [Authorize(Policy = Policies.ReadReferenceData)]
    [HttpGet]
    [ProducesResponseType<PageOf<ServiceItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] ListServiceItemsRequest request,
        [FromServices] IQueryHandler<ListServiceItemsQuery, PageOf<ServiceItemDto>> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(request.ToQuery(), ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>
    /// Replaces the name, category, price and tax rate of an item; a category left out is cleared. Bill lines already
    /// priced from the item keep their own price and tax rate. Needs If-Match.
    /// </summary>
    [Authorize(Policy = Policies.WriteReferenceData)]
    [HttpPatch("{id:guid}")]
    [ProducesResponseType<ServiceItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        UpdateServiceItemRequest request,
        [FromServices] ICommandHandler<UpdateServiceItemCommand, ServiceItemDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(request.ToCommand(new ServiceItemId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>Deactivates an item: it is no longer offered for new bill lines. Needs If-Match.</summary>
    [Authorize(Policy = Policies.WriteReferenceData)]
    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType<ServiceItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> DeactivateAsync(
        Guid id,
        [FromServices] ICommandHandler<DeactivateServiceItemCommand, ServiceItemDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(new DeactivateServiceItemCommand(new ServiceItemId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>Makes an inactive item active again. Needs If-Match.</summary>
    [Authorize(Policy = Policies.WriteReferenceData)]
    [HttpPost("{id:guid}/reactivate")]
    [ProducesResponseType<ServiceItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> ReactivateAsync(
        Guid id,
        [FromServices] ICommandHandler<ReactivateServiceItemCommand, ServiceItemDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(new ReactivateServiceItemCommand(new ServiceItemId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }
}
