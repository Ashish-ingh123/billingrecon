using BillingRecon.Application.Common;
using BillingRecon.Domain.Adjustments;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Adjustments;

/// <summary>
/// The checker rejects an adjustment that waits for approval (BR-603, BR-605): the request is closed with the
/// checker's comment and the adjustment is a draft again, in one commit. Nothing financial happens.
/// </summary>
public sealed class RejectAdjustmentHandler(
    IValidator<RejectAdjustmentCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    IAdjustmentRepository repository,
    IApprovalRequestRepository approvals,
    IUnitOfWork unitOfWork) : ICommandHandler<RejectAdjustmentCommand, AdjustmentDto>
{
    /// <inheritdoc />
    public async Task<Result<AdjustmentDto>> HandleAsync(RejectAdjustmentCommand command, CancellationToken ct)
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

        if (currentUser.UserId is not { } checker)
        {
            return CommonErrors.ApprovalNeedsUser();
        }

        // The adjustment first: one that does not wait has no request to look for.
        var returned = adjustment.ReturnToDraft(command.Comment);
        if (returned.IsFailure)
        {
            return returned.Error;
        }

        var request = await approvals.GetPendingAsync(ApprovalDocumentType.Adjustment, adjustment.Id.Value, ct)
            ?? throw new InvalidOperationException($"Adjustment {adjustment.Id} waits for approval and has no pending approval request.");
        var decided = request.Reject(checker, command.Comment, clock.UtcNow);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? adjustment.ToDto() : committed.Error;
    }
}
