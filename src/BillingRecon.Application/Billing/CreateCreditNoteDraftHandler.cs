using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Creates a draft credit note against a posted bill (US-24, BR-210). The bill must exist for the tenant and be
/// posted; the credit note takes its payer and its patient from it. The date defaults to today in India and must be
/// allowed today and not before the bill date (BR-205, BR-214). A line that names a line of the bill takes its
/// defaults from it (BR-211). A draft has no number and no ledger effect, so nothing but the credit note is written;
/// the caps of BR-210 and BR-211 are checked when it is submitted.
/// </summary>
public sealed class CreateCreditNoteDraftHandler(
    IValidator<CreateCreditNoteDraftCommand> validator,
    ITenantSettingsReader settings,
    IClock clock,
    IBillRepository bills,
    ICreditNoteRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<CreateCreditNoteDraftCommand, CreditNoteDto>
{
    /// <inheritdoc />
    public async Task<Result<CreditNoteDto>> HandleAsync(CreateCreditNoteDraftCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        var bill = await bills.GetAsync(command.BillId, ct);
        if (bill is null)
        {
            return BillingErrors.BillNotFound();
        }

        if (bill.Number is null)
        {
            return BillingErrors.BillNotPosted();
        }

        var lines = CreditNoteLineResolver.Resolve(bill, command.Lines);
        if (lines.IsFailure)
        {
            return lines.Error;
        }

        // One reading of the clock for the use case (BR-003).
        var today = clock.TodayIst;
        var dates = await settings.GetDocumentSettingsAsync(ct);

        var created = CreditNote.Create(
            bill.Id,
            bill.PayerId,
            bill.PatientId,
            bill.BillDate,
            command.CreditNoteDate ?? today,
            command.Reason,
            lines.Value,
            new BusinessDateWindow(today, dates.MaxBackdateDays));
        if (created.IsFailure)
        {
            return created.Error;
        }

        var creditNote = created.Value;
        repository.Add(creditNote);

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? creditNote.ToDto() : committed.Error;
    }
}
