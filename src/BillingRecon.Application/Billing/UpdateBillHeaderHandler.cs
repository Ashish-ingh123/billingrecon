using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Replaces the dates and the two texts of a draft bill (BR-204, BR-205). The client must have read the current
/// version (BR-804): a stale one is refused before anything changes, and the commit checks the row version once more.
/// </summary>
public sealed class UpdateBillHeaderHandler(
    IValidator<UpdateBillHeaderCommand> validator,
    ITenantSettingsReader settings,
    IClock clock,
    IBillRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<UpdateBillHeaderCommand, BillDto>
{
    /// <inheritdoc />
    public async Task<Result<BillDto>> HandleAsync(UpdateBillHeaderCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        // The validator refuses a command without its dates, so no request takes these branches.
        var billDate = command.BillDate ?? throw new ArgumentException("The command has no bill date.", nameof(command));
        var dueDate = command.DueDate ?? throw new ArgumentException("The command has no due date.", nameof(command));

        var bill = await repository.GetAsync(command.Id, ct);
        if (bill is null)
        {
            return BillingErrors.BillNotFound();
        }

        if (RowVersionText.Of(bill) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        var dates = await settings.GetDocumentSettingsAsync(ct);
        var updated = bill.UpdateHeader(
            new BillHeader { BillDate = billDate, DueDate = dueDate, EncounterReference = command.EncounterReference, Notes = command.Notes },
            new BusinessDateWindow(clock.TodayIst, dates.MaxBackdateDays));
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? bill.ToDto() : committed.Error;
    }
}
