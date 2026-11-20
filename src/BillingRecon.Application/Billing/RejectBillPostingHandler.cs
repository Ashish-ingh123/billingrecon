using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>
/// The checker rejects a bill that waits for approval (BR-603, BR-605): the request is closed with the checker's
/// comment and the bill is a draft again, in one commit. Nothing financial happens: the bill had no number and no
/// ledger entry, and has none now. The maker can correct the draft and post it again, which opens a new request.
/// </summary>
public sealed class RejectBillPostingHandler(
    IValidator<RejectBillPostingCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    IBillRepository repository,
    IApprovalRequestRepository approvals,
    IUnitOfWork unitOfWork) : ICommandHandler<RejectBillPostingCommand, BillDto>
{
    /// <inheritdoc />
    public async Task<Result<BillDto>> HandleAsync(RejectBillPostingCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        var bill = await repository.GetAsync(command.Id, ct);
        if (bill is null)
        {
            return BillingErrors.BillNotFound();
        }

        if (RowVersionText.Of(bill) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        if (currentUser.UserId is not { } checker)
        {
            return CommonErrors.ApprovalNeedsUser();
        }

        // The bill first: a bill that does not wait has no request to look for.
        var returned = bill.ReturnToDraft(command.Comment);
        if (returned.IsFailure)
        {
            return returned.Error;
        }

        var request = await approvals.GetPendingAsync(ApprovalDocumentType.Bill, bill.Id.Value, ct)
            ?? throw new InvalidOperationException($"Bill {bill.Id} waits for approval and has no pending approval request.");

        var decided = request.Reject(checker, command.Comment, clock.UtcNow);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? bill.ToDto() : committed.Error;
    }
}
