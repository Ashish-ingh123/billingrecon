using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Voids a draft bill (US-23, BR-204): the bill keeps its content, records who voided it, when and why, and can no
/// longer change. The client must have read the current version (BR-804); a stale version is answered before the state
/// rule, so the client re-reads and sees why.
/// </summary>
public sealed class VoidBillHandler(
    IValidator<VoidBillCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    IBillRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<VoidBillCommand, BillDto>
{
    /// <inheritdoc />
    public async Task<Result<BillDto>> HandleAsync(VoidBillCommand command, CancellationToken ct)
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

        var voided = bill.Void(command.Reason, clock.UtcNow, currentUser.UserId);
        if (voided.IsFailure)
        {
            return voided.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? bill.ToDto() : committed.Error;
    }
}
