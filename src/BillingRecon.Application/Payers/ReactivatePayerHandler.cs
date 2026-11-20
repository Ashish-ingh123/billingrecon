using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;

namespace BillingRecon.Application.Payers;

/// <summary>Makes an inactive payer active again. The client must have read the current version (BR-804).</summary>
public sealed class ReactivatePayerHandler(IPayerRepository repository, IUnitOfWork unitOfWork)
    : ICommandHandler<ReactivatePayerCommand, PayerDto>
{
    /// <inheritdoc />
    public async Task<Result<PayerDto>> HandleAsync(ReactivatePayerCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var payer = await repository.GetAsync(command.Id, ct);
        if (payer is null)
        {
            return PayersErrors.PayerNotFound();
        }

        if (RowVersionText.Of(payer) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        var reactivated = payer.Reactivate();
        if (reactivated.IsFailure)
        {
            return reactivated.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? payer.ToDto() : committed.Error;
    }
}
