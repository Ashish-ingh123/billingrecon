using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>
/// A checker approves a prefix change, and that is the change (BR-603, BR-606, BR-802; ADR 0033): the decision on the
/// request, the tenant's default prefix, the prefix of the type's series of the current fiscal year (by today's date in
/// India) when one exists, and the audit records are one commit. The checker is not the maker and holds Supervisor or
/// TenantAdmin; the prefix must still belong to no other kind of document. A number taken in the same instant changes
/// the series too, and the row version answers 409: the checker tries again.
/// </summary>
public sealed class ApprovePrefixChangeHandler(
    IValidator<ApprovePrefixChangeCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    IPrefixChangeRepository changes,
    IApprovalRequestRepository approvals,
    ITenantRepository tenants,
    IPrefixUsage usage,
    INumberSeriesRepository series,
    IUnitOfWork unitOfWork) : ICommandHandler<ApprovePrefixChangeCommand, PrefixChangeDto>
{
    /// <inheritdoc />
    public async Task<Result<PrefixChangeDto>> HandleAsync(ApprovePrefixChangeCommand command, CancellationToken ct)
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
        var decided = pending.Value.Approve(checker, currentUser.Roles, command.Comment, now);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        // Asked again: another type may have taken the prefix while this change waited.
        if (await usage.IsTakenAsync(change.DocumentType, change.ToPrefix, ct))
        {
            return IdentityErrors.PrefixInUse();
        }

        var approved = change.RecordApproval(now);
        if (approved.IsFailure)
        {
            return approved.Error;
        }

        var tenant = await tenants.GetCurrentAsync(ct);
        tenant.ChangePrefix(change.DocumentType, change.ToPrefix);

        var current = await series.FindAsync(change.DocumentType, FiscalYear.FromDate(clock.ToIstDate(now)), ct);
        current?.ChangePrefix(change.ToPrefix, now);

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? change.ToDto() : committed.Error;
    }
}
