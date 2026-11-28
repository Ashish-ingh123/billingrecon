namespace BillingRecon.Application.Reconciliation;

/// <summary>Reads a bank's statement file with its channel's mapping and says what importing it would do, storing nothing (BR-1003).</summary>
public sealed record DryRunStatementQuery : IStatementFileInput
{
    /// <inheritdoc />
    public required Guid ChannelId { get; init; }

    /// <inheritdoc />
    public required string FileName { get; init; }

    /// <inheritdoc />
    public required string Text { get; init; }
}
