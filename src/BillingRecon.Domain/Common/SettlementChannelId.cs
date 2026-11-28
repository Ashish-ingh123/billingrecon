namespace BillingRecon.Domain.Common;

/// <summary>
/// The id of a settlement channel, a bank or gateway account of the hospital (ADR 0065). In Common, like every typed
/// id: the statement lines, the matches and the reports of R2 name the channel.
/// </summary>
public readonly record struct SettlementChannelId(Guid Value) : ITypedId
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
