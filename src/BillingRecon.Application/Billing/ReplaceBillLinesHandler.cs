using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Replaces the lines of a draft bill (BR-204). The client must have read the current version of the bill (BR-804): a
/// stale one is refused before anything changes, and the commit checks the bill's row version once more, also when
/// only lines changed. A service item that was deactivated since the draft was written may stay on the lines it is
/// already on; a new line cannot name it.
/// </summary>
public sealed class ReplaceBillLinesHandler(
    IValidator<ReplaceBillLinesCommand> validator,
    IServiceItemReadService items,
    IBillRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<ReplaceBillLinesCommand, BillDto>
{
    /// <inheritdoc />
    public async Task<Result<BillDto>> HandleAsync(ReplaceBillLinesCommand command, CancellationToken ct)
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

        // The state is the first rule: a bill that cannot be edited needs no look at the catalogue.
        var editable = bill.CheckIsDraft();
        if (editable.IsFailure)
        {
            return editable.Error;
        }

        var alreadyOnBill = bill.Lines.Select(line => line.ServiceItemId).OfType<ServiceItemId>().ToHashSet();
        var lines = await BillLineResolver.ResolveAsync(items, command.Lines, alreadyOnBill, ct);
        if (lines.IsFailure)
        {
            return lines.Error;
        }

        var replaced = bill.ReplaceLines(lines.Value);
        if (replaced.IsFailure)
        {
            return replaced.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? bill.ToDto() : committed.Error;
    }
}
