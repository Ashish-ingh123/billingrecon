using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Adjustments;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Adjustments;

/// <summary>
/// Replaces the date, the kind, the amount and the reason of a draft adjustment (BR-501, BR-205). The client must
/// have read the current version (BR-804): a stale one is refused before anything changes.
/// </summary>
public sealed class UpdateAdjustmentHandler(
    IValidator<UpdateAdjustmentCommand> validator,
    ITenantSettingsReader settings,
    IClock clock,
    IBillSettlement bills,
    IAdjustmentRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<UpdateAdjustmentCommand, AdjustmentDto>
{
    /// <inheritdoc />
    public async Task<Result<AdjustmentDto>> HandleAsync(UpdateAdjustmentCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        // The validator refuses a command without these, so no request takes these branches.
        var adjustmentDate = command.AdjustmentDate ?? throw new ArgumentException("The command has no adjustment date.", nameof(command));
        var kind = command.Kind ?? throw new ArgumentException("The command has no kind.", nameof(command));
        var amount = Money.Of(command.Amount ?? throw new ArgumentException("The command has no amount.", nameof(command)));

        var adjustment = await repository.GetAsync(command.Id, ct);
        if (adjustment is null)
        {
            return AdjustmentsErrors.AdjustmentNotFound();
        }

        if (RowVersionText.Of(adjustment) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        // The state is the first rule: an adjustment that cannot be corrected needs no look at the bill.
        var editable = adjustment.CheckIsDraft();
        if (editable.IsFailure)
        {
            return editable.Error;
        }

        var bill = await bills.FindPostedAsync(adjustment.BillId, ct);
        if (bill.IsFailure)
        {
            return bill.Error;
        }

        var dates = await settings.GetDocumentSettingsAsync(ct);
        var updated = adjustment.Update(adjustmentDate, kind, amount, command.Reason, bill.Value.BillDate, new BusinessDateWindow(clock.TodayIst, dates.MaxBackdateDays));
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? adjustment.ToDto() : committed.Error;
    }
}
