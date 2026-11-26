using BillingRecon.Application.Adjustments;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// The maker's half of a credit note (US-24; BR-212, BR-604): nothing is posted. Every credit note is handed to a
/// second person, whatever its total: it becomes <c>PendingApproval</c>, and an approval request with the current user
/// as its maker is opened in the same commit. No number, no ledger entry, no balance: the checker's approval is the
/// posting (BR-606, <see cref="ApproveCreditNoteHandler"/>).
/// <para>
/// What can refuse is asked first: the version the client read (BR-804), the credit note itself (a draft, with a line
/// and a total above zero, whose date is still allowed, BR-205 and BR-214), and the caps against the bill and its
/// lines (BR-210, BR-211), measured against the credit notes of the bill that are posted today. The approval measures
/// them again.
/// </para>
/// </summary>
public sealed class PostCreditNoteHandler(
    IClock clock,
    ICurrentUser currentUser,
    ICorrelationContext correlation,
    IBillRepository bills,
    ICreditNoteRepository repository,
    IBillAdjustments adjustments,
    IApprovalRequestRepository approvals,
    ITenantSettingsReader tenantSettings,
    IUnitOfWork unitOfWork) : ICommandHandler<PostCreditNoteCommand, CreditNoteDto>
{
    /// <inheritdoc />
    public async Task<Result<CreditNoteDto>> HandleAsync(PostCreditNoteCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var creditNote = await repository.GetAsync(command.Id, ct);
        if (creditNote is null)
        {
            return BillingErrors.CreditNoteNotFound();
        }

        if (RowVersionText.Of(creditNote) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        // One reading of the clock for the use case: the instant of the request, and today in India from it (BR-003).
        var now = clock.UtcNow;
        var settings = await tenantSettings.GetDocumentSettingsAsync(ct);
        var window = new BusinessDateWindow(clock.ToIstDate(now), settings.MaxBackdateDays);
        var bill = await bills.GetOfAsync(creditNote, ct);

        var allowed = creditNote.CheckCanSubmit(bill.BillDate, window);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var withinBill = await CreditNoteCaps.CheckAsync(repository, adjustments, creditNote, bill, ct);
        if (withinBill.IsFailure)
        {
            return withinBill.Error;
        }

        if (currentUser.UserId is not { } maker)
        {
            return CommonErrors.ApprovalNeedsUser();
        }

        var submitted = creditNote.SubmitForApproval(bill.BillDate, window);
        if (submitted.IsFailure)
        {
            return submitted.Error;
        }

        approvals.Add(ApprovalRequest.Open(ApprovalDocumentType.CreditNote, creditNote.Id.Value, ApprovalAction.Post, maker, makerComment: null, now, correlation.CorrelationId));

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? creditNote.ToDto() : committed.Error;
    }
}
