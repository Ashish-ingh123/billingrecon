using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Receivables;
using FluentValidation;

namespace BillingRecon.Application.Receivables;

/// <summary>
/// The maker submits an opening batch (ADR 0053; BR-603): the files are checked as the dry run checks them, and with
/// one finding nothing is written; files that were submitted before for the same cut-over date, and wait or were
/// approved, are refused; otherwise the batch is kept with its files and an approval request is opened. Nothing is
/// posted: the approval of a second person is the posting.
/// </summary>
public sealed class SubmitOpeningBatchHandler(
    IValidator<SubmitOpeningBatchCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    ICorrelationContext correlation,
    IImportParties parties,
    IOpeningReceivables openingReceivables,
    IOpeningAdvances openingAdvances,
    IOpeningBatchRepository batches,
    IApprovalRequestRepository approvals,
    IUnitOfWork unitOfWork) : ICommandHandler<SubmitOpeningBatchCommand, OpeningBatchDto>
{
    /// <inheritdoc />
    public async Task<Result<OpeningBatchDto>> HandleAsync(SubmitOpeningBatchCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        if (currentUser.UserId is not { } maker)
        {
            return CommonErrors.ApprovalNeedsUser();
        }

        var files = new OpeningBatchFiles(command.CutOverDate!.Value, command.BillsCsv, command.AdvancesCsv, command.TrialBalanceCsv);
        // First, so that files sent a second time are named for what they are: once they were brought in, every row of
        // them would be rejected as brought in before, which says less.
        var hash = files.ContentHash();
        if (await batches.AnyLiveWithHashAsync(hash, ct))
        {
            return ReceivablesErrors.OpeningBatchDuplicate();
        }

        var check = await new OpeningBatchChecker(parties, openingReceivables, openingAdvances).CheckAsync(files, ct);
        if (check.Findings.Count > 0)
        {
            return ReceivablesErrors.OpeningBatchHasRejections(check.Findings.Count);
        }

        var batch = OpeningBatch.Submit(
            files.CutOverDate,
            hash,
            files.BillsCsv ?? string.Empty,
            files.AdvancesCsv ?? string.Empty,
            files.TrialBalanceCsv ?? string.Empty,
            check.Receivables.Count,
            check.ReceivableTotal,
            check.Advances.Count,
            check.AdvanceTotal);
        batches.Add(batch);
        approvals.Add(ApprovalRequest.Open(ApprovalDocumentType.OpeningBatch, batch.Id.Value, ApprovalAction.BringForward, maker, command.Comment, clock.UtcNow, correlation.CorrelationId));

        var committed = await unitOfWork.CommitAsync(ct);
        if (committed.IsFailure)
        {
            // The same files were submitted in the same instant: the filtered unique index answers.
            return committed.Error.Code == CommonErrors.DuplicateKeyCode ? ReceivablesErrors.OpeningBatchDuplicate() : committed.Error;
        }

        return batch.ToDto();
    }
}
