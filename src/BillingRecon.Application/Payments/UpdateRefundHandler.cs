using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>
/// Replaces a draft refund (the date, the amount, the method, the reference and the reason) under the rules of a new
/// one. A refund that is not a draft is refused before its source is read.
/// </summary>
public sealed class UpdateRefundHandler(
    IValidator<UpdateRefundCommand> validator,
    ITenantSettingsReader settings,
    IClock clock,
    IPaymentRepository payments,
    ICreditNoteSettlement creditNotes,
    IRefundRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<UpdateRefundCommand, RefundDto>
{
    /// <inheritdoc />
    public async Task<Result<RefundDto>> HandleAsync(UpdateRefundCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        // The validator refuses a command without these, so no request takes these branches.
        var refundDate = command.RefundDate ?? throw new ArgumentException("The command has no refund date.", nameof(command));
        var amount = Money.Of(command.Amount ?? throw new ArgumentException("The command has no amount.", nameof(command)));
        var method = command.Method ?? throw new ArgumentException("The command has no method.", nameof(command));

        var refund = await repository.GetAsync(command.Id, ct);
        if (refund is null)
        {
            return PaymentsErrors.RefundNotFound();
        }

        if (RowVersionText.Of(refund) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        // The state is the first rule: a refund that cannot be corrected needs no look at its source.
        var editable = refund.CheckIsDraft();
        if (editable.IsFailure)
        {
            return editable.Error;
        }

        var found = await RefundSources.FindAsync(refund.Source, payments, creditNotes, ct);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var dates = await settings.GetDocumentSettingsAsync(ct);
        var updated = refund.Update(refundDate, amount, method, command.Reference, command.Reason, found.Value.Date, new BusinessDateWindow(clock.TodayIst, dates.MaxBackdateDays));
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? refund.ToDto() : committed.Error;
    }
}
