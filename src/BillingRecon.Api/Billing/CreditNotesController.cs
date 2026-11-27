using BillingRecon.Api.Common;
using BillingRecon.Api.Common.Authorization;
using BillingRecon.Api.Common.Idempotency;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BillingRecon.Api.Billing;

/// <summary>
/// The tenant's credit notes (US-24; BR-209 to BR-214): draft against a posted bill, correct, void, hand to a second
/// person, approve (which posts) or reject, and apply the credit that is left to other bills of the payer. Every
/// response that returns one credit note carries its version as the ETag; every change needs that version in
/// If-Match (BR-804). Another tenant's credit note or bill answers 404, exactly like an id nobody has.
/// </summary>
[Route(ApiRoutes.V1 + "/credit-notes")]
public sealed class CreditNotesController : ApiControllerBase
{
    private const string GetCreditNoteRoute = "GetCreditNote";

    /// <summary>
    /// Creates a draft credit note against a posted bill, with any lines sent. 404 when the bill does not exist; 422
    /// when it is not posted, the date is not allowed (BR-205, BR-214) or a line breaks BR-211.
    /// </summary>
    [Authorize(Policy = Policies.DraftCreditNotesAndAdjustments)]
    [HttpPost]
    [Idempotent]
    [ProducesResponseType<CreditNoteDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateAsync(
        CreateCreditNoteRequest request,
        [FromServices] ICommandHandler<CreateCreditNoteDraftCommand, CreditNoteDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(request.ToCommand(), ct);

        return result.IsSuccess
            ? CreatedWithVersion(GetCreditNoteRoute, result.Value.Id, result.Value, result.Value.Version)
            : Problem(result.Error);
    }

    /// <summary>Reads one credit note with its lines and the applications of its credit.</summary>
    [Authorize(Policy = Policies.ReadCreditNotes)]
    [HttpGet("{id:guid}", Name = GetCreditNoteRoute)]
    [ProducesResponseType<CreditNoteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(
        Guid id,
        [FromServices] IQueryHandler<GetCreditNoteQuery, CreditNoteDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GetCreditNoteQuery(new CreditNoteId(id)), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>Lists the credit notes one page at a time, the newest date first, without their lines.</summary>
    [Authorize(Policy = Policies.ReadCreditNotes)]
    [HttpGet]
    [ProducesResponseType<PageOf<CreditNoteSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] ListCreditNotesRequest request,
        [FromServices] IQueryHandler<ListCreditNotesQuery, PageOf<CreditNoteSummaryDto>> handler,
        CancellationToken ct)
    {
        var query = request.ToQuery();
        if (query.IsFailure)
        {
            return Problem(query.Error);
        }

        var result = await handler.HandleAsync(query.Value, ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Replaces the date and the reason of a draft. Needs If-Match.</summary>
    [Authorize(Policy = Policies.DraftCreditNotesAndAdjustments)]
    [HttpPatch("{id:guid}")]
    [ProducesResponseType<CreditNoteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> UpdateHeaderAsync(
        Guid id,
        UpdateCreditNoteHeaderRequest request,
        [FromServices] ICommandHandler<UpdateCreditNoteHeaderCommand, CreditNoteDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(request.ToCommand(new CreditNoteId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>Replaces every line of a draft with the lines sent and recomputes the totals. Needs If-Match.</summary>
    [Authorize(Policy = Policies.DraftCreditNotesAndAdjustments)]
    [HttpPut("{id:guid}/lines")]
    [ProducesResponseType<CreditNoteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> ReplaceLinesAsync(
        Guid id,
        ReplaceCreditNoteLinesRequest request,
        [FromServices] ICommandHandler<ReplaceCreditNoteLinesCommand, CreditNoteDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(request.ToCommand(new CreditNoteId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>Voids a draft, with a reason. The credit note keeps its content and can no longer change. Needs If-Match.</summary>
    [Authorize(Policy = Policies.DraftCreditNotesAndAdjustments)]
    [HttpPost("{id:guid}/void")]
    [Idempotent]
    [ProducesResponseType<CreditNoteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> VoidAsync(
        Guid id,
        VoidCreditNoteRequest request,
        [FromServices] ICommandHandler<VoidCreditNoteCommand, CreditNoteDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(request.ToCommand(new CreditNoteId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>
    /// The maker asks for the draft to be posted (US-24; BR-212, BR-604). Every credit note needs a second person, so
    /// this never posts: it answers 202 with the credit note in <c>PendingApproval</c>, without a number and without
    /// any ledger effect. 422 when the credit note has no line or comes to nothing, its date is no longer allowed, or
    /// the credits of the bill or of one of its lines would exceed it (BR-210, BR-211). Needs If-Match and an
    /// Idempotency-Key; no body.
    /// </summary>
    [Authorize(Policy = Policies.DraftCreditNotesAndAdjustments)]
    [HttpPost("{id:guid}/post")]
    [Idempotent]
    [ProducesResponseType<CreditNoteDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> PostAsync(
        Guid id,
        [FromServices] ICommandHandler<PostCreditNoteCommand, CreditNoteDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(new PostCreditNoteCommand(new CreditNoteId(id), version), ct);
        if (result.IsFailure)
        {
            return Problem(result.Error);
        }

        // Accepted and not done yet: the checker's approval is the posting (BR-606).
        var answer = OkWithVersion(result.Value, result.Value.Version);
        answer.StatusCode = StatusCodes.Status202Accepted;

        return answer;
    }

    /// <summary>
    /// A second person approves a credit note that waits, and that posts it (US-24; BR-212, BR-603, BR-606): the
    /// number, the ledger entries, the credit applied to its bill as far as the bill is owed, and the audit records,
    /// in one transaction. The maker cannot approve their own credit note: 403. Needs If-Match and an
    /// Idempotency-Key; the body, an optional comment, may be left out.
    /// </summary>
    [Authorize(Policy = Policies.Decide)]
    [HttpPost("{id:guid}/approve")]
    [Idempotent]
    [ProducesResponseType<CreditNoteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> ApproveAsync(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ApproveCreditNoteRequest? request,
        [FromServices] ICommandHandler<ApproveCreditNoteCommand, CreditNoteDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync((request ?? new ApproveCreditNoteRequest()).ToCommand(new CreditNoteId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>
    /// A second person rejects a credit note that waits, saying why (BR-605): it is a draft again and can be corrected
    /// and submitted once more. Needs If-Match and an Idempotency-Key.
    /// </summary>
    [Authorize(Policy = Policies.Decide)]
    [HttpPost("{id:guid}/reject")]
    [Idempotent]
    [ProducesResponseType<CreditNoteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> RejectAsync(
        Guid id,
        RejectCreditNoteRequest request,
        [FromServices] ICommandHandler<RejectCreditNoteCommand, CreditNoteDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(request.ToCommand(new CreditNoteId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>
    /// Applies available credit of a posted credit note to one or more posted bills of its payer, all of them or none
    /// (BR-213). Each bill's balance due and status follow (BR-208). 422 when an amount is more than the credit note
    /// has available or the bill is owed, the bill is not posted, or it is owed by another payer. Needs If-Match,
    /// with the version of the credit note, and an Idempotency-Key.
    /// </summary>
    [Authorize(Policy = Policies.DraftCreditNotesAndAdjustments)]
    [HttpPost("{id:guid}/applications")]
    [Idempotent]
    [ProducesResponseType<CreditNoteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> ApplyAsync(
        Guid id,
        ApplyCreditNoteRequest request,
        [FromServices] ICommandHandler<ApplyCreditNoteCommand, CreditNoteDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(request.ToCommand(new CreditNoteId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }
}
