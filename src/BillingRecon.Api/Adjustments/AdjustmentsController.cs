using BillingRecon.Api.Common;
using BillingRecon.Api.Common.Authorization;
using BillingRecon.Api.Common.Idempotency;
using BillingRecon.Application.Adjustments;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BillingRecon.Api.Adjustments;

/// <summary>
/// The tenant's adjustments (BR-501 to BR-503): a discount, a write-off or a rounding difference that lowers a posted
/// bill. Draft, correct, void, hand to a second person, approve (which posts) or reject. Every response that returns
/// one adjustment carries its version as the ETag; every change needs that version in If-Match (BR-804). Another
/// tenant's adjustment or bill answers 404, exactly like an id nobody has.
/// </summary>
[Route(ApiRoutes.V1 + "/adjustments")]
public sealed class AdjustmentsController : ApiControllerBase
{
    private const string GetAdjustmentRoute = "GetAdjustment";

    /// <summary>
    /// Creates a draft adjustment against a posted bill. 404 when the bill does not exist; 422 when it is not posted,
    /// the date is not allowed, or a rounding is one rupee or more (BR-501).
    /// </summary>
    [Authorize(Policy = Policies.DraftCreditNotesAndAdjustments)]
    [HttpPost]
    [Idempotent]
    [ProducesResponseType<AdjustmentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateAsync(
        CreateAdjustmentRequest request,
        [FromServices] ICommandHandler<CreateAdjustmentDraftCommand, AdjustmentDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(request.ToCommand(), ct);

        return result.IsSuccess
            ? CreatedWithVersion(GetAdjustmentRoute, result.Value.Id, result.Value, result.Value.Version)
            : Problem(result.Error);
    }

    /// <summary>Reads one adjustment.</summary>
    [Authorize(Policy = Policies.ReadAdjustments)]
    [HttpGet("{id:guid}", Name = GetAdjustmentRoute)]
    [ProducesResponseType<AdjustmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(
        Guid id,
        [FromServices] IQueryHandler<GetAdjustmentQuery, AdjustmentDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GetAdjustmentQuery(new AdjustmentId(id)), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>Lists the adjustments one page at a time, the newest date first.</summary>
    [Authorize(Policy = Policies.ReadAdjustments)]
    [HttpGet]
    [ProducesResponseType<PageOf<AdjustmentDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] ListAdjustmentsRequest request,
        [FromServices] IQueryHandler<ListAdjustmentsQuery, PageOf<AdjustmentDto>> handler,
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

    /// <summary>Replaces the date, the kind, the amount and the reason of a draft. Needs If-Match.</summary>
    [Authorize(Policy = Policies.DraftCreditNotesAndAdjustments)]
    [HttpPut("{id:guid}")]
    [ProducesResponseType<AdjustmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        UpdateAdjustmentRequest request,
        [FromServices] ICommandHandler<UpdateAdjustmentCommand, AdjustmentDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(request.ToCommand(new AdjustmentId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>Voids a draft, with a reason. The adjustment keeps its content and can no longer change. Needs If-Match.</summary>
    [Authorize(Policy = Policies.DraftCreditNotesAndAdjustments)]
    [HttpPost("{id:guid}/void")]
    [Idempotent]
    [ProducesResponseType<AdjustmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> VoidAsync(
        Guid id,
        VoidAdjustmentRequest request,
        [FromServices] ICommandHandler<VoidAdjustmentCommand, AdjustmentDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(request.ToCommand(new AdjustmentId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>
    /// The maker asks for the draft to be posted (BR-502, BR-604). Every adjustment needs a second person, a rounding
    /// of a few paise included, so this never posts: it answers 202 with the adjustment in <c>PendingApproval</c>,
    /// without a number and without any ledger effect. 422 when its date is no longer allowed or its amount is more
    /// than the bill is owed (BR-501). Needs If-Match and an Idempotency-Key; no body.
    /// </summary>
    [Authorize(Policy = Policies.DraftCreditNotesAndAdjustments)]
    [HttpPost("{id:guid}/post")]
    [Idempotent]
    [ProducesResponseType<AdjustmentDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> PostAsync(
        Guid id,
        [FromServices] ICommandHandler<PostAdjustmentCommand, AdjustmentDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(new PostAdjustmentCommand(new AdjustmentId(id), version), ct);
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
    /// A second person approves an adjustment that waits, and that posts it (BR-502, BR-603, BR-606): the number, the
    /// bill lowered by the amount, the ledger entry and the audit records, in one transaction. The maker cannot
    /// approve their own adjustment: 403. Needs If-Match and an Idempotency-Key; the body, an optional comment, may be
    /// left out.
    /// </summary>
    [Authorize(Policy = Policies.Decide)]
    [HttpPost("{id:guid}/approve")]
    [Idempotent]
    [ProducesResponseType<AdjustmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> ApproveAsync(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ApproveAdjustmentRequest? request,
        [FromServices] ICommandHandler<ApproveAdjustmentCommand, AdjustmentDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync((request ?? new ApproveAdjustmentRequest()).ToCommand(new AdjustmentId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>
    /// A second person rejects an adjustment that waits, saying why (BR-605): it is a draft again and can be corrected
    /// and submitted once more. Needs If-Match and an Idempotency-Key.
    /// </summary>
    [Authorize(Policy = Policies.Decide)]
    [HttpPost("{id:guid}/reject")]
    [Idempotent]
    [ProducesResponseType<AdjustmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> RejectAsync(
        Guid id,
        RejectAdjustmentRequest request,
        [FromServices] ICommandHandler<RejectAdjustmentCommand, AdjustmentDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(request.ToCommand(new AdjustmentId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }
}
