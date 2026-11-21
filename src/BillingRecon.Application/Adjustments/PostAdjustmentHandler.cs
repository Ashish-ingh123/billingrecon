using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Adjustments;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Adjustments;

/// <summary>
/// The maker's half of an adjustment (BR-502, BR-604): nothing is posted. Every adjustment is handed to a second
/// person, a rounding of a few paise included: it becomes <c>PendingApproval</c>, and an approval request with the
/// current user as its maker is opened in the same commit. No number, no ledger entry, no balance: the checker's
/// approval is the posting (BR-606, <see cref="ApproveAdjustmentHandler"/>).
/// <para>
/// What can refuse is asked first: the version the client read (BR-804), the adjustment (a draft whose date is still
/// allowed) and the amount against what the bill is owed today (BR-501). The approval asks the bill again.
/// </para>
/// </summary>
public sealed class PostAdjustmentHandler(
    IClock clock,
    ICurrentUser currentUser,
    ICorrelationContext correlation,
    IBillSettlement bills,
    IAdjustmentRepository repository,
    IApprovalRequestRepository approvals,
    ITenantSettingsReader tenantSettings,
    IUnitOfWork unitOfWork) : ICommandHandler<PostAdjustmentCommand, AdjustmentDto>
{
    /// <inheritdoc />
    public async Task<Result<AdjustmentDto>> HandleAsync(PostAdjustmentCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var adjustment = await repository.GetAsync(command.Id, ct);
        if (adjustment is null)
        {
            return AdjustmentsErrors.AdjustmentNotFound();
        }

        if (RowVersionText.Of(adjustment) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        var editable = adjustment.CheckIsDraft();
        if (editable.IsFailure)
        {
            return editable.Error;
        }

        var bill = await bills.FindPostedAsync(adjustment.BillId, ct);
        if (bill.IsFailure)
        {
            return bill.Error;
        }

        var fits = adjustment.CheckFits(bill.Value.Number, bill.Value.BalanceDue);
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

        var submitted = adjustment.SubmitForApproval(bill.Value.BillDate, new BusinessDateWindow(clock.ToIstDate(now), settings.MaxBackdateDays));
        if (submitted.IsFailure)
        {
            return submitted.Error;
        }

        approvals.Add(ApprovalRequest.Open(ApprovalDocumentType.Adjustment, adjustment.Id.Value, ApprovalAction.Post, maker, makerComment: null, now, correlation.CorrelationId));

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? adjustment.ToDto() : committed.Error;
    }
}
