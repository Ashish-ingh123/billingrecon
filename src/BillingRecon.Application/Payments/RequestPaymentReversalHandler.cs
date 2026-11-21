using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>
/// The maker's half of a payment reversal (US-33; BR-306, BR-604): nothing financial happens. The payment waits, and
/// nothing can be allocated from it meanwhile; an approval request with the current user as its maker and the reason
/// as its comment is opened in the same commit. Refused while one of its allocations waits for a decision of its own,
/// and for good once a refund was posted from it (BR-306, ADR 0029). The checker's approval is the reversal
/// (<see cref="ApprovePaymentReversalHandler"/>).
/// </summary>
public sealed class RequestPaymentReversalHandler(
    IValidator<RequestPaymentReversalCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    ICorrelationContext correlation,
    IPaymentRepository repository,
    IRefundRepository refunds,
    IApprovalRequestRepository approvals,
    IUnitOfWork unitOfWork) : ICommandHandler<RequestPaymentReversalCommand, PaymentAllocationsDto>
{
    /// <inheritdoc />
    public async Task<Result<PaymentAllocationsDto>> HandleAsync(RequestPaymentReversalCommand command, CancellationToken ct)
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

        // The money of a posted refund left already: the payment is never reversed (BR-306). A refund cannot be posted
        // from the payment while it waits for its reversal, so the answer cannot change between here and the approval.
        if (await refunds.AnyPostedFromPaymentAsync(payment.Id, ct))
        {
            return PaymentsErrors.PaymentRefunded();
        }

        var requested = payment.RequestReversal(command.Reason);
        if (requested.IsFailure)
        {
            return requested.Error;
        }

        approvals.Add(ApprovalRequest.Open(
            ApprovalDocumentType.Payment, payment.Id.Value, ApprovalAction.Reverse, maker, command.Reason, clock.UtcNow, correlation.CorrelationId));

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? payment.ToDtoWithAllocations() : committed.Error;
    }
}
