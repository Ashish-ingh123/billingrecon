using BillingRecon.Api.Common;
using BillingRecon.Api.Common.Authorization;
using BillingRecon.Api.Common.Idempotency;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BillingRecon.Api.Payments;

/// <summary>
/// The payments of the tenant (US-30; BR-301, BR-302, BR-309): record, read and list. Recording a payment posts it:
/// the gapless number, the ledger entry and the audit record, in one transaction (BR-007). A payment is never edited
/// or deleted. Allocating it to bills (US-31, BR-303) is an action of its own; reversals and refunds arrive with
/// their slices.
/// </summary>
[Route(ApiRoutes.V1 + "/payments")]
public sealed class PaymentsController : ApiControllerBase
{
    private const string GetPaymentRoute = "GetPayment";

    /// <summary>
    /// Allocates the payment to one or more posted bills of its payer, all of them or none (BR-303, BR-304). Each
    /// bill's balance due and status follow (BR-208). 422 when an amount is more than the payment has unapplied or
    /// the bill is owed, the bill is not posted, or it is owed by another payer. Needs If-Match, with the version
    /// of the payment, and an Idempotency-Key.
    /// </summary>
    [Authorize(Policy = Policies.RecordPayments)]
    [HttpPost("{id:guid}/allocations")]
    [Idempotent]
    [ProducesResponseType<PaymentAllocationsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> AllocateAsync(
        Guid id,
        AllocatePaymentRequest request,
        [FromServices] ICommandHandler<AllocatePaymentCommand, PaymentAllocationsDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(request.ToCommand(new PaymentId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Payment.Version) : Problem(result.Error);
    }

    /// <summary>Reads one payment with every allocation made from it, the oldest first.</summary>
    [Authorize(Policy = Policies.ReadPayments)]
    [HttpGet("{id:guid}/allocations")]
    [ProducesResponseType<PaymentAllocationsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAllocationsAsync(
        Guid id,
        [FromServices] IQueryHandler<GetPaymentAllocationsQuery, PaymentAllocationsDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GetPaymentAllocationsQuery(new PaymentId(id)), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Payment.Version) : Problem(result.Error);
    }

    /// <summary>
    /// Records money received from an active payer. 422 when the payer is inactive or the date is not allowed today
    /// (BR-205, BR-309). Needs an Idempotency-Key.
    /// </summary>
    [Authorize(Policy = Policies.RecordPayments)]
    [HttpPost]
    [Idempotent]
    [ProducesResponseType<PaymentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RecordAsync(
        RecordPaymentRequest request,
        [FromServices] ICommandHandler<RecordPaymentCommand, PaymentDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(request.ToCommand(), ct);

        return result.IsSuccess
            ? CreatedWithVersion(GetPaymentRoute, result.Value.Id, result.Value, result.Value.Version)
            : Problem(result.Error);
    }

    /// <summary>Reads one payment.</summary>
    [Authorize(Policy = Policies.ReadPayments)]
    [HttpGet("{id:guid}", Name = GetPaymentRoute)]
    [ProducesResponseType<PaymentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(
        Guid id,
        [FromServices] IQueryHandler<GetPaymentQuery, PaymentDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GetPaymentQuery(new PaymentId(id)), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>Lists the payments one page at a time, the newest payment date first.</summary>
    [Authorize(Policy = Policies.ReadPayments)]
    [HttpGet]
    [ProducesResponseType<PageOf<PaymentDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] ListPaymentsRequest request,
        [FromServices] IQueryHandler<ListPaymentsQuery, PageOf<PaymentDto>> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(request.ToQuery(), ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }
}
