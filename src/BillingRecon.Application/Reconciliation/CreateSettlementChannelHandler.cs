using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Reconciliation;
using FluentValidation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>
/// Sets up a settlement channel (BR-1001). The name must be free in the hospital, whatever its case; a gateway's bank
/// channel must be an active bank channel of the hospital. The unique index on the name decides a race between two
/// channels of one name, whose loser gets the same answer as the check here.
/// </summary>
public sealed class CreateSettlementChannelHandler(
    IValidator<CreateSettlementChannelCommand> validator,
    ISettlementChannelReadService channels,
    ISettlementChannelRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<CreateSettlementChannelCommand, SettlementChannelDto>
{
    /// <inheritdoc />
    public async Task<Result<SettlementChannelDto>> HandleAsync(CreateSettlementChannelCommand command, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(command, ct) is { } invalid)
        {
            return invalid;
        }

        var name = command.Name.Trim();
        if (await channels.NameExistsAsync(name, null, ct))
        {
            return ReconciliationErrors.SettlementChannelNameDuplicate();
        }

        var label = string.IsNullOrWhiteSpace(command.AccountLabel) ? null : command.AccountLabel.Trim();
        var window = command.DateWindowDays ?? SettlementChannel.DefaultDateWindowDays;
        SettlementChannel channel;
        if (command.Kind == SettlementChannelKind.Gateway)
        {
            var bank = new SettlementChannelId(command.BankChannelId!.Value);
            if (!await channels.IsActiveBankAsync(bank, ct))
            {
                return ReconciliationErrors.SettlementChannelBankInvalid();
            }

            channel = SettlementChannel.CreateGateway(name, label, window, command.Gateway!.Value, bank);
        }
        else
        {
            channel = SettlementChannel.CreateBank(name, label, window, command.Mapping!.ToMapping());
        }

        repository.Add(channel);

        var committed = await unitOfWork.CommitAsync(ct);
        if (committed.IsFailure)
        {
            // The only unique index a new channel can hit is the name's: another channel of that name won the race.
            return committed.Error.Code == CommonErrors.DuplicateKeyCode ? ReconciliationErrors.SettlementChannelNameDuplicate() : committed.Error;
        }

        return channel.ToDto();
    }
}
