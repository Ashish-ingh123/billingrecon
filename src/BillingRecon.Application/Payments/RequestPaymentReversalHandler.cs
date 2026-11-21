using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>
/// The maker's half of a payment reversal (US-33; BR-306, BR-604): nothing financial happens. The payment waits, and
/// nothing can be allocated from it meanwhile; an approval request with the current user as its maker and the reason
/// as its comment is opened in the same commit. Refused while one of its allocations waits for a decision of its own.
/// The checker's approval is the reversal (<see cref="ApprovePaymentReversalHandler"/>).
/// </summary>
public sealed class RequestPaymentReversalHandler(
    IValidator<RequestPaymentReversalCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    ICorrelationContext correlation,
    IPaymentRepository repository,
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
