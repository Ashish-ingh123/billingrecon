using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>
/// A checker rejects a prefix change (BR-603, BR-605; ADR 0033): the request is closed with the checker's comment and
/// the change is rejected, in one commit. No prefix changes.
/// </summary>
public sealed class RejectPrefixChangeHandler(
    IValidator<RejectPrefixChangeCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    IPrefixChangeRepository changes,
    IApprovalRequestRepository approvals,
    IUnitOfWork unitOfWork) : ICommandHandler<RejectPrefixChangeCommand, PrefixChangeDto>
{
    /// <inheritdoc />
    public async Task<Result<PrefixChangeDto>> HandleAsync(RejectPrefixChangeCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        var change = await changes.GetAsync(command.Id, ct);
        if (change is null)
        {
            return IdentityErrors.PrefixChangeNotFound();
        }

        if (RowVersionText.Of(change) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        var pendingChange = change.CheckIsPending();
        if (pendingChange.IsFailure)
        {
            return pendingChange.Error;
        }

        if (currentUser.UserId is not { } checker)
        {
            return CommonErrors.ApprovalNeedsUser();
        }

        var pending = await approvals.FindPendingAsync(ApprovalDocumentType.PrefixChange, change.Id.Value, ct);
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

        var rejected = change.RecordRejection(command.Comment!, now);
        if (rejected.IsFailure)
        {
            return rejected.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? change.ToDto() : committed.Error;
    }
}
