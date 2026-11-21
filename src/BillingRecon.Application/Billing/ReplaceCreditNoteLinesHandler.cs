using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Replaces the lines of a draft credit note. The client must have read the current version (BR-804): a stale one is
/// refused before anything changes, and the commit checks the row version once more, also when only lines changed.
/// </summary>
public sealed class ReplaceCreditNoteLinesHandler(
    IValidator<ReplaceCreditNoteLinesCommand> validator,
    IBillRepository bills,
    ICreditNoteRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<ReplaceCreditNoteLinesCommand, CreditNoteDto>
{
    /// <inheritdoc />
    public async Task<Result<CreditNoteDto>> HandleAsync(ReplaceCreditNoteLinesCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        var creditNote = await repository.GetAsync(command.Id, ct);
        if (creditNote is null)
        {
            return BillingErrors.CreditNoteNotFound();
        }

        if (RowVersionText.Of(creditNote) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        // The state is the first rule: a credit note that cannot be edited needs no look at the bill.
        var editable = creditNote.CheckIsDraft();
        if (editable.IsFailure)
        {
            return editable.Error;
        }

        var bill = await bills.GetOfAsync(creditNote, ct);
        var lines = CreditNoteLineResolver.Resolve(bill, command.Lines);
        if (lines.IsFailure)
        {
            return lines.Error;
        }

        var replaced = creditNote.ReplaceLines(lines.Value);
        if (replaced.IsFailure)
        {
            return replaced.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? creditNote.ToDto() : committed.Error;
    }
}
