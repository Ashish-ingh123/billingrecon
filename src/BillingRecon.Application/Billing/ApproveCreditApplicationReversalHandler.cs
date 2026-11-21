using BillingRecon.Application.Common;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>
/// The checker approves the reversal of an application of credit, and that is the reversal (ADR 0028; BR-603,
/// BR-606): the decision on the request, the application marked reversed, the credit note's available amount and the
/// bill's balance raised by its amount, the bill's status moved back (BR-208), the mirror ledger pair
/// Dr Receivable(bill) / Cr CreditAvailable(credit note) and the audit records are one transaction (BR-007). Nothing
/// is deleted, the original posting is not touched, and no document number is taken.
/// <para>
/// Order and lock (ADR 0022, ADR 0027): first everything that can refuse, with nothing locked: the version the
/// checker read, the application (it waits), the checker (a user, and not the maker, BR-603). Then the credit note and
/// the bill take the amount back, the bill through <see cref="IBillSettlement"/>; the ledger, which locks the payer's
/// row of cached balances; and one commit that saves the credit note, the bill and the request under their row
/// versions. A decision committed by another checker in between is answered 409 (ADR 0027, point 9).
/// </para>
/// </summary>
public sealed class ApproveCreditApplicationReversalHandler(
    IValidator<ApproveCreditApplicationReversalCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    ICreditNoteRepository repository,
    IApprovalRequestRepository approvals,
    IBillSettlement bills,
    IReceivablesLedger ledger,
    IUnitOfWork unitOfWork) : ICommandHandler<ApproveCreditApplicationReversalCommand, CreditNoteDto>
{
    /// <inheritdoc />
    public async Task<Result<CreditNoteDto>> HandleAsync(ApproveCreditApplicationReversalCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        var creditNote = await repository.GetAsync(command.Id, ct);
        if (creditNote is null)
        {
            return BillingErrors.CreditNoteNotFound();
        }

        if (RowVersionText.Of(creditNote) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        var waits = creditNote.CheckApplicationAwaitsReversal(command.ApplicationId);
        if (waits.IsFailure)
        {
            return waits.Error;
        }

        if (currentUser.UserId is not { } checker)
        {
            return CommonErrors.ApprovalNeedsUser();
        }

        // An application that waits has exactly one pending request: the same commit made both (BR-605). When it is
        // gone, another decision was committed after the credit note was read here: a conflict, not a fault (BR-804).
        var pending = await approvals.FindPendingAsync(ApprovalDocumentType.CreditNoteAllocation, command.ApplicationId.Value, ct);
        if (pending.IsFailure)
        {
            return pending.Error;
        }

        var request = pending.Value;

        // One reading of the clock for the use case: the instant of the reversal, and today in India from it (BR-003).
        var now = clock.UtcNow;
        var decided = request.Approve(checker, command.Comment, now);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        // The reason is the cashier's, kept by the request as its comment; it reaches the row only now (ADR 0028).
        var reason = request.MakerComment
            ?? throw new InvalidOperationException($"Approval request {request.Id} asks for a reversal and carries no reason.");
        var reversed = creditNote.ReverseApplication(command.ApplicationId, reason, now, checker);
        if (reversed.IsFailure)
        {
            return reversed.Error;
        }

        var application = reversed.Value;
        var restored = await bills.RestoreAsync(application.BillId, creditNote.PayerId, application.Amount, ct);
        if (restored.IsFailure)
        {
            return restored.Error;
        }

        // A posted credit note has its number; only a posted one has applications.
        var template = PostingTemplate.CreditApplicationReversed(
            application.Id,
            creditNote.Id,
            application.BillId,
            creditNote.PayerId,
            application.Amount,
            clock.ToIstDate(now),
            creditNote.Number ?? string.Empty,
            restored.Value.Number,
            application.PostingId);
        var entered = await ledger.PostAsync(template, ct);
        if (entered.IsFailure)
        {
            return entered.Error;
        }

        creditNote.AttachApplicationReversalPosting(application, entered.Value);

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? creditNote.ToDto() : committed.Error;
    }
}
