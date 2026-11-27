using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>
/// A checker approves the closing of a shift with its difference, and that closes it (US-47; BR-314, BR-315, BR-603,
/// BR-606; ADR 0060): the decision on the request, the shift closed and the audit records are one commit. The checker
/// holds Supervisor or TenantAdmin and is not the cashier of the shift, who is the maker of the request. Nothing is
/// posted to the ledger: the difference stays what the shift recorded.
/// </summary>
public sealed class ApproveCashierShiftClosingHandler(
    IValidator<ApproveCashierShiftClosingCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    ICashierShiftRepository shifts,
    IApprovalRequestRepository approvals,
    IShiftCash shiftCash,
    IUserNames userNames,
    IUnitOfWork unitOfWork) : ICommandHandler<ApproveCashierShiftClosingCommand, CashierShiftDto>
{
    /// <inheritdoc />
    public async Task<Result<CashierShiftDto>> HandleAsync(ApproveCashierShiftClosingCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        var shift = await shifts.GetAsync(command.Id, ct);
        if (shift is null)
        {
            return PaymentsErrors.ShiftNotFound();
        }

        if (RowVersionText.Of(shift) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        var waits = shift.CheckAwaitsApproval();
        if (waits.IsFailure)
        {
            return waits.Error;
        }

        if (currentUser.UserId is not { } checker)
        {
            return CommonErrors.ApprovalNeedsUser();
        }

        // A closing that waits has exactly one pending request: the same commit made both (BR-605). When it is gone,
        // another decision was committed after the shift was read here: a conflict, not a fault (BR-804).
        var pending = await approvals.FindPendingAsync(ApprovalDocumentType.CashierShift, shift.Id.Value, ct);
        if (pending.IsFailure)
        {
            return pending.Error;
        }

        var now = clock.UtcNow;
        var decided = pending.Value.Approve(checker, currentUser.Roles, command.Comment, now);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        var approved = shift.ApproveClosing(now);
        if (approved.IsFailure)
        {
            return approved.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? await shift.ToDtoAsync(shiftCash, userNames, ct) : committed.Error;
    }
}
