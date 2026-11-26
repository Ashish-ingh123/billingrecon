using BillingRecon.Api.Common;
using BillingRecon.Api.Common.Authorization;
using BillingRecon.Api.Common.Idempotency;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BillingRecon.Api.Payments;

/// <summary>
/// The tenant's refunds (US-34; BR-307 to BR-309): money paid back to a payer from what a payment has unapplied or
/// what a credit note has available. Draft, correct, void, hand to a second person, approve (which posts) or reject; a
/// posted refund is never reversed. Every response that returns one refund carries its version as the ETag; every
/// change needs that version in If-Match (BR-804). Another tenant's refund, payment or credit note answers 404,
/// exactly like an id nobody has.
/// </summary>
[Route(ApiRoutes.V1 + "/refunds")]
public sealed class RefundsController : ApiControllerBase
{
    private const string GetRefundRoute = "GetRefund";

    /// <summary>
    /// Creates a draft refund from a payment or a credit note; the refund takes its payer. 404 when the source does not
    /// exist; 409 when the payment waits for its reversal or was reversed; 422 when the credit note is not posted, the
    /// payer is inactive (BR-309), or the date is not allowed or before the source's date (BR-205, BR-307).
    /// </summary>
    [Authorize(Policy = Policies.DraftRefunds)]
    [HttpPost]
    [Idempotent]
    [ProducesResponseType<RefundDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateAsync(
        CreateRefundRequest request,
        [FromServices] ICommandHandler<CreateRefundDraftCommand, RefundDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(request.ToCommand(), ct);

        return result.IsSuccess
            ? CreatedWithVersion(GetRefundRoute, result.Value.Id, result.Value, result.Value.Version)
            : Problem(result.Error);
    }

    /// <summary>
    /// Who drafted the refund, by name, for a voucher whose print settings ask for it (ADR 0059); the name is null when
    /// no user of the hospital drafted it.
    /// </summary>
    [Authorize(Policy = Policies.ReadRefunds)]
    [HttpGet("{id:guid}/recorder")]
    [ProducesResponseType<DocumentRecorderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRecorderAsync(
        Guid id,
        [FromServices] IQueryHandler<GetRefundRecorderQuery, DocumentRecorderDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GetRefundRecorderQuery(new RefundId(id)), ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Reads one refund.</summary>
    [Authorize(Policy = Policies.ReadRefunds)]
    [HttpGet("{id:guid}", Name = GetRefundRoute)]
    [ProducesResponseType<RefundDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(
        Guid id,
        [FromServices] IQueryHandler<GetRefundQuery, RefundDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GetRefundQuery(new RefundId(id)), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>Lists the refunds one page at a time, the newest date first.</summary>
    [Authorize(Policy = Policies.ReadRefunds)]
    [HttpGet]
    [ProducesResponseType<PageOf<RefundDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] ListRefundsRequest request,
        [FromServices] IQueryHandler<ListRefundsQuery, PageOf<RefundDto>> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(request.ToQuery(), ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>Replaces the date, the amount, the method, the reference and the reason of a draft; the source stays. Needs If-Match.</summary>
    [Authorize(Policy = Policies.DraftRefunds)]
    [HttpPut("{id:guid}")]
    [ProducesResponseType<RefundDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        UpdateRefundRequest request,
        [FromServices] ICommandHandler<UpdateRefundCommand, RefundDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(request.ToCommand(new RefundId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>Voids a draft, with a reason. The refund keeps its content and can no longer change. Needs If-Match.</summary>
    [Authorize(Policy = Policies.DraftRefunds)]
    [HttpPost("{id:guid}/void")]
    [Idempotent]
    [ProducesResponseType<RefundDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> VoidAsync(
        Guid id,
        VoidRefundRequest request,
        [FromServices] ICommandHandler<VoidRefundCommand, RefundDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(request.ToCommand(new RefundId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>
    /// The maker asks for the draft to be posted (BR-604). Every refund needs a second person, so this never posts: it
    /// answers 202 with the refund in <c>PendingApproval</c>, without a number and without any ledger effect. 422 when
    /// its date is no longer allowed or its amount is more than the source has left (BR-307); 409 when the payment
    /// waits for its reversal or was reversed. Needs If-Match and an Idempotency-Key; no body.
    /// </summary>
    [Authorize(Policy = Policies.DraftRefunds)]
    [HttpPost("{id:guid}/post")]
    [Idempotent]
    [ProducesResponseType<RefundDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> PostAsync(
        Guid id,
        [FromServices] ICommandHandler<PostRefundCommand, RefundDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(new PostRefundCommand(new RefundId(id), version), ct);
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
    /// A second person approves a refund that waits, and that posts it (BR-307, BR-603, BR-606): the number, the
    /// source lowered by the amount, the ledger entry and the audit records, in one transaction. The maker cannot
    /// approve their own refund: 403. The cap and the payer are asked again (BR-307, BR-309). Needs If-Match and an
    /// Idempotency-Key; the body, an optional comment, may be left out.
    /// </summary>
    [Authorize(Policy = Policies.Decide)]
    [HttpPost("{id:guid}/approve")]
    [Idempotent]
    [ProducesResponseType<RefundDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> ApproveAsync(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ApproveRefundRequest? request,
        [FromServices] ICommandHandler<ApproveRefundCommand, RefundDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync((request ?? new ApproveRefundRequest()).ToCommand(new RefundId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>
    /// A second person rejects a refund that waits, saying why (BR-605): it is a draft again and can be corrected
    /// and submitted once more. Needs If-Match and an Idempotency-Key.
    /// </summary>
    [Authorize(Policy = Policies.Decide)]
    [HttpPost("{id:guid}/reject")]
    [Idempotent]
    [ProducesResponseType<RefundDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> RejectAsync(
        Guid id,
        RejectRefundRequest request,
        [FromServices] ICommandHandler<RejectRefundCommand, RefundDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(request.ToCommand(new RefundId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }
    /// <summary>
    /// Records that the refund is being printed (ADR 0054) and answers which print it is: the first is the original,
    /// every later one a duplicate. Only refund that took financial effect is printed. Needs an Idempotency-Key, so a
    /// request sent twice is one print; no body.
    /// </summary>
    [Authorize(Policy = Policies.ReadRefunds)]
    [HttpPost("{id:guid}/prints")]
    [Idempotent]
    [ProducesResponseType<DocumentPrintDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RecordPrintAsync(
        Guid id,
        [FromServices] ICommandHandler<RecordDocumentPrintCommand, DocumentPrintDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new RecordDocumentPrintCommand(PrintedDocumentType.Refund, id), ct);

        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : Problem(result.Error);
    }
}
