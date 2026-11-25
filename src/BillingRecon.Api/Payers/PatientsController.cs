using BillingRecon.Api.Common;
using BillingRecon.Api.Common.Authorization;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BillingRecon.Api.Payers;

/// <summary>
/// The tenant's patients (US-10, US-12; BR-101, BR-102): register, read, list, correct, deactivate and reactivate. A
/// patient is never deleted. Every response that returns one patient carries its version as the ETag; every change of
/// an existing patient needs that version in If-Match (BR-804). Another tenant's patient answers 404, exactly like an
/// id nobody has (docs/api.md).
/// </summary>
[Route(ApiRoutes.V1 + "/patients")]
public sealed class PatientsController : ApiControllerBase
{
    private const string GetPatientRoute = "GetPatient";

    /// <summary>Registers a patient. 409 <c>patient_mrn_duplicate</c> when the tenant already has the MRN.</summary>
    [Authorize(Policy = Policies.WriteReferenceData)]
    [HttpPost]
    [ProducesResponseType<PatientDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RegisterAsync(
        RegisterPatientRequest request,
        [FromServices] ICommandHandler<RegisterPatientCommand, PatientDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(request.ToCommand(), ct);

        return result.IsSuccess
            ? CreatedWithVersion(GetPatientRoute, result.Value.Id, result.Value, result.Value.Version)
            : Problem(result.Error);
    }

    /// <summary>Reads one patient.</summary>
    [Authorize(Policy = Policies.ReadReferenceData)]
    [HttpGet("{id:guid}", Name = GetPatientRoute)]
    [ProducesResponseType<PatientDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(
        Guid id,
        [FromServices] IQueryHandler<GetPatientQuery, PatientDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GetPatientQuery(new PatientId(id)), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>Lists the patients one page at a time, by name; <c>search</c> matches a part of an MRN or of a name.</summary>
    [Authorize(Policy = Policies.ReadReferenceData)]
    [HttpGet]
    [ProducesResponseType<PageOf<PatientDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] ListPatientsRequest request,
        [FromServices] IQueryHandler<ListPatientsQuery, PageOf<PatientDto>> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(request.ToQuery(), ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Replaces the demographics of a patient; an optional field left out is cleared. Needs If-Match.</summary>
    [Authorize(Policy = Policies.WriteReferenceData)]
    [HttpPatch("{id:guid}")]
    [ProducesResponseType<PatientDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        UpdatePatientRequest request,
        [FromServices] ICommandHandler<UpdatePatientCommand, PatientDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(request.ToCommand(new PatientId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>Deactivates a patient (BR-102): no new bill may be raised for them. Needs If-Match.</summary>
    [Authorize(Policy = Policies.WriteReferenceData)]
    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType<PatientDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> DeactivateAsync(
        Guid id,
        [FromServices] ICommandHandler<DeactivatePatientCommand, PatientDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(new DeactivatePatientCommand(new PatientId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>Makes an inactive patient active again. Needs If-Match.</summary>
    [Authorize(Policy = Policies.WriteReferenceData)]
    [HttpPost("{id:guid}/reactivate")]
    [ProducesResponseType<PatientDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> ReactivateAsync(
        Guid id,
        [FromServices] ICommandHandler<ReactivatePatientCommand, PatientDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(new ReactivatePatientCommand(new PatientId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }
}
