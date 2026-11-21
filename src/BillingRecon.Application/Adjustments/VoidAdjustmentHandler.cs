using BillingRecon.Application.Common;
using BillingRecon.Domain.Adjustments;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Adjustments;

/// <summary>
/// Voids a draft adjustment: it keeps its content, records who voided it, when and why, and can no longer change. The
/// client must have read the current version (BR-804).
/// </summary>
public sealed class VoidAdjustmentHandler(
    IValidator<VoidAdjustmentCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    IAdjustmentRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<VoidAdjustmentCommand, AdjustmentDto>
{
    /// <inheritdoc />
    public async Task<Result<AdjustmentDto>> HandleAsync(VoidAdjustmentCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        var adjustment = await repository.GetAsync(command.Id, ct);
        if (adjustment is null)
        {
            return AdjustmentsErrors.AdjustmentNotFound();
        }

        if (RowVersionText.Of(adjustment) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        var voided = adjustment.Void(command.Reason, clock.UtcNow, currentUser.UserId);
        if (voided.IsFailure)
        {
            return voided.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? adjustment.ToDto() : committed.Error;
    }
}
