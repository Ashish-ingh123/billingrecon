using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Adjustments;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Adjustments;

/// <summary>
/// Creates a draft adjustment against a posted bill (BR-501). The bill is read through the Billing module's contract:
/// it must exist for the tenant and be posted, and the adjustment takes its payer from it. The date defaults to today
/// in India and must be allowed today and not before the bill date; a rounding is less than one rupee. A draft has no
/// number and no ledger effect, so nothing but the adjustment is written; whether the amount fits what the bill is
/// owed is asked when it is submitted.
/// </summary>
public sealed class CreateAdjustmentDraftHandler(
    IValidator<CreateAdjustmentDraftCommand> validator,
    ITenantSettingsReader settings,
    IClock clock,
    IBillSettlement bills,
    IAdjustmentRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<CreateAdjustmentDraftCommand, AdjustmentDto>
{
    /// <inheritdoc />
    public async Task<Result<AdjustmentDto>> HandleAsync(CreateAdjustmentDraftCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        // The validator refuses a command without these, so no request takes these branches.
        var kind = command.Kind ?? throw new ArgumentException("The command has no kind.", nameof(command));
        var amount = Money.Of(command.Amount ?? throw new ArgumentException("The command has no amount.", nameof(command)));

        var bill = await bills.FindPostedAsync(command.BillId, ct);
        if (bill.IsFailure)
        {
            return bill.Error;
        }

        // One reading of the clock for the use case (BR-003).
        var today = clock.TodayIst;
        var dates = await settings.GetDocumentSettingsAsync(ct);

        var created = Adjustment.Create(
            command.BillId,
            bill.Value.PayerId,
            bill.Value.BillDate,
            command.AdjustmentDate ?? today,
            kind,
            amount,
            command.Reason,
            new BusinessDateWindow(today, dates.MaxBackdateDays));
        if (created.IsFailure)
        {
            return created.Error;
        }

        var adjustment = created.Value;
        repository.Add(adjustment);

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? adjustment.ToDto() : committed.Error;
    }
}
