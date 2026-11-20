using BillingRecon.Api.Common;
using BillingRecon.Api.Common.Idempotency;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace BillingRecon.Api.Billing;

/// <summary>
/// The tenant's bills while they are drafts (US-20, US-23; BR-201 to BR-205): create, read, list, correct the header,
/// replace the lines and void. A draft has no number and no ledger effect, and is never deleted. Every response that
/// returns one bill carries its version as the ETag; every change needs that version in If-Match (BR-804), and the
/// version changes with the bill and with any of its lines. Another tenant's bill, patient, payer or service item
/// answers 404, exactly like an id nobody has (docs/api.md).
/// </summary>
[Route(ApiRoutes.V1 + "/bills")]
public sealed class BillsController : ApiControllerBase
{
    private const string GetBillRoute = "GetBill";

    /// <summary>
    /// Creates a draft bill for a patient, owed by a payer, with any lines sent. 404 when the patient, the payer or a
    /// service item does not exist; 422 when one of them is inactive, a date is not allowed (BR-205) or a line breaks
    /// BR-201.
    /// </summary>
    [HttpPost]
    [Idempotent]
    [ProducesResponseType<BillDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateAsync(
        CreateBillRequest request,
        [FromServices] ICommandHandler<CreateBillDraftCommand, BillDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(request.ToCommand(), ct);

        return result.IsSuccess
            ? CreatedWithVersion(GetBillRoute, result.Value.Id, result.Value, result.Value.Version)
            : Problem(result.Error);
    }

    /// <summary>Reads one bill with its lines.</summary>
    [HttpGet("{id:guid}", Name = GetBillRoute)]
    [ProducesResponseType<BillDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(
        Guid id,
        [FromServices] IQueryHandler<GetBillQuery, BillDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GetBillQuery(new BillId(id)), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>Lists the bills one page at a time, the newest bill date first, without their lines.</summary>
    [HttpGet]
    [ProducesResponseType<PageOf<BillSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] ListBillsRequest request,
        [FromServices] IQueryHandler<ListBillsQuery, PageOf<BillSummaryDto>> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(request.ToQuery(), ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Replaces the dates and the two texts of a draft; a text left out is cleared. Needs If-Match.</summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType<BillDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> UpdateHeaderAsync(
        Guid id,
        UpdateBillHeaderRequest request,
        [FromServices] ICommandHandler<UpdateBillHeaderCommand, BillDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(request.ToCommand(new BillId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>
    /// Replaces every line of a draft with the lines sent and recomputes the totals (BR-202, BR-203). Needs If-Match,
    /// with the version of the bill.
    /// </summary>
    [HttpPut("{id:guid}/lines")]
    [ProducesResponseType<BillDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> ReplaceLinesAsync(
        Guid id,
        ReplaceBillLinesRequest request,
        [FromServices] ICommandHandler<ReplaceBillLinesCommand, BillDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(request.ToCommand(new BillId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>Voids a draft, with a reason (US-23). The bill keeps its content and can no longer change. Needs If-Match.</summary>
    [HttpPost("{id:guid}/void")]
    [Idempotent]
    [ProducesResponseType<BillDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> VoidAsync(
        Guid id,
        VoidBillRequest request,
        [FromServices] ICommandHandler<VoidBillCommand, BillDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(request.ToCommand(new BillId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }
}
