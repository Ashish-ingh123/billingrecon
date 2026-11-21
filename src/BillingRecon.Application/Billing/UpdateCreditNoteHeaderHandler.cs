using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Replaces the date and the reason of a draft credit note (BR-205, BR-214). The client must have read the current
/// version (BR-804).
/// </summary>
public sealed class UpdateCreditNoteHeaderHandler(
    IValidator<UpdateCreditNoteHeaderCommand> validator,
    ITenantSettingsReader settings,
    IClock clock,
    IBillRepository bills,
    ICreditNoteRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<UpdateCreditNoteHeaderCommand, CreditNoteDto>
{
    /// <inheritdoc />
    public async Task<Result<CreditNoteDto>> HandleAsync(UpdateCreditNoteHeaderCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        // The validator refuses a command without its date, so no request takes this branch.
        var creditNoteDate = command.CreditNoteDate ?? throw new ArgumentException("The command has no credit note date.", nameof(command));

        var creditNote = await repository.GetAsync(command.Id, ct);
        if (creditNote is null)
        {
            return BillingErrors.CreditNoteNotFound();
        }

        if (RowVersionText.Of(creditNote) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        var bill = await bills.GetOfAsync(creditNote, ct);
        var dates = await settings.GetDocumentSettingsAsync(ct);

        var updated = creditNote.UpdateHeader(creditNoteDate, command.Reason, bill.BillDate, new BusinessDateWindow(clock.TodayIst, dates.MaxBackdateDays));
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? creditNote.ToDto() : committed.Error;
    }
}
