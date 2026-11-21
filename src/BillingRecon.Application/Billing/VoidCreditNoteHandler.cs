using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Voids a draft credit note: it keeps its content, records who voided it, when and why, and can no longer change.
/// The client must have read the current version (BR-804).
/// </summary>
public sealed class VoidCreditNoteHandler(
    IValidator<VoidCreditNoteCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    ICreditNoteRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<VoidCreditNoteCommand, CreditNoteDto>
{
    /// <inheritdoc />
    public async Task<Result<CreditNoteDto>> HandleAsync(VoidCreditNoteCommand command, CancellationToken ct)
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

        var voided = creditNote.Void(command.Reason, clock.UtcNow, currentUser.UserId);
        if (voided.IsFailure)
        {
            return voided.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? creditNote.ToDto() : committed.Error;
    }
}
