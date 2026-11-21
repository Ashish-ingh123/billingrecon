using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Application.Payments;

/// <summary>
/// The maker's <c>post</c> of a refund (BR-604): every refund needs a second person, so this hands the draft over and
/// opens the approval request in the same commit; nothing financial happens. The source is asked first whether it
/// still has the amount (BR-307), for an early 422; the checker's approval asks again.
/// </summary>
public sealed class PostRefundHandler(
    IClock clock,
    ICurrentUser currentUser,
    ICorrelationContext correlation,
    IPaymentRepository payments,
    ICreditNoteSettlement creditNotes,
    IRefundRepository repository,
    IApprovalRequestRepository approvals,
    ITenantSettingsReader tenantSettings,
    IUnitOfWork unitOfWork) : ICommandHandler<PostRefundCommand, RefundDto>
{
    /// <inheritdoc />
    public async Task<Result<RefundDto>> HandleAsync(PostRefundCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var refund = await repository.GetAsync(command.Id, ct);
        if (refund is null)
        {
            return PaymentsErrors.RefundNotFound();
        }

        if (RowVersionText.Of(refund) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        var editable = refund.CheckIsDraft();
        if (editable.IsFailure)
        {
            return editable.Error;
        }

        var found = await RefundSources.FindAsync(refund.Source, payments, creditNotes, ct);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var source = found.Value;
        var fits = refund.CheckFits(source.Number, source.Available);
        if (fits.IsFailure)
        {
            return fits.Error;
        }

        if (currentUser.UserId is not { } maker)
        {
            return CommonErrors.ApprovalNeedsUser();
        }

        // One reading of the clock for the use case: the instant of the request, and today in India from it (BR-003).
        var now = clock.UtcNow;
        var settings = await tenantSettings.GetDocumentSettingsAsync(ct);
        var submitted = refund.SubmitForApproval(source.Date, new BusinessDateWindow(clock.ToIstDate(now), settings.MaxBackdateDays));
        if (submitted.IsFailure)
        {
            return submitted.Error;
        }

        approvals.Add(ApprovalRequest.Open(ApprovalDocumentType.Refund, refund.Id.Value, ApprovalAction.Post, maker, makerComment: null, now, correlation.CorrelationId));

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? refund.ToDto() : committed.Error;
    }
}
