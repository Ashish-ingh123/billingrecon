using BillingRecon.Application.Adjustments;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>
/// The checker approves a credit note that waits for approval, and that is the posting (US-24; BR-212, BR-603,
/// BR-606): the decision on the request, the number, two ledger postings, the payer's balances, the bill's balance
/// and status, and the audit records are one transaction (BR-007).
/// <para>
/// The order is that of every posting (ADR 0022, ADR 0025). First everything that can refuse, with nothing locked:
/// the version the checker read, the credit note (it waits, and its date is still allowed), the checker (a user, and
/// not the maker, BR-603) and the caps against the bill (BR-210, BR-211). Then the number. Then the ledger:
/// Dr Revenue / Cr CreditAvailable(credit note) for the total, which takes the update lock on the payer's row of
/// cached balances and keeps it to the commit. Under that lock the caps are measured once more, because another
/// credit note of the same bill may have been approved since they were read: one that no longer fits is answered
/// 409 <c>concurrency_conflict</c>, and the number goes back with the rest.
/// </para>
/// <para>
/// Then the credit is applied to its own bill as far as the bill is owed, <c>min(Total, BalanceDue)</c>, with the
/// paired posting Dr CreditAvailable(credit note) / Cr Receivable(bill); the rest stays available (BR-212, BR-213). A
/// bill that is owed nothing gets nothing applied. One commit saves the credit note, the bill and the request, each
/// under its row version.
/// </para>
/// </summary>
public sealed class ApproveCreditNoteHandler(
    IValidator<ApproveCreditNoteCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    IBillRepository bills,
    ICreditNoteRepository repository,
    IBillAdjustments adjustments,
    IApprovalRequestRepository approvals,
    ITenantSettingsReader tenantSettings,
    INumberSeries numbers,
    IReceivablesLedger ledger,
    IUnitOfWork unitOfWork) : ICommandHandler<ApproveCreditNoteCommand, CreditNoteDto>
{
    /// <inheritdoc />
    public async Task<Result<CreditNoteDto>> HandleAsync(ApproveCreditNoteCommand command, CancellationToken ct)
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

        var now = clock.UtcNow;
        var today = clock.ToIstDate(now);
        var settings = await tenantSettings.GetDocumentSettingsAsync(ct);
        var window = new BusinessDateWindow(today, settings.MaxBackdateDays);
        var bill = await bills.GetOfAsync(creditNote, ct);

        var allowed = creditNote.CheckCanApprove(bill.BillDate, window);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        if (currentUser.UserId is not { } checker)
        {
            return CommonErrors.ApprovalNeedsUser();
        }

        // A credit note that waits has exactly one pending request: the same commit made both (BR-605). When it is gone,
        // another decision was committed after the document was read here: a conflict, not a fault (BR-804).
        var pending = await approvals.FindPendingAsync(ApprovalDocumentType.CreditNote, creditNote.Id.Value, ct);
        if (pending.IsFailure)
        {
            return pending.Error;
        }

        var request = pending.Value;
        // Who wrote the document, or changed it last, does not approve it either (BR-603; review 2026-11, R9).
        var decided = request.Approve(checker, currentUser.Roles, command.Comment, now, creditNote.CreatedByUserId, creditNote.UpdatedByUserId);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        var withinBill = await CreditNoteCaps.CheckAsync(repository, adjustments, creditNote, bill, ct);
        if (withinBill.IsFailure)
        {
            return withinBill.Error;
        }

        // Last, after every check: from here on the series of the fiscal year is locked until the commit (BR-801).
        var number = await numbers.NextAsync(DocumentType.CreditNote, FiscalYear.FromDate(creditNote.CreditNoteDate), ct);
        var posted = creditNote.PostApproved(number, now, checker, bill.BillDate, window);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        // From this posting on, the payer's row of cached balances is locked until the commit.
        var entered = await ledger.PostAsync(PostingTemplate.CreditNotePosted(creditNote.Id, creditNote.PayerId, creditNote.Total, creditNote.CreditNoteDate, number), ct);
        if (entered.IsFailure)
        {
            return entered.Error;
        }

        // Every approval of a credit note of this payer waits for that lock, so what is read now is what holds at
        // the commit. A cap that fitted a moment ago and no longer does was taken by a request that committed first.
        var stillWithinBill = await CreditNoteCaps.CheckAsync(repository, adjustments, creditNote, bill, ct);
        if (stillWithinBill.IsFailure)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        var toOwnBill = creditNote.Total <= bill.BalanceDue ? creditNote.Total : bill.BalanceDue;
        if (toOwnBill.IsPositive)
        {
            var settled = bill.ApplyCredit(toOwnBill);
            if (settled.IsFailure)
            {
                return settled.Error;
            }

            var applied = creditNote.ApplyToOwnBillAtPosting(toOwnBill, now, checker);
            if (applied.IsFailure)
            {
                return applied.Error;
            }

            // Adding the application gives it its id, which its ledger posting names as its document.
            repository.Add(applied.Value);

            // A posted bill has its number; the draft could not have been written against one that has none.
            var template = PostingTemplate.CreditApplied(applied.Value.Id, creditNote.Id, bill.Id, creditNote.PayerId, toOwnBill, today, number.Value, bill.Number ?? string.Empty);
            var paired = await ledger.PostAsync(template, ct);
            if (paired.IsFailure)
            {
                // The bill accepted the amount as it was read. A ledger that finds a balance going below zero has seen
                // what another request committed in between (BR-804, ADR 0024).
                return LedgerRefusal.IsBalanceBelowZero(paired.Error) ? CommonErrors.ConcurrencyConflict() : paired.Error;
            }

            creditNote.AttachApplicationPosting(applied.Value, paired.Value);
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? creditNote.ToDto() : committed.Error;
    }
}
