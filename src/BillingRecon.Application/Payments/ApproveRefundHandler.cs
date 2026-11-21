using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>
/// The checker approves a refund that waits, and that is the posting (US-34; BR-307, BR-309, BR-603, BR-606): the
/// decision on the request, the gapless number, the source's lower balance, the ledger posting
/// Dr UnappliedCash(payment) or CreditAvailable(credit note) / Cr Cash, and the audit records are one transaction
/// (BR-007).
/// <para>
/// Order and lock (ADR 0022, ADR 0029): first everything that can refuse, with nothing locked: the version the
/// checker read; the source, still usable; the refund, waiting and with a date still allowed; the checker (a user, and
/// not the maker, BR-603); the payer, still active (BR-309); the source, still holding the amount (BR-307). Then the
/// number; the source pays, a payment here and a credit note through <see cref="ICreditNoteSettlement"/>; the
/// ledger, which locks the payer's row of cached balances; one commit that saves the refund, its source and the
/// request under their row versions. A source changed by a request that committed in between answers 409.
/// </para>
/// </summary>
public sealed class ApproveRefundHandler(
    IValidator<ApproveRefundCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    IPaymentRepository payments,
    ICreditNoteSettlement creditNotes,
    IActivePayers payers,
    IRefundRepository repository,
    IApprovalRequestRepository approvals,
    ITenantSettingsReader tenantSettings,
    INumberSeries numbers,
    IReceivablesLedger ledger,
    IUnitOfWork unitOfWork) : ICommandHandler<ApproveRefundCommand, RefundDto>
{
    /// <inheritdoc />
    public async Task<Result<RefundDto>> HandleAsync(ApproveRefundCommand command, CancellationToken ct)
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

        var found = await RefundSources.FindAsync(refund.Source, payments, creditNotes, ct);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var source = found.Value;

        // One reading of the clock for the use case: the instant of the posting, and today in India from it (BR-003).
        var now = clock.UtcNow;
        var settings = await tenantSettings.GetDocumentSettingsAsync(ct);
        var window = new BusinessDateWindow(clock.ToIstDate(now), settings.MaxBackdateDays);
        var allowed = refund.CheckCanApprove(source.Date, window);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        if (currentUser.UserId is not { } checker)
        {
            return CommonErrors.ApprovalNeedsUser();
        }

        // A refund that waits has exactly one pending request: the same commit made both (BR-605). When it is gone,
        // another decision was committed after the refund was read here: a conflict, not a fault (BR-804).
        var pending = await approvals.FindPendingAsync(ApprovalDocumentType.Refund, refund.Id.Value, ct);
        if (pending.IsFailure)
        {
            return pending.Error;
        }

        var decided = pending.Value.Approve(checker, command.Comment, now);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        // Money is paid back only to an active payer (BR-309); the payer may have been deactivated while it waited.
        var active = await payers.EnsureActiveAsync(refund.PayerId, ct);
        if (active.IsFailure)
        {
            return active.Error;
        }

        // The source may have been allocated, applied or refunded while the refund waited (BR-307: the cap holds at posting).
        var fits = refund.CheckFits(source.Number, source.Available);
        if (fits.IsFailure)
        {
            return fits.Error;
        }

        // Last, after every check: from here on the series of the fiscal year is locked until the commit (BR-801).
        var number = await numbers.NextAsync(DocumentType.Refund, FiscalYear.FromDate(refund.RefundDate), ct);
        var posted = refund.PostApproved(number, now, checker, source.Date, window);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        var paid = await PayFromSourceAsync(refund, source, number, ct);
        if (paid.IsFailure)
        {
            return paid.Error;
        }

        // From this posting on, the payer's row of cached balances is locked until the commit.
        var entered = await ledger.PostAsync(paid.Value, ct);
        if (entered.IsFailure)
        {
            // The source accepted the amount as it was read. A ledger that finds a balance going below zero has seen
            // what another request committed in between (BR-804, ADR 0024).
            return LedgerRefusal.IsBalanceBelowZero(entered.Error) ? CommonErrors.ConcurrencyConflict() : entered.Error;
        }

        refund.AttachPosting(entered.Value);

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? refund.ToDto() : committed.Error;
    }

    // The source gives up the amount, and the posting that records it.
    private async Task<Result<PostingTemplate>> PayFromSourceAsync(Refund refund, RefundSourceFacts source, DocumentNumber number, CancellationToken ct)
    {
        if (source.Payment is { } payment)
        {
            var fromPayment = payment.PayRefund(number.Value, refund.Amount);
            if (fromPayment.IsFailure)
            {
                return fromPayment.Error;
            }

            return PostingTemplate.RefundFromPaymentPosted(refund.Id, payment.Id, refund.PayerId, refund.Amount, refund.RefundDate, number, payment.Number);
        }

        var creditNoteId = refund.SourceCreditNoteId ?? throw new InvalidOperationException($"Refund {refund.Id} names neither a payment nor a credit note.");
        var fromCreditNote = await creditNotes.PayRefundAsync(creditNoteId, refund.PayerId, number.Value, refund.Amount, ct);
        if (fromCreditNote.IsFailure)
        {
            return fromCreditNote.Error;
        }

        return PostingTemplate.RefundFromCreditNotePosted(refund.Id, creditNoteId, refund.PayerId, refund.Amount, refund.RefundDate, number, fromCreditNote.Value.Number);
    }
}
