using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Reconciliation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>
/// Reactivates a settlement channel. The client must have read the current version (BR-804); a stale version is answered
/// before the status rule, so the client re-reads and sees why.
/// </summary>
public sealed class ReactivateSettlementChannelHandler(ISettlementChannelRepository repository, IUnitOfWork unitOfWork)
    : ICommandHandler<ReactivateSettlementChannelCommand, SettlementChannelDto>
{
    /// <inheritdoc />
    public async Task<Result<SettlementChannelDto>> HandleAsync(ReactivateSettlementChannelCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var channel = await repository.GetAsync(command.Id, ct);
        if (channel is null)
        {
            return ReconciliationErrors.SettlementChannelNotFound();
        }

        if (RowVersionText.Of(channel) != command.ExpectedVersion)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        var changed = channel.Reactivate();
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        var committed = await unitOfWork.CommitAsync(ct);
        return committed.IsSuccess ? channel.ToDto() : committed.Error;
    }
}
