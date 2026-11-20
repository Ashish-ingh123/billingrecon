using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Posts a draft bill (US-21, US-25; BR-206, BR-006, BR-007). Everything that can refuse the posting is asked first:
/// the version the client read (BR-804), the bill itself (a draft, with a line, whose date is still allowed today,
/// BR-205), its payer (active, BR-105) and the tenant's approval threshold (BR-207). Only then is the number taken,
/// so a refusal never touches the series (BR-801). After that, in this order and in one transaction: the bill is
/// posted under the number, the ledger gets Dr Receivable(bill) / Cr Revenue and the payer's balance moves, and the
/// commit saves all of it with the audit record. If anything fails, the number goes back with the rest.
/// <para>
/// Lock order (ADR 0022): the number series, then the balances of the payer, both before the save; the row of the
/// bill is written by the save, under its row version. A second posting of the same bill waits
/// on the series and then loses on the row version of the bill: 409, and its number is rolled back.
/// </para>
/// <para>
/// A bill at or above the threshold is refused with <c>bill_requires_approval</c> until maker-checker exists
/// (Prompt 3.6); a bill that comes to nothing is settled at once and has no ledger posting, because a line of zero
/// is not an entry (ADR 0022).
/// </para>
/// </summary>
public sealed class PostBillHandler(
    IClock clock,
    ICurrentUser currentUser,
    IBillRepository repository,
    ITenantSettingsReader tenantSettings,
    IBillableParties parties,
    INumberSeries numbers,
    IReceivablesLedger ledger,
    IUnitOfWork unitOfWork) : ICommandHandler<PostBillCommand, BillDto>
{
    /// <inheritdoc />
    public async Task<Result<BillDto>> HandleAsync(PostBillCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var bill = await repository.GetAsync(command.Id, ct);
        if (bill is null)
        {
            return BillingErrors.BillNotFound();
        }

        if (RowVersionText.Of(bill) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        // One reading of the clock for the use case: the instant of the posting, and today in India from it (BR-003).
        var now = clock.UtcNow;
        var settings = await tenantSettings.GetDocumentSettingsAsync(ct);
        var window = new BusinessDateWindow(clock.ToIstDate(now), settings.MaxBackdateDays);

        var allowed = bill.CheckCanPost(window);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var payer = await parties.EnsureCanOweAsync(bill.PayerId, ct);
        if (payer.IsFailure)
        {
            return payer.Error;
        }

        if (settings.PostingApprovalThreshold is { } threshold && bill.Total >= threshold)
        {
            return BillingErrors.BillRequiresApproval();
        }

        // Last, after every check: from here on the series of the fiscal year is locked until the commit (BR-801).
        var number = await numbers.NextAsync(DocumentType.Bill, FiscalYear.FromDate(bill.BillDate), ct);

        var posted = bill.Post(number, now, currentUser.UserId, window);
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
