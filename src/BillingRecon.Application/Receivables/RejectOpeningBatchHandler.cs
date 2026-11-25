using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Receivables;
using FluentValidation;

namespace BillingRecon.Application.Receivables;

/// <summary>
/// The checker rejects an opening batch (ADR 0053; BR-603), saying why: nothing of it is posted, and the same files may
/// be submitted again once they are right.
/// </summary>
public sealed class RejectOpeningBatchHandler(
    IValidator<RejectOpeningBatchCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    IOpeningBatchRepository batches,
    IApprovalRequestRepository approvals,
    IUnitOfWork unitOfWork) : ICommandHandler<RejectOpeningBatchCommand, OpeningBatchDto>
{
    /// <inheritdoc />
    public async Task<Result<OpeningBatchDto>> HandleAsync(RejectOpeningBatchCommand command, CancellationToken ct)
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
        var decided = pending.Value.Reject(checker, currentUser.Roles, command.Comment!, now);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        var rejected = batch.RecordRejection(command.Comment!, now);
        if (rejected.IsFailure)
        {
            return rejected.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? batch.ToDto() : committed.Error;
    }
}
