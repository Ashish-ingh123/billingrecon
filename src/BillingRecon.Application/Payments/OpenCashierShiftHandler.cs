using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>
/// A user opens a shift with a float (US-45; BR-310; ADR 0060). The business date of the shift is today in India, and
/// it stays that date however long the shift is open (BR-311). A user has one shift at a time: a second one is
/// refused while the first is open or waits for approval, by this check and, for two requests at the same instant,
/// by a filtered unique index.
/// </summary>
public sealed class OpenCashierShiftHandler(
    IValidator<OpenCashierShiftCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    ICashierShiftRepository shifts,
    IShiftCash shiftCash,
    IUserNames userNames,
    IUnitOfWork unitOfWork) : ICommandHandler<OpenCashierShiftCommand, CashierShiftDto>
{
    /// <inheritdoc />
    public async Task<Result<CashierShiftDto>> HandleAsync(OpenCashierShiftCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        if (currentUser.UserId is not { } cashier)
        {
            return PaymentsErrors.ShiftNotOwn();
        }

        if (await shifts.AnyUnclosedOfAsync(cashier, ct))
        {
            return PaymentsErrors.ShiftAlreadyOpen();
        }

        // The validator refuses a command without a float, so no request takes this branch.
        var openingFloat = Money.Of(command.OpeningFloat ?? throw new ArgumentException("The command has no opening float.", nameof(command)));

        // One reading of the clock for the use case: the instant of the opening, and today in India from it (BR-003).
        var now = clock.UtcNow;
        var shift = CashierShift.Open(cashier, clock.ToIstDate(now), openingFloat, now);
        shifts.Add(shift);

        var committed = await unitOfWork.CommitAsync(ct);
        if (committed.IsFailure)
        {
            // Two openings by one user at the same instant: the filtered unique index let one through, and this is the other.
            return committed.Error.Code == CommonErrors.DuplicateKeyCode ? PaymentsErrors.ShiftAlreadyOpen() : committed.Error;
        }

        return await shift.ToDtoAsync(shiftCash, userNames, ct);
    }
}
