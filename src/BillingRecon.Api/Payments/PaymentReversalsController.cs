using BillingRecon.Api.Common;
using BillingRecon.Api.Common.Idempotency;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BillingRecon.Api.Payments;

/// <summary>
/// Reversing a payment or one of its allocations (US-32, US-33; BR-305, BR-306, BR-604). Each is asked for by a
/// cashier with a reason, which answers 202 and changes nothing financial, and decided by a second person: an approval
/// is the reversal, with its mirror ledger postings; a rejection leaves everything as it was. Nothing is deleted. Every
/// request needs the version of the payment in If-Match (BR-804) and an Idempotency-Key, and answers the payment with
/// its allocations.
/// </summary>
[Route(ApiRoutes.V1 + "/payments")]
public sealed class PaymentReversalsController : ApiControllerBase
{
    /// <summary>
    /// A cashier asks for the payment to be reversed, for example a dishonoured cheque (US-33, BR-306). 202: the
    /// payment waits in <c>PendingReversal</c> and nothing can be allocated from it meanwhile. 409 when it already
    /// waits, is reversed, or one of its allocations waits for a decision of its own.
    /// </summary>
    [HttpPost("{id:guid}/reverse")]
    [Idempotent]
    [ProducesResponseType<PaymentAllocationsDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> RequestReversalAsync(
        Guid id,
        RequestReversalRequest request,
        [FromServices] ICommandHandler<RequestPaymentReversalCommand, PaymentAllocationsDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(new RequestPaymentReversalCommand(new PaymentId(id), version, request.Reason ?? string.Empty), ct);

        return result.IsSuccess ? Accepted(result.Value) : Problem(result.Error);
    }

    /// <summary>
    /// A second person approves the reversal of the payment, and that reverses it (BR-306, BR-603, BR-606): every
    /// allocation still applied is reversed first, then the payment, each with its mirror ledger posting, in one
    /// transaction. The cashier who asked cannot approve: 403. The body, an optional comment, may be left out.
    /// </summary>
    [HttpPost("{id:guid}/approve-reversal")]
    [Idempotent]
    [ProducesResponseType<PaymentAllocationsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> ApproveReversalAsync(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ApproveReversalRequest? request,
        [FromServices] ICommandHandler<ApprovePaymentReversalCommand, PaymentAllocationsDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(new ApprovePaymentReversalCommand(new PaymentId(id), version, request?.Comment), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Payment.Version) : Problem(result.Error);
    }

    /// <summary>A second person rejects the reversal of the payment, saying why (BR-605): the payment is posted again, as it was.</summary>
    [HttpPost("{id:guid}/reject-reversal")]
    [Idempotent]
    [ProducesResponseType<PaymentAllocationsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> RejectReversalAsync(
        Guid id,
        RejectReversalRequest request,
        [FromServices] ICommandHandler<RejectPaymentReversalCommand, PaymentAllocationsDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(new RejectPaymentReversalCommand(new PaymentId(id), version, request.Comment ?? string.Empty), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Payment.Version) : Problem(result.Error);
    }

    /// <summary>
    /// A cashier asks for one allocation of the payment to be reversed (US-32, BR-305). 202: the allocation waits in
    /// <c>PendingReversal</c>, still applied to its bill. 404 when the payment has no such allocation; 409 when it
    /// already waits or is reversed, or the payment itself waits for a reversal or is reversed.
    /// </summary>
    [HttpPost("{id:guid}/allocations/{allocationId:guid}/reverse")]
    [Idempotent]
    [ProducesResponseType<PaymentAllocationsDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> RequestAllocationReversalAsync(
        Guid id,
        Guid allocationId,
        RequestReversalRequest request,
        [FromServices] ICommandHandler<RequestAllocationReversalCommand, PaymentAllocationsDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var command = new RequestAllocationReversalCommand(new PaymentId(id), new PaymentAllocationId(allocationId), version, request.Reason ?? string.Empty);
        var result = await handler.HandleAsync(command, ct);

        return result.IsSuccess ? Accepted(result.Value) : Problem(result.Error);
    }

    /// <summary>
    /// A second person approves the reversal of the allocation, and that reverses it (BR-305, BR-603, BR-606): the
    /// amount is unapplied again, the bill is owed it again and its status moves back, and the ledger gets the mirror
    /// pair, in one transaction. The cashier who asked cannot approve: 403. The body may be left out.
    /// </summary>
    [HttpPost("{id:guid}/allocations/{allocationId:guid}/approve-reversal")]
    [Idempotent]
    [ProducesResponseType<PaymentAllocationsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> ApproveAllocationReversalAsync(
        Guid id,
        Guid allocationId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ApproveReversalRequest? request,
        [FromServices] ICommandHandler<ApproveAllocationReversalCommand, PaymentAllocationsDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var command = new ApproveAllocationReversalCommand(new PaymentId(id), new PaymentAllocationId(allocationId), version, request?.Comment);
        var result = await handler.HandleAsync(command, ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Payment.Version) : Problem(result.Error);
    }

    /// <summary>A second person rejects the reversal of the allocation, saying why (BR-605): the allocation is in force again, as it was.</summary>
    [HttpPost("{id:guid}/allocations/{allocationId:guid}/reject-reversal")]
    [Idempotent]
    [ProducesResponseType<PaymentAllocationsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> RejectAllocationReversalAsync(
        Guid id,
        Guid allocationId,
        RejectReversalRequest request,
        [FromServices] ICommandHandler<RejectAllocationReversalCommand, PaymentAllocationsDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var command = new RejectAllocationReversalCommand(new PaymentId(id), new PaymentAllocationId(allocationId), version, request.Comment ?? string.Empty);
        var result = await handler.HandleAsync(command, ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Payment.Version) : Problem(result.Error);
    }

    // 202 with the payment and its version as the ETag: accepted and not done yet, the checker's approval is the reversal.
    private OkObjectResult Accepted(PaymentAllocationsDto waiting)
    {
        var answer = OkWithVersion(waiting, waiting.Payment.Version);
        answer.StatusCode = StatusCodes.Status202Accepted;

        return answer;
    }
}
