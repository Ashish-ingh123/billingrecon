using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>
/// The checker rejects a credit note that waits for approval (BR-603, BR-605): the request is closed with the
/// checker's comment and the credit note is a draft again, in one commit. Nothing financial happens. The maker can
/// correct the draft and ask again, which opens a new request.
/// </summary>
public sealed class RejectCreditNoteHandler(
    IValidator<RejectCreditNoteCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    ICreditNoteRepository repository,
    IApprovalRequestRepository approvals,
    IUnitOfWork unitOfWork) : ICommandHandler<RejectCreditNoteCommand, CreditNoteDto>
{
    /// <inheritdoc />
    public async Task<Result<CreditNoteDto>> HandleAsync(RejectCreditNoteCommand command, CancellationToken ct)
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

        if (currentUser.UserId is not { } checker)
        {
            return CommonErrors.ApprovalNeedsUser();
        }

        // The credit note first: one that does not wait has no request to look for.
        var returned = creditNote.ReturnToDraft(command.Comment);
        if (returned.IsFailure)
        {
            return returned.Error;
        }

        var pending = await approvals.FindPendingAsync(ApprovalDocumentType.CreditNote, creditNote.Id.Value, ct);
        if (pending.IsFailure)
        {
            return pending.Error;
        }

        var request = pending.Value;
        var decided = request.Reject(checker, command.Comment, clock.UtcNow);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? creditNote.ToDto() : committed.Error;
    }
}
