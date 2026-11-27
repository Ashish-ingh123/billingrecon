using BillingRecon.Api.Common;
using BillingRecon.Api.Common.Authorization;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BillingRecon.Api.Payments;

/// <summary>
/// The shifts of the cash counter (BR-310 to BR-315; ADR 0060; docs/api.md). A user opens a shift with a float and
/// closes it with a count; a closing with a difference waits for a checker. A shift is not a financial document: it
/// has no number and posts nothing, so its POSTs take no <c>Idempotency-Key</c>; a second opening is refused by the
/// rule of one shift at a time, and a closing by the version of the shift.
/// </summary>
[Route(ApiRoutes.V1 + "/cashier-shifts")]
public sealed class CashierShiftsController : ApiControllerBase
{
    private const string GetCashierShiftRoute = "GetCashierShift";

    /// <summary>Opens a shift for the user who asks, with the float of the drawer. One shift at a time (BR-310).</summary>
    [Authorize(Policy = Policies.WorkShift)]
    [HttpPost]
    [ProducesResponseType<CashierShiftDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> OpenAsync(
        OpenCashierShiftRequest request,
        [FromServices] ICommandHandler<OpenCashierShiftCommand, CashierShiftDto> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await handler.HandleAsync(request.ToCommand(), ct);

        return result.IsSuccess
            ? CreatedWithVersion(GetCashierShiftRoute, result.Value.Id, result.Value, result.Value.Version)
            : Problem(result.Error);
    }

    /// <summary>One shift with the cash the ledger has for it. A Cashier and a BillingClerk read only their own: 404 otherwise.</summary>
    [Authorize(Policy = Policies.ReadShifts)]
    [HttpGet("{id:guid}", Name = GetCashierShiftRoute)]
    [ProducesResponseType<CashierShiftDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(
        Guid id,
        [FromServices] IQueryHandler<GetCashierShiftQuery, CashierShiftDto> handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(new GetCashierShiftQuery(new CashierShiftId(id)), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>Shifts, the newest first. A Cashier and a BillingClerk get their own, whatever is asked.</summary>
    [Authorize(Policy = Policies.ReadShifts)]
    [HttpGet]
    [ProducesResponseType<PageOf<CashierShiftDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] ListCashierShiftsRequest request,
        [FromServices] IQueryHandler<ListCashierShiftsQuery, PageOf<CashierShiftDto>> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await handler.HandleAsync(request.ToQuery(), ct);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>
    /// The cashier counts the drawer and closes the shift (BR-313). Closed when the count is the expected cash: 200.
    /// With a difference, which needs a reason, the closing waits for a checker: 202 (BR-314). Needs If-Match.
    /// </summary>
    [Authorize(Policy = Policies.WorkShift)]
    [HttpPost("{id:guid}/close")]
    [ProducesResponseType<CashierShiftDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<CashierShiftDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> CloseAsync(
        Guid id,
        CloseCashierShiftRequest request,
        [FromServices] ICommandHandler<CloseCashierShiftCommand, CashierShiftDto> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(request.ToCommand(new CashierShiftId(id), version), ct);
        if (result.IsFailure)
        {
            return Problem(result.Error);
        }

        // Closed: 200. Handed to a second person because of a difference: 202, accepted and not done yet (BR-314).
        var answer = OkWithVersion(result.Value, result.Value.Version);
        if (result.Value.Status == CashierShiftStatus.PendingApproval)
        {
            answer.StatusCode = StatusCodes.Status202Accepted;
        }

        return answer;
    }

    /// <summary>A checker approves the closing with its difference: the shift is closed (BR-314). Never its own cashier. Needs If-Match.</summary>
    [Authorize(Policy = Policies.Decide)]
    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType<CashierShiftDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> ApproveAsync(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DecideCashierShiftRequest? request,
        [FromServices] ICommandHandler<ApproveCashierShiftClosingCommand, CashierShiftDto> handler,
        CancellationToken ct)
    {
        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync((request ?? new DecideCashierShiftRequest()).ToApproval(new CashierShiftId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }

    /// <summary>A checker rejects the closing, saying why: the shift is open again, to be counted again (BR-605). Needs If-Match.</summary>
    [Authorize(Policy = Policies.Decide)]
    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType<CashierShiftDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> RejectAsync(
        Guid id,
        DecideCashierShiftRequest request,
        [FromServices] ICommandHandler<RejectCashierShiftClosingCommand, CashierShiftDto> handler,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryGetExpectedVersion(out var version, out var refusal))
        {
            return refusal;
        }

        var result = await handler.HandleAsync(request.ToRejection(new CashierShiftId(id), version), ct);

        return result.IsSuccess ? OkWithVersion(result.Value, result.Value.Version) : Problem(result.Error);
    }
}
