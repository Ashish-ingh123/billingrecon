using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Receivables;
using FluentValidation;

namespace BillingRecon.Application.Receivables;

/// <summary>
/// The checker approves an opening batch (ADR 0053; BR-603, BR-801, BR-401): a second user, a Supervisor or TenantAdmin,
/// decides; the kept files are checked again against the tenant as it is now, and one finding refuses the approval with
/// nothing written; then every opening receivable and advance takes the next number of its series in the fiscal year
/// of the cut-over, is added, and is posted to the ledger against <c>OpeningBalances</c>, dated the day before the
/// cut-over. One commit writes the batch, its decision and all of its documents, or none of them.
/// </summary>
public sealed class ApproveOpeningBatchHandler(
    IValidator<ApproveOpeningBatchCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    IOpeningBatchRepository batches,
    IApprovalRequestRepository approvals,
    IImportParties parties,
    IOpeningReceivables openingReceivables,
    IOpeningAdvances openingAdvances,
    INumberSeries numbers,
    IReceivablesLedger ledger,
    IUnitOfWork unitOfWork) : ICommandHandler<ApproveOpeningBatchCommand, OpeningBatchDto>
{
    /// <inheritdoc />
    public async Task<Result<OpeningBatchDto>> HandleAsync(ApproveOpeningBatchCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        var batch = await batches.GetAsync(command.Id, ct);
        if (batch is null)
        {
            return ReceivablesErrors.OpeningBatchNotFound();
        }

        if (RowVersionText.Of(batch) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        var waiting = batch.CheckIsPending();
        if (waiting.IsFailure)
        {
            return waiting.Error;
        }

        if (currentUser.UserId is not { } checker)
        {
            return CommonErrors.ApprovalNeedsUser();
        }

        var pending = await approvals.FindPendingAsync(ApprovalDocumentType.OpeningBatch, batch.Id.Value, ct);
        if (pending.IsFailure)
        {
            return pending.Error;
        }

        var now = clock.UtcNow;
        var decided = pending.Value.Approve(checker, currentUser.Roles, command.Comment, now);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        // Asked again: a patient may have been deactivated, or a number brought in, while the batch waited.
        var files = new OpeningBatchFiles(batch.CutOverDate, batch.BillsCsv, batch.AdvancesCsv, batch.TrialBalanceCsv);
        var check = await new OpeningBatchChecker(parties, openingReceivables, openingAdvances).CheckAsync(files, ct);
        if (check.Findings.Count > 0)
        {
            return ReceivablesErrors.OpeningBatchHasRejections(check.Findings.Count);
        }

        var approved = batch.RecordApproval(now);
        if (approved.IsFailure)
        {
            return approved.Error;
        }

        // Last, after every check: from here on the two opening series are locked until the commit (BR-801).
        var fiscalYear = FiscalYear.FromDate(batch.CutOverDate);
        var closingDate = batch.CutOverDate.AddDays(-1);
        foreach (var receivable in check.Receivables)
        {
            var number = await numbers.NextAsync(DocumentType.OpeningReceivable, fiscalYear, ct);
            var billId = openingReceivables.BringForward(receivable, number, now, checker);
            var entered = await ledger.PostAsync(PostingTemplate.OpeningReceivableBroughtForward(billId, receivable.PayerId, receivable.Outstanding, closingDate, number), ct);
            if (entered.IsFailure)
            {
                return entered.Error;
            }
        }

        foreach (var advance in check.Advances)
        {
            var number = await numbers.NextAsync(DocumentType.OpeningAdvance, fiscalYear, ct);
            var paymentId = openingAdvances.BringForward(advance, number);
            var entered = await ledger.PostAsync(PostingTemplate.OpeningAdvanceBroughtForward(paymentId, advance.PayerId, advance.Amount, closingDate, number), ct);
            if (entered.IsFailure)
            {
                return entered.Error;
            }

            openingAdvances.AttachPosting(paymentId, entered.Value);
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? batch.ToDto() : committed.Error;
    }
}
