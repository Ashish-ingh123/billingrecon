using BillingRecon.Api.Common;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace BillingRecon.Api.Payers;

/// <summary>
/// The tenant's payers (US-11, US-12; BR-103 to BR-105): create, read, list, correct, deactivate and reactivate. A
/// payer is never deleted. Every response that returns one payer carries its version as the ETag; every change of an
/// existing payer needs that version in If-Match (BR-804). Another tenant's payer, or a patient of another tenant named
/// in a request, answers 404, exactly like an id nobody has (docs/api.md).
/// </summary>
[Route(ApiRoutes.V1 + "/payers")]
public sealed class PayersController : ApiControllerBase
{
    private const string GetPayerRoute = "GetPayer";

    /// <summary>
    /// Creates a self-pay payer, for a patient or as a guarantor. 422 <c>payer_kind_not_supported</c> for Insurer and
    /// Corporate (BR-103); 409 <c>patient_already_linked</c> when the patient has a payer (BR-104).
    /// </summary>
    [HttpPost]
    [ProducesResponseType<PayerDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateAsync(
        CreatePayerRequest request,
        [FromServices] ICommandHandler<CreatePayerCommand, PayerDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(request.ToCommand(), ct);

        return result.IsSuccess
            ? CreatedWithVersion(GetPayerRoute, result.Value.Id, result.Value, result.Value.Version)
            : Problem(result.Error);
    }

    /// <summary>Reads one payer.</summary>
    [HttpGet("{id:guid}", Name = GetPayerRoute)]
    [ProducesResponseType<PayerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(
        Guid id,
        [FromServices] IQueryHandler<GetPayerQuery, PayerDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GetPayerQuery(new PayerId(id)), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>Lists the payers one page at a time, by name; <c>search</c> matches part of a name.</summary>
    [HttpGet]
    [ProducesResponseType<PageOf<PayerDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] ListPayersRequest request,
        [FromServices] IQueryHandler<ListPayersQuery, PageOf<PayerDto>> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(request.ToQuery(), ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Replaces the name and contact details of a payer; a contact detail left out is cleared. Needs If-Match.</summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType<PayerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        UpdatePayerRequest request,
        [FromServices] ICommandHandler<UpdatePayerCommand, PayerDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(request.ToCommand(new PayerId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>Deactivates a payer (BR-105): no new bill, payment or refund for it. Needs If-Match.</summary>
    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType<PayerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> DeactivateAsync(
        Guid id,
        [FromServices] ICommandHandler<DeactivatePayerCommand, PayerDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(new DeactivatePayerCommand(new PayerId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>Makes an inactive payer active again. Needs If-Match.</summary>
    [HttpPost("{id:guid}/reactivate")]
    [ProducesResponseType<PayerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> ReactivateAsync(
        Guid id,
        [FromServices] ICommandHandler<ReactivatePayerCommand, PayerDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(new ReactivatePayerCommand(new PayerId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }
}
