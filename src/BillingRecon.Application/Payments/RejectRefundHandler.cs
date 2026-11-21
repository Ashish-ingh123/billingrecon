using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>
/// The checker rejects a refund (BR-603, BR-605): the request is closed with the checker's comment and the refund is a
/// draft again, in one commit. Nothing financial happened, and nothing does.
/// </summary>
public sealed class RejectRefundHandler(
    IValidator<RejectRefundCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    IRefundRepository repository,
    IApprovalRequestRepository approvals,
    IUnitOfWork unitOfWork) : ICommandHandler<RejectRefundCommand, RefundDto>
{
    /// <inheritdoc />
    public async Task<Result<RefundDto>> HandleAsync(RejectRefundCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        var refund = await repository.GetAsync(command.Id, ct);
        if (refund is null)
        {
            return PaymentsErrors.RefundNotFound();
        }

        if (RowVersionText.Of(refund) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        if (currentUser.UserId is not { } checker)
        {
            return CommonErrors.ApprovalNeedsUser();
        }

        // The refund first: one that does not wait has no request to look for.
        var returned = refund.ReturnToDraft(command.Comment);
        if (returned.IsFailure)
        {
            return returned.Error;
        }

        var pending = await approvals.FindPendingAsync(ApprovalDocumentType.Refund, refund.Id.Value, ct);
        if (pending.IsFailure)
        {
            return pending.Error;
        }

        var decided = pending.Value.Reject(checker, currentUser.Roles, command.Comment, clock.UtcNow);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? refund.ToDto() : committed.Error;
    }
}
