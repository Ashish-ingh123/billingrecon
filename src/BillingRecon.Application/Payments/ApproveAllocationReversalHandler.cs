using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>
/// The checker approves the reversal of an allocation, and that is the reversal (US-32; BR-305, BR-603, BR-606): the
/// decision on the request, the allocation marked reversed, the payment's unapplied amount and the bill's balance
/// raised by its amount, the bill's status moved back (BR-208), the mirror ledger pair
/// Dr Receivable(bill) / Cr UnappliedCash(payment) and the audit records are one transaction (BR-007). Nothing is
/// deleted, and the original posting is not touched.
/// <para>
/// Order and lock (ADR 0022, ADR 0027): first everything that can refuse, with nothing locked: the version the
/// checker read, the allocation (it waits), the checker (a user, and not the maker, BR-603). Then the payment and the
/// bill take the amount back, the bill through <see cref="IBillSettlement"/>; the ledger, which locks the payer's row
/// of cached balances; and one commit that saves the payment, the bill and the request under their row versions. No
/// document number is taken: a reversal has none.
/// </para>
/// </summary>
public sealed class ApproveAllocationReversalHandler(
    IValidator<ApproveAllocationReversalCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    IPaymentRepository repository,
    IApprovalRequestRepository approvals,
    IBillSettlement bills,
    IReceivablesLedger ledger,
    IUnitOfWork unitOfWork) : ICommandHandler<ApproveAllocationReversalCommand, PaymentAllocationsDto>
{
    /// <inheritdoc />
    public async Task<Result<PaymentAllocationsDto>> HandleAsync(ApproveAllocationReversalCommand command, CancellationToken ct)
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

        var waits = payment.CheckAllocationAwaitsReversal(command.AllocationId);
        if (waits.IsFailure)
        {
            return waits.Error;
        }

        if (currentUser.UserId is not { } checker)
        {
            return CommonErrors.ApprovalNeedsUser();
        }

        // An allocation that waits has exactly one pending request: the same commit made both (BR-605). When it is gone,
        // another decision was committed after the document was read here: a conflict, not a fault (BR-804).
        var pending = await approvals.FindPendingAsync(ApprovalDocumentType.PaymentAllocation, command.AllocationId.Value, ct);
        if (pending.IsFailure)
        {
            return pending.Error;
        }

        var request = pending.Value;

        // One reading of the clock for the use case: the instant of the reversal, and today in India from it (BR-003).
        var now = clock.UtcNow;
        var decided = request.Approve(checker, command.Comment, now);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        var reversed = payment.ReverseAllocation(command.AllocationId, ReversalReasons.Of(request), now, checker);
        if (reversed.IsFailure)
        {
            return reversed.Error;
        }

        var entered = await ReversalPostings.EnterAsync(payment, reversed.Value, clock.ToIstDate(now), bills, ledger, ct);
        if (entered.IsFailure)
        {
            return entered.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? payment.ToDtoWithAllocations() : committed.Error;
    }
}
