using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>
/// The maker's half of an allocation reversal (US-32; BR-305, BR-604): nothing financial happens. The allocation
/// waits, still applied to its bill, and an approval request with the current user as its maker and the reason as its
/// comment is opened in the same commit. The checker's approval is the reversal
/// (<see cref="ApproveAllocationReversalHandler"/>).
/// </summary>
public sealed class RequestAllocationReversalHandler(
    IValidator<RequestAllocationReversalCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    ICorrelationContext correlation,
    IPaymentRepository repository,
    IApprovalRequestRepository approvals,
    IUnitOfWork unitOfWork) : ICommandHandler<RequestAllocationReversalCommand, PaymentAllocationsDto>
{
    /// <inheritdoc />
    public async Task<Result<PaymentAllocationsDto>> HandleAsync(RequestAllocationReversalCommand command, CancellationToken ct)
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

        if (currentUser.UserId is not { } maker)
        {
            return CommonErrors.ApprovalNeedsUser();
        }

        var requested = payment.RequestAllocationReversal(command.AllocationId, command.Reason);
        if (requested.IsFailure)
        {
            return requested.Error;
        }

        approvals.Add(ApprovalRequest.Open(
            ApprovalDocumentType.PaymentAllocation, command.AllocationId.Value, ApprovalAction.Reverse, maker, command.Reason, clock.UtcNow, correlation.CorrelationId));

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? payment.ToDtoWithAllocations() : committed.Error;
    }
}
