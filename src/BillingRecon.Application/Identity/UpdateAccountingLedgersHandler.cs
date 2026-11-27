using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>
/// An administrator saves the names of the accountant's ledgers (ADR 0062). The first save makes the hospital's row;
/// two first saves at once leave one row, and the other is told the names changed (409).
/// </summary>
public sealed class UpdateAccountingLedgersHandler(
    IValidator<UpdateAccountingLedgersCommand> validator,
    IAccountingLedgersRepository ledgers,
    IUnitOfWork unitOfWork) : ICommandHandler<UpdateAccountingLedgersCommand, AccountingLedgersDto>
{
    /// <inheritdoc />
    public async Task<Result<AccountingLedgersDto>> HandleAsync(UpdateAccountingLedgersCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        var current = await ledgers.FindAsync(ct);
        var version = current is null ? AccountingLedgersMapping.NoVersion : RowVersionText.Of(current);
        if (version != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        if (current is null)
        {
            current = AccountingLedgers.Create(command.Names);
            ledgers.Add(current);
        }
        else if (!current.Change(command.Names))
        {
            return current.ToDto();
        }

        var committed = await unitOfWork.CommitAsync(ct);
        if (committed.IsFailure)
        {
            // The row another request made in the same instant: this one read none, and is told so.
            return committed.Error.Code == CommonErrors.DuplicateKey().Code ? CommonErrors.ConcurrencyConflict() : committed.Error;
        }

        return current.ToDto();
    }
}
