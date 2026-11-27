using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>
/// The cashier counts the drawer and closes the shift (US-46; BR-312, BR-313, BR-314; ADR 0060). The expected cash is
/// the float and the Cash entries of the ledger postings that name the shift, read here and from nowhere else. With no
/// difference the shift is closed; with one it waits, and an approval request is opened in the same commit (BR-605).
/// The difference is recorded on the shift and nothing is posted to the ledger.
/// <para>
/// The ledger is read after the shift, and the shift is saved under the version that was read: a cash document that
/// joined the shift in between changed that version, so this closing answers 409 and never closes on a figure the
/// ledger no longer has (BR-804).
/// </para>
/// </summary>
public sealed class CloseCashierShiftHandler(
    IValidator<CloseCashierShiftCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    ICorrelationContext correlation,
    ICashierShiftRepository shifts,
    IShiftCash shiftCash,
    IApprovalRequestRepository approvals,
    IUserNames userNames,
    IUnitOfWork unitOfWork) : ICommandHandler<CloseCashierShiftCommand, CashierShiftDto>
{
    /// <inheritdoc />
    public async Task<Result<CashierShiftDto>> HandleAsync(CloseCashierShiftCommand command, CancellationToken ct)
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

        if (currentUser.UserId is not { } cashier || cashier != shift.CashierUserId)
        {
            return PaymentsErrors.ShiftNotOwn();
        }

        if (RowVersionText.Of(shift) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        // The validator refuses a command without a count that is one, so no request takes this branch.
        var count = CashCount.Of((command.Count ?? throw new ArgumentException("The command has no count.", nameof(command)))
            .Select(line => (line.Denomination ?? 0, line.Pieces ?? 0)));
        var looseChange = Money.Of(command.LooseChange ?? 0m);

        var cash = await shiftCash.OfAsync(shift.Id, ct);
        var now = clock.UtcNow;
        var closed = shift.Close(count, looseChange, cash.Movement, command.Reason, now);
        if (closed.IsFailure)
        {
            return closed.Error;
        }

        if (shift.Status == CashierShiftStatus.PendingApproval)
        {
            approvals.Add(ApprovalRequest.Open(
                ApprovalDocumentType.CashierShift, shift.Id.Value, ApprovalAction.Close, cashier, shift.DifferenceReason, now, correlation.CorrelationId));
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? shift.ToDto(cash, await userNames.FindDisplayNameAsync(shift.CashierUserId, ct)) : committed.Error;
    }
}
