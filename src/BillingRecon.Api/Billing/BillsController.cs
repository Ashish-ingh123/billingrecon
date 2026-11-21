using BillingRecon.Api.Common;
using BillingRecon.Api.Common.Authorization;
using BillingRecon.Api.Common.Idempotency;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

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
    [Authorize(Policy = Policies.WriteBills)]
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
    [Authorize(Policy = Policies.ReadBills)]
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
    [Authorize(Policy = Policies.ReadBills)]
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
    [Authorize(Policy = Policies.WriteBills)]
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
    [Authorize(Policy = Policies.WriteBills)]
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
    [Authorize(Policy = Policies.WriteBills)]
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

    /// <summary>
    /// Posts a draft (US-21, BR-206): the bill gets the next number of the fiscal year of its bill date, the payer owes
    /// its total, and it can no longer be edited or voided. All of it, with the ledger entry and the audit record, is
    /// one transaction (BR-007). A bill at or above the approval threshold of the hospital is not posted: it answers
    /// 202 and waits for a second person (BR-207). Needs If-Match and an Idempotency-Key; no body.
    /// </summary>
    [Authorize(Policy = Policies.WriteBills)]
    [HttpPost("{id:guid}/post")]
    [Idempotent]
    [ProducesResponseType<BillDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<BillDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> PostAsync(
        Guid id,
        [FromServices] ICommandHandler<PostBillCommand, BillDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(new PostBillCommand(new BillId(id), version), ct);
        if (result.IsFailure)
        {
            return Problem(result.Error);
        }

        // Posted: 200. Handed to a second person because of the threshold: 202, accepted and not done yet (BR-207).
        var answer = OkWithVersion(result.Value, result.Value.Version);
        if (result.Value.Status == BillStatus.PendingApproval)
        {
            answer.StatusCode = StatusCodes.Status202Accepted;
        }

        return answer;
    }

    /// <summary>
    /// A second person approves a bill that waits for approval, and that posts it (US-22; BR-603, BR-606): the number,
    /// the ledger entry and the audit records, in one transaction. The maker cannot approve their own bill: 403.
    /// Needs If-Match and an Idempotency-Key; the body, an optional comment, may be left out.
    /// </summary>
    [Authorize(Policy = Policies.Decide)]
    [HttpPost("{id:guid}/approve")]
    [Idempotent]
    [ProducesResponseType<BillDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> ApproveAsync(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ApproveBillRequest? request,
        [FromServices] ICommandHandler<ApproveBillPostingCommand, BillDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync((request ?? new ApproveBillRequest()).ToCommand(new BillId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>
    /// A second person rejects a bill that waits for approval, saying why (BR-605): the bill is a draft again and can
    /// be corrected and posted once more. Needs If-Match and an Idempotency-Key.
    /// </summary>
    [Authorize(Policy = Policies.Decide)]
    [HttpPost("{id:guid}/reject")]
    [Idempotent]
    [ProducesResponseType<BillDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> RejectAsync(
        Guid id,
        RejectBillRequest request,
        [FromServices] ICommandHandler<RejectBillPostingCommand, BillDto> handler,
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
