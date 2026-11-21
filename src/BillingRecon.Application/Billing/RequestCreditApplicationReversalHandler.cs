using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>
/// The maker's half of reversing an application of credit (ADR 0028, BR-604): nothing financial happens. The
/// application waits, still applied to its bill, and an approval request with the current user as its maker and the
/// reason as its comment is opened in the same commit. The application a credit note made to its own bill when it
/// was posted cannot be reversed. The checker's approval is the reversal
/// (<see cref="ApproveCreditApplicationReversalHandler"/>).
/// </summary>
public sealed class RequestCreditApplicationReversalHandler(
    IValidator<RequestCreditApplicationReversalCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    ICorrelationContext correlation,
    ICreditNoteRepository repository,
    IApprovalRequestRepository approvals,
    IUnitOfWork unitOfWork) : ICommandHandler<RequestCreditApplicationReversalCommand, CreditNoteDto>
{
    /// <inheritdoc />
    public async Task<Result<CreditNoteDto>> HandleAsync(RequestCreditApplicationReversalCommand command, CancellationToken ct)
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

        if (currentUser.UserId is not { } maker)
        {
            return CommonErrors.ApprovalNeedsUser();
        }

        var requested = creditNote.RequestApplicationReversal(command.ApplicationId, command.Reason);
        if (requested.IsFailure)
        {
            return requested.Error;
        }

        approvals.Add(ApprovalRequest.Open(
            ApprovalDocumentType.CreditNoteAllocation, command.ApplicationId.Value, ApprovalAction.Reverse, maker, command.Reason, clock.UtcNow, correlation.CorrelationId));

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? creditNote.ToDto() : committed.Error;
    }
}
