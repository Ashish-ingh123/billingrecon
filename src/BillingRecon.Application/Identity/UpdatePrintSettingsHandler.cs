using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Identity;

/// <summary>
/// A TenantAdmin sets how one kind of document is printed (ADR 0059). The first save of a kind makes its row, and must
/// say that it read none (<see cref="PrintSettingsMapping.NoVersion"/>); a later one changes the row under its version.
/// Settings that change nothing write nothing and audit nothing. Two first saves at once: the unique index lets one
/// through, and the other is told the record changed.
/// </summary>
public sealed class UpdatePrintSettingsHandler(
    IValidator<UpdatePrintSettingsCommand> validator,
    IPrintSettingsRepository settings,
    IUnitOfWork unitOfWork) : ICommandHandler<UpdatePrintSettingsCommand, PrintSettingsDto>
{
    /// <inheritdoc />
    public async Task<Result<PrintSettingsDto>> HandleAsync(UpdatePrintSettingsCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        var current = await settings.FindAsync(command.DocumentType, ct);
        var version = current is null ? PrintSettingsMapping.NoVersion : RowVersionText.Of(current);
        if (version != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        if (current is null)
        {
            current = Domain.Identity.PrintSettings.Create(command.DocumentType, command.Layout);
            settings.Add(current);
        }
        else if (!current.Change(command.Layout))
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
