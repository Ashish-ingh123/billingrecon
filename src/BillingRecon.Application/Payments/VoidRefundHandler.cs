using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>Voids a draft refund. Nothing financial happened, and nothing does.</summary>
public sealed class VoidRefundHandler(
    IValidator<VoidRefundCommand> validator,
    IClock clock,
    ICurrentUser currentUser,
    IRefundRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<VoidRefundCommand, RefundDto>
{
    /// <inheritdoc />
    public async Task<Result<RefundDto>> HandleAsync(VoidRefundCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        var refund = await repository.GetAsync(command.Id, ct);
        if (refund is null)
        {
            return PaymentsErrors.RefundNotFound();
        }

        if (RowVersionText.Of(refund) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        var voided = refund.Void(command.Reason, clock.UtcNow, currentUser.UserId);
        if (voided.IsFailure)
        {
            return voided.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? refund.ToDto() : committed.Error;
    }
}
