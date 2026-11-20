using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;

namespace BillingRecon.Application.Payers;

/// <summary>
/// Deactivates a payer (US-12, BR-105). The client must have read the current version (BR-804); a stale version is
/// answered before the status rule, so the client re-reads and sees why.
/// </summary>
public sealed class DeactivatePayerHandler(IPayerRepository repository, IUnitOfWork unitOfWork)
    : ICommandHandler<DeactivatePayerCommand, PayerDto>
{
    /// <inheritdoc />
    public async Task<Result<PayerDto>> HandleAsync(DeactivatePayerCommand command, CancellationToken ct)
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

        var deactivated = payer.Deactivate();
        if (deactivated.IsFailure)
        {
            return deactivated.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? payer.ToDto() : committed.Error;
    }
}
