namespace BillingRecon.Application.Reconciliation;

/// <summary>
/// Imports a bank's statement file into its channel (BR-1003, BR-1004): the lines the channel does not hold yet, each
/// once. <see cref="ExpectedFileHash"/> is the hash its dry run answered: a file that differs is refused whole.
/// </summary>
public sealed record ImportStatementCommand : IStatementFileInput
{
    /// <inheritdoc />
    public required Guid ChannelId { get; init; }

    /// <inheritdoc />
    public required string FileName { get; init; }

    /// <inheritdoc />
    public required string Text { get; init; }

    /// <summary>The file hash the dry run of this file answered.</summary>
    public required string ExpectedFileHash { get; init; }
}
