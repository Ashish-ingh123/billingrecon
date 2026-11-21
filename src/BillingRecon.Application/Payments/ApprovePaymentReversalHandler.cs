using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>
/// The checker approves the reversal of a payment, and that is the reversal (US-33; BR-306, BR-603, BR-606). Every
/// allocation that is still applied is reversed first, each with its mirror pair and its bill raised again (BR-305);
/// then the payment itself, with Dr UnappliedCash(payment) / Cr Cash for its whole amount. Afterwards the payment
/// holds nothing and every balance is where it was before the payment was recorded. All of it, with the decision on
/// the request and the audit records, is one transaction (BR-007).
/// <para>
/// Order and lock (ADR 0022, ADR 0027): the version the checker read, the payment (it waits), the checker (a user,
/// and not the maker); then the bills, in the order of their ids, through <see cref="IBillSettlement"/>; the ledger,
/// whose first posting locks the payer's row of cached balances; and one commit that saves the payment, every bill
/// and the request under their row versions. That a refund was posted from the payment, which BR-306 says forbids
/// its reversal, is checked with refunds (Prompt 4.6); until then nothing can post one.
/// </para>
/// </summary>
public sealed class ApprovePaymentReversalHandler(
    IValidator<ApprovePaymentReversalCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    IPaymentRepository repository,
    IApprovalRequestRepository approvals,
    IBillSettlement bills,
    IReceivablesLedger ledger,
    IUnitOfWork unitOfWork) : ICommandHandler<ApprovePaymentReversalCommand, PaymentAllocationsDto>
{
    /// <inheritdoc />
    public async Task<Result<PaymentAllocationsDto>> HandleAsync(ApprovePaymentReversalCommand command, CancellationToken ct)
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

        var waits = payment.CheckAwaitsReversal();
        if (waits.IsFailure)
        {
            return waits.Error;
        }

        if (currentUser.UserId is not { } checker)
        {
            return CommonErrors.ApprovalNeedsUser();
        }

        // A payment that waits has exactly one pending request: the same commit made both (BR-605). When it is gone,
        // another decision was committed after the document was read here: a conflict, not a fault (BR-804).
        var pending = await approvals.FindPendingAsync(ApprovalDocumentType.Payment, payment.Id.Value, ct);
        if (pending.IsFailure)
        {
            return pending.Error;
        }

        var request = pending.Value;

        var now = clock.UtcNow;
        var today = clock.ToIstDate(now);
        var decided = request.Approve(checker, currentUser.Roles, command.Comment, now);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        var reversed = payment.Reverse(ReversalReasons.Of(request), now, checker);
        if (reversed.IsFailure)
        {
            return reversed.Error;
        }

        // The allocations first: each mirror pair gives the payment its cash back, so that the last posting can take
        // the whole amount out (BR-306). The first posting locks the payer's balances until the commit.
        foreach (var allocation in reversed.Value)
        {
            var mirrored = await ReversalPostings.EnterAsync(payment, allocation, today, bills, ledger, ct);
            if (mirrored.IsFailure)
            {
                return mirrored.Error;
            }
        }

        var entered = await ledger.PostAsync(PostingTemplate.PaymentReversed(payment.Id, payment.PayerId, payment.Amount, today, payment.Number, payment.PostingId), ct);
        if (entered.IsFailure)
        {
            // The payment had its whole amount unapplied as it was read. A ledger that finds a balance going below
            // zero has seen what another request committed in between (BR-804, ADR 0024).
            return LedgerRefusal.IsBalanceBelowZero(entered.Error) ? CommonErrors.ConcurrencyConflict() : entered.Error;
        }

        payment.AttachReversalPosting(entered.Value);

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? payment.ToDtoWithAllocations() : committed.Error;
    }
}
