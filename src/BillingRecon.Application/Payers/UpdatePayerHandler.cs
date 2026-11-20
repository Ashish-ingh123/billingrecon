using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using FluentValidation;

namespace BillingRecon.Application.Payers;

/// <summary>
/// Replaces the name and contact details of a payer. The client must have read the current version (BR-804): a stale
/// one is refused before anything changes, and the commit checks the row version once more, against the row itself.
/// </summary>
public sealed class UpdatePayerHandler(
    IValidator<UpdatePayerCommand> validator,
    IPayerRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<UpdatePayerCommand, PayerDto>
{
    /// <inheritdoc />
    public async Task<Result<PayerDto>> HandleAsync(UpdatePayerCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        var payer = await repository.GetAsync(command.Id, ct);
        if (payer is null)
        {
            return PayersErrors.PayerNotFound();
        }

        if (RowVersionText.Of(payer) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        payer.Update(command.ToDetails());

        var committed = await unitOfWork.CommitAsync(ct);

        return committed.IsSuccess ? payer.ToDto() : committed.Error;
    }
}
