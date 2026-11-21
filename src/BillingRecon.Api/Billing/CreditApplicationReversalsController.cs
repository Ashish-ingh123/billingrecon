using BillingRecon.Api.Common;
using BillingRecon.Api.Common.Idempotency;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BillingRecon.Api.Billing;

/// <summary>
/// Reversing an application of a credit note's credit to a bill (ADR 0028; BR-213, BR-604). A cashier asks with a
/// reason, which answers 202 and changes nothing financial; a second person decides: an approval is the reversal,
/// with its mirror ledger posting, and a rejection leaves everything as it was. The application a credit note made to
/// its own bill when it was posted cannot be reversed. Nothing is deleted. Every request needs the version of the
/// credit note in If-Match (BR-804) and an Idempotency-Key, and answers the credit note with its applications.
/// </summary>
[Route(ApiRoutes.V1 + "/credit-notes")]
public sealed class CreditApplicationReversalsController : ApiControllerBase
{
    /// <summary>
    /// A cashier asks for one application of the credit note to be reversed. 202: the application waits in
    /// <c>PendingReversal</c>, still applied to its bill. 404 when the credit note has no such application; 409 when
    /// it already waits or is reversed; 422 when it is the application made at posting.
    /// </summary>
    [HttpPost("{id:guid}/applications/{applicationId:guid}/reverse")]
    [Idempotent]
    [ProducesResponseType<CreditNoteDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> RequestReversalAsync(
        Guid id,
        Guid applicationId,
        ReverseCreditApplicationRequest request,
        [FromServices] ICommandHandler<RequestCreditApplicationReversalCommand, CreditNoteDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var command = new RequestCreditApplicationReversalCommand(new CreditNoteId(id), new CreditNoteAllocationId(applicationId), version, request.Reason ?? string.Empty);
        var result = await handler.HandleAsync(command, ct);
        if (result.IsFailure)
        {
            return Problem(result.Error);
        }

        // Accepted and not done yet: the checker's approval is the reversal (BR-606).
        var answer = OkWithVersion(result.Value, result.Value.Version);
        answer.StatusCode = StatusCodes.Status202Accepted;

        return answer;
    }

    /// <summary>
    /// A second person approves the reversal of the application, and that reverses it (ADR 0028; BR-603, BR-606): the
    /// amount is available credit again, the bill is owed it again and its status moves back, and the ledger gets the
    /// mirror pair, in one transaction. The cashier who asked cannot approve: 403. The body may be left out.
    /// </summary>
    [HttpPost("{id:guid}/applications/{applicationId:guid}/approve-reversal")]
    [Idempotent]
    [ProducesResponseType<CreditNoteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> ApproveReversalAsync(
        Guid id,
        Guid applicationId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ApproveCreditApplicationReversalRequest? request,
        [FromServices] ICommandHandler<ApproveCreditApplicationReversalCommand, CreditNoteDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var command = new ApproveCreditApplicationReversalCommand(new CreditNoteId(id), new CreditNoteAllocationId(applicationId), version, request?.Comment);
        var result = await handler.HandleAsync(command, ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>A second person rejects the reversal of the application, saying why (BR-605): the application is in force again, as it was.</summary>
    [HttpPost("{id:guid}/applications/{applicationId:guid}/reject-reversal")]
    [Idempotent]
    [ProducesResponseType<CreditNoteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> RejectReversalAsync(
        Guid id,
        Guid applicationId,
        RejectCreditApplicationReversalRequest request,
        [FromServices] ICommandHandler<RejectCreditApplicationReversalCommand, CreditNoteDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var command = new RejectCreditApplicationReversalCommand(new CreditNoteId(id), new CreditNoteAllocationId(applicationId), version, request.Comment ?? string.Empty);
        var result = await handler.HandleAsync(command, ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }
}
