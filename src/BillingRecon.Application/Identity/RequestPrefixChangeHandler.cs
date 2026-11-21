using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>
/// A TenantAdmin asks for a new prefix of one document type (BR-802, BR-604; ADR 0033): the change waits for a second
/// person, and its approval request is opened in the same commit; no prefix changes yet. Refused when the type has the
/// prefix already (422), when a change of the type already waits (409), or when the prefix belongs to another kind of
/// document, now or in the past (409); the approval asks the last again.
/// </summary>
public sealed class RequestPrefixChangeHandler(
    IValidator<RequestPrefixChangeCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    ICorrelationContext correlation,
    ITenantRepository tenants,
    IPrefixChangeRepository changes,
    IPrefixUsage usage,
    IApprovalRequestRepository approvals,
    IUnitOfWork unitOfWork) : ICommandHandler<RequestPrefixChangeCommand, PrefixChangeDto>
{
    /// <inheritdoc />
    public async Task<Result<PrefixChangeDto>> HandleAsync(RequestPrefixChangeCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        if (currentUser.UserId is not { } maker)
        {
            return CommonErrors.ApprovalNeedsUser();
        }

        var documentType = command.DocumentType!.Value;
        var prefix = SettingsRules.NormalizePrefix(command.Prefix)!;
        var tenant = await tenants.GetCurrentAsync(ct);
        var from = tenant.PrefixOf(documentType);

        if (from == prefix)
        {
            return IdentityErrors.PrefixUnchanged();
        }

        if (await changes.AnyPendingAsync(documentType, ct))
        {
            return IdentityErrors.PrefixChangePending();
        }

        if (await usage.IsTakenAsync(documentType, prefix, ct))
        {
            return IdentityErrors.PrefixInUse();
        }

        var change = PrefixChange.Request(documentType, from, prefix);
        changes.Add(change);
        approvals.Add(ApprovalRequest.Open(ApprovalDocumentType.PrefixChange, change.Id.Value, ApprovalAction.ChangePrefix, maker, command.Comment, clock.UtcNow, correlation.CorrelationId));

        var committed = await unitOfWork.CommitAsync(ct);
        if (committed.IsFailure)
        {
            // Another request for the type was asked for in the same instant: the filtered unique index answers.
            return committed.Error.Code == CommonErrors.DuplicateKeyCode ? IdentityErrors.PrefixChangePending() : committed.Error;
        }

        return change.ToDto();
    }
}
