using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>
/// The checker rejects the reversal of a payment (BR-603, BR-605): the request is closed with the checker's comment
/// and the payment is posted again, in one commit. Nothing financial happened, and nothing does.
/// </summary>
public sealed class RejectPaymentReversalHandler(
    IValidator<RejectPaymentReversalCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    IPaymentRepository repository,
    IApprovalRequestRepository approvals,
    IUnitOfWork unitOfWork) : ICommandHandler<RejectPaymentReversalCommand, PaymentAllocationsDto>
{
    /// <inheritdoc />
    public async Task<Result<PaymentAllocationsDto>> HandleAsync(RejectPaymentReversalCommand command, CancellationToken ct)
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

        // The payment first: one that does not wait has no request to look for.
        var returned = payment.RejectReversal(command.Comment);
        if (returned.IsFailure)
        {
            return returned.Error;
        }

        var pending = await approvals.FindPendingAsync(ApprovalDocumentType.Payment, payment.Id.Value, ct);
        if (pending.IsFailure)
        {
            return pending.Error;
        }

        var request = pending.Value;
        var decided = request.Reject(checker, currentUser.Roles, command.Comment, clock.UtcNow);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? payment.ToDtoWithAllocations() : committed.Error;
    }
}
