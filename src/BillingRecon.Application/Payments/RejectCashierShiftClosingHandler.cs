using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>
/// A checker rejects the closing of a shift, saying why (US-47; BR-603, BR-605; ADR 0060): the shift is open again
/// and its cashier counts it again. The decision and the shift are one commit. The maker of the request, the cashier,
/// decides nothing.
/// </summary>
public sealed class RejectCashierShiftClosingHandler(
    IValidator<RejectCashierShiftClosingCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    ICashierShiftRepository shifts,
    IApprovalRequestRepository approvals,
    IShiftCash shiftCash,
    IUserNames userNames,
    IUnitOfWork unitOfWork) : ICommandHandler<RejectCashierShiftClosingCommand, CashierShiftDto>
{
    /// <inheritdoc />
    public async Task<Result<CashierShiftDto>> HandleAsync(RejectCashierShiftClosingCommand command, CancellationToken ct)
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

        var pending = await approvals.FindPendingAsync(ApprovalDocumentType.CashierShift, shift.Id.Value, ct);
        if (pending.IsFailure)
        {
            return pending.Error;
        }

        var decided = pending.Value.Reject(checker, currentUser.Roles, command.Comment!, clock.UtcNow);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        var rejected = shift.RejectClosing(command.Comment!);
        if (rejected.IsFailure)
        {
            return rejected.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? await shift.ToDtoAsync(shiftCash, userNames, ct) : committed.Error;
    }
}
