using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>
/// The checker approves a bill that waits for approval, and that is the posting (US-22; BR-603, BR-606): the decision
/// on the request, the number, the ledger entry, the payer's balance, the frozen bill and the audit records are one
/// transaction (BR-007). It lives with the bill, not with the request: Common knows no document (ADR 0003).
/// <para>
/// The order is that of every posting (ADR 0022). First everything that can refuse, with nothing locked: the version
/// the checker read, the bill (it waits, and its date is still allowed today, BR-205), the checker (a user, not the
/// maker, and a Supervisor or TenantAdmin, BR-603) and the payer (active, BR-105). Then the number, the ledger, and one
/// commit that saves the bill and its request together.
/// </para>
/// </summary>
public sealed class ApproveBillPostingHandler(
    IValidator<ApproveBillPostingCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    IBillRepository repository,
    IApprovalRequestRepository approvals,
    ITenantSettingsReader tenantSettings,
    IBillableParties parties,
    INumberSeries numbers,
    IReceivablesLedger ledger,
    IUnitOfWork unitOfWork) : ICommandHandler<ApproveBillPostingCommand, BillDto>
{
    /// <inheritdoc />
    public async Task<Result<BillDto>> HandleAsync(ApproveBillPostingCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        var bill = await repository.GetAsync(command.Id, ct);
        if (bill is null)
        {
            return BillingErrors.BillNotFound();
        }

        if (RowVersionText.Of(bill) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        var now = clock.UtcNow;
        var settings = await tenantSettings.GetDocumentSettingsAsync(ct);
        var window = new BusinessDateWindow(clock.ToIstDate(now), settings.MaxBackdateDays);

        var allowed = bill.CheckCanApprove(window);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        if (currentUser.UserId is not { } checker)
        {
            return CommonErrors.ApprovalNeedsUser();
        }

        // A bill that waits has exactly one pending request: the same commit made both (BR-605). When it is gone,
        // another decision was committed after the document was read here: a conflict, not a fault (BR-804).
        var pending = await approvals.FindPendingAsync(ApprovalDocumentType.Bill, bill.Id.Value, ct);
        if (pending.IsFailure)
        {
            return pending.Error;
        }

        var request = pending.Value;

        // Who wrote the document, or changed it last, does not approve it either (BR-603; review 2026-11, R9).
        var decided = request.Approve(checker, currentUser.Roles, command.Comment, now, bill.CreatedByUserId, bill.UpdatedByUserId);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        var payer = await parties.EnsureCanOweAsync(bill.PayerId, ct);
        if (payer.IsFailure)
        {
            return payer.Error;
        }

        // Last, after every check: from here on the series of the fiscal year is locked until the commit (BR-801).
        var number = await numbers.NextAsync(DocumentType.Bill, FiscalYear.FromDate(bill.BillDate), ct);

        var posted = bill.PostApproved(number, now, checker, window);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        if (bill.Total.IsPositive)
        {
            var entered = await ledger.PostAsync(PostingTemplate.BillPosted(bill.Id, bill.PayerId, bill.Total, bill.BillDate, number), ct);
            if (entered.IsFailure)
            {
                return entered.Error;
            }
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? bill.ToDto() : committed.Error;
    }
}
