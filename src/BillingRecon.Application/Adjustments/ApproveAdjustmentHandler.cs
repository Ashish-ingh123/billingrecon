using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Adjustments;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Adjustments;

/// <summary>
/// The checker approves an adjustment that waits for approval, and that is the posting (BR-502, BR-603, BR-606): the
/// decision on the request, the number, the bill's balance and status, the ledger entry Dr Adjustments /
/// Cr Receivable(bill), the payer's balance and the audit records are one transaction (BR-007).
/// <para>
/// The order is that of every posting (ADR 0022, ADR 0026). First everything that can refuse, with nothing locked:
/// the version the checker read, the adjustment (it waits, and its date is still allowed), the checker (a user, and
/// not the maker, BR-603) and the amount against what the bill is owed now (BR-501). Then the number. Then the bill
/// takes the amount, through <see cref="IBillSettlement"/>, the Billing module's contract; the ledger, which locks
/// the payer's row of cached balances; and one commit that saves the adjustment, the bill and the request, each under
/// its row version. A bill that somebody lowered in between fails that commit, or is noticed by the ledger under its
/// lock: both are answered 409 <c>concurrency_conflict</c>, and the number goes back with the rest.
/// </para>
/// </summary>
public sealed class ApproveAdjustmentHandler(
    IValidator<ApproveAdjustmentCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    IBillSettlement bills,
    IAdjustmentRepository repository,
    IApprovalRequestRepository approvals,
    ITenantSettingsReader tenantSettings,
    INumberSeries numbers,
    IReceivablesLedger ledger,
    IUnitOfWork unitOfWork) : ICommandHandler<ApproveAdjustmentCommand, AdjustmentDto>
{
    /// <inheritdoc />
    public async Task<Result<AdjustmentDto>> HandleAsync(ApproveAdjustmentCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        var adjustment = await repository.GetAsync(command.Id, ct);
        if (adjustment is null)
        {
            return AdjustmentsErrors.AdjustmentNotFound();
        }

        if (RowVersionText.Of(adjustment) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        var found = await bills.FindPostedAsync(adjustment.BillId, ct);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var bill = found.Value;
        var now = clock.UtcNow;
        var settings = await tenantSettings.GetDocumentSettingsAsync(ct);
        var window = new BusinessDateWindow(clock.ToIstDate(now), settings.MaxBackdateDays);

        var allowed = adjustment.CheckCanApprove(bill.BillDate, window);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        if (currentUser.UserId is not { } checker)
        {
            return CommonErrors.ApprovalNeedsUser();
        }

        // An adjustment that waits has exactly one pending request: the same commit made both (BR-605). When it is gone,
        // another decision was committed after the document was read here: a conflict, not a fault (BR-804).
        var pending = await approvals.FindPendingAsync(ApprovalDocumentType.Adjustment, adjustment.Id.Value, ct);
        if (pending.IsFailure)
        {
            return pending.Error;
        }

        var request = pending.Value;
        var decided = request.Approve(checker, currentUser.Roles, command.Comment, now);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        // The bill may have been paid or credited while the adjustment waited (BR-501: the cap holds at posting).
        var fits = adjustment.CheckFits(bill.Number, bill.BalanceDue);
        if (fits.IsFailure)
        {
            return fits.Error;
        }

        // Last, after every check: from here on the series of the fiscal year is locked until the commit (BR-801).
        var number = await numbers.NextAsync(DocumentType.Adjustment, FiscalYear.FromDate(adjustment.AdjustmentDate), ct);
        var posted = adjustment.PostApproved(number, now, checker, bill.BillDate, window);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        var settled = await bills.ApplyAsync(adjustment.BillId, adjustment.PayerId, adjustment.Amount, ct);
        if (settled.IsFailure)
        {
            return settled.Error;
        }

        // From this posting on, the payer's row of cached balances is locked until the commit.
        var entered = await ledger.PostAsync(PostingTemplate.AdjustmentPosted(adjustment.Id, adjustment.BillId, adjustment.PayerId, adjustment.Amount, adjustment.AdjustmentDate, number, bill.Number), ct);
        if (entered.IsFailure)
        {
            // The bill accepted the amount as it was read. A ledger that finds a balance going below zero has seen what
            // another request committed in between (BR-804, ADR 0024).
            return LedgerRefusal.IsBalanceBelowZero(entered.Error) ? CommonErrors.ConcurrencyConflict() : entered.Error;
        }

        adjustment.AttachPosting(entered.Value);

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? adjustment.ToDto() : committed.Error;
    }
}
