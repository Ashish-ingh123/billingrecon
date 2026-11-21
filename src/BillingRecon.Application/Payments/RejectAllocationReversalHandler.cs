using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>
/// The checker rejects the reversal of an allocation (BR-603, BR-605): the request is closed with the checker's
/// comment and the allocation is in force again, in one commit. Nothing financial happened, and nothing does.
/// </summary>
public sealed class RejectAllocationReversalHandler(
    IValidator<RejectAllocationReversalCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    IPaymentRepository repository,
    IApprovalRequestRepository approvals,
    IUnitOfWork unitOfWork) : ICommandHandler<RejectAllocationReversalCommand, PaymentAllocationsDto>
{
    /// <inheritdoc />
    public async Task<Result<PaymentAllocationsDto>> HandleAsync(RejectAllocationReversalCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        var payment = await repository.GetAsync(command.Id, ct);
        if (payment is null)
        {
            return PaymentsErrors.PaymentNotFound();
        }

        if (RowVersionText.Of(payment) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        if (currentUser.UserId is not { } checker)
        {
            return CommonErrors.ApprovalNeedsUser();
        }

        // The allocation first: one that does not wait has no request to look for.
        var returned = payment.RejectAllocationReversal(command.AllocationId, command.Comment);
        if (returned.IsFailure)
        {
            return returned.Error;
        }

        var pending = await approvals.FindPendingAsync(ApprovalDocumentType.PaymentAllocation, command.AllocationId.Value, ct);
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

        return committed.IsSuccess ? payment.ToDtoWithAllocations() : committed.Error;
    }
}
