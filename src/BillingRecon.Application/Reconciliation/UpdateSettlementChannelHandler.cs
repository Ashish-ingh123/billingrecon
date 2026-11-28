using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Reconciliation;
using FluentValidation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>
/// Changes a settlement channel (BR-1001, BR-1002). The client must have read the current version (BR-804). A bank
/// sends its mapping and no bank channel; a gateway sends its bank channel, an active bank of the hospital other than
/// itself, and no mapping. Lines imported before keep what was read.
/// </summary>
public sealed class UpdateSettlementChannelHandler(
    IValidator<UpdateSettlementChannelCommand> validator,
    ISettlementChannelReadService channels,
    ISettlementChannelRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<UpdateSettlementChannelCommand, SettlementChannelDto>
{
    /// <inheritdoc />
    public async Task<Result<SettlementChannelDto>> HandleAsync(UpdateSettlementChannelCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        var channel = await repository.GetAsync(command.Id, ct);
        if (channel is null)
        {
            return ReconciliationErrors.SettlementChannelNotFound();
        }

        if (RowVersionText.Of(channel) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        SettlementChannelId? bank = null;
        if (channel.Kind == SettlementChannelKind.Bank)
        {
            if (command.Mapping is null || command.BankChannelId is not null)
            {
                return CommonErrors.ValidationFailed(new Dictionary<string, string[]> { ["mapping"] = ["A bank channel sends its column mapping and no bank channel."] });
            }
        }
        else
        {
            if (command.Mapping is not null || command.BankChannelId is null)
            {
                return CommonErrors.ValidationFailed(new Dictionary<string, string[]> { ["bankChannelId"] = ["A gateway channel sends the bank channel it settles into and no mapping."] });
            }

            bank = new SettlementChannelId(command.BankChannelId.Value);
            if (bank == channel.Id || !await channels.IsActiveBankAsync(bank.Value, ct))
            {
                return ReconciliationErrors.SettlementChannelBankInvalid();
            }
        }

        var name = command.Name.Trim();
        if (await channels.NameExistsAsync(name, channel.Id, ct))
        {
            return ReconciliationErrors.SettlementChannelNameDuplicate();
        }

        var label = string.IsNullOrWhiteSpace(command.AccountLabel) ? null : command.AccountLabel.Trim();
        channel.Change(name, label, command.DateWindowDays ?? channel.DateWindowDays, bank);
        if (command.Mapping is { } mapping && channel.ChangeMapping(mapping.ToMapping()) is { IsFailure: true } refused)
        {
            return refused.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);
        if (committed.IsFailure)
        {
            return committed.Error.Code == CommonErrors.DuplicateKeyCode ? ReconciliationErrors.SettlementChannelNameDuplicate() : committed.Error;
        }

        return channel.ToDto();
    }
}
