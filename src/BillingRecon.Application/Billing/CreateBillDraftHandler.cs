using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Payers;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Creates a draft bill (US-20). The patient and the payer are checked through the Payers module's contract: both must
/// exist for the tenant and be active (BR-102, BR-105, BR-106). The dates default from the clock and the tenant's
/// settings and must be allowed today (BR-205). Lines priced from the catalogue take their defaults from it (BR-201).
/// A draft has no number and no ledger effect, so nothing but the bill is written.
/// </summary>
public sealed class CreateBillDraftHandler(
    IValidator<CreateBillDraftCommand> validator,
    IBillableParties parties,
    ITenantSettingsReader settings,
    IServiceItemReadService items,
    IClock clock,
    IBillRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<CreateBillDraftCommand, BillDto>
{
    private static readonly HashSet<ServiceItemId> NoLinesYet = [];

    /// <inheritdoc />
    public async Task<Result<BillDto>> HandleAsync(CreateBillDraftCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        var billable = await parties.EnsureCanBeBilledAsync(command.PatientId, command.PayerId, ct);
        if (billable.IsFailure)
        {
            return billable.Error;
        }

        var lines = await BillLineResolver.ResolveAsync(items, command.Lines, NoLinesYet, ct);
        if (lines.IsFailure)
        {
            return lines.Error;
        }

        // One reading of the clock for the use case (BR-003).
        var today = clock.TodayIst;
        var dates = await settings.GetDocumentSettingsAsync(ct);
        var billDate = command.BillDate ?? today;

        // A bill date in the future is refused below; no due date is derived from it, so a date at the end of the
        // calendar cannot overflow here first.
        var dueDate = command.DueDate ?? (billDate <= today ? billDate.AddDays(dates.DefaultDueDays) : billDate);

        var created = Bill.Create(
            command.PatientId,
            command.PayerId,
            new BillHeader { BillDate = billDate, DueDate = dueDate, EncounterReference = command.EncounterReference, Notes = command.Notes },
            lines.Value,
            new BusinessDateWindow(today, dates.MaxBackdateDays));
        if (created.IsFailure)
        {
            return created.Error;
        }

        var bill = created.Value;
        repository.Add(bill);

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? bill.ToDto() : committed.Error;
    }
}
