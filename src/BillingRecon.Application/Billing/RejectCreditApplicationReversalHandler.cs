using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>
/// The checker rejects the reversal of an application of credit (BR-603, BR-605): the request is closed with the
/// checker's comment and the application is in force again, in one commit. Nothing financial happened, and nothing
/// does.
/// </summary>
public sealed class RejectCreditApplicationReversalHandler(
    IValidator<RejectCreditApplicationReversalCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    ICreditNoteRepository repository,
    IApprovalRequestRepository approvals,
    IUnitOfWork unitOfWork) : ICommandHandler<RejectCreditApplicationReversalCommand, CreditNoteDto>
{
    /// <inheritdoc />
    public async Task<Result<CreditNoteDto>> HandleAsync(RejectCreditApplicationReversalCommand command, CancellationToken ct)
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

        // The application first: one that does not wait has no request to look for.
        var returned = creditNote.RejectApplicationReversal(command.ApplicationId, command.Comment);
        if (returned.IsFailure)
        {
            return returned.Error;
        }

        var pending = await approvals.FindPendingAsync(ApprovalDocumentType.CreditNoteAllocation, command.ApplicationId.Value, ct);
        if (pending.IsFailure)
        {
            return pending.Error;
        }

        var decided = pending.Value.Reject(checker, command.Comment, clock.UtcNow);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? creditNote.ToDto() : committed.Error;
    }
}
