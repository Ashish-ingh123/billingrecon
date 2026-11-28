using System.Text.Json.Serialization;
using BillingRecon.Application.Reconciliation;

namespace BillingRecon.Api.Reconciliation;

/// <summary>
/// The body of the dry run and of the import of a bank's statement (BR-1003): the channel, the file's name and its text,
/// read as UTF-8, and for the import the file hash its dry run answered. A member the API does not know is refused.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StatementFileRequest
{
    /// <summary>The bank channel the file is for.</summary>
    public Guid? ChannelId { get; init; }

    /// <summary>The name of the file.</summary>
    public string? FileName { get; init; }

    /// <summary>The text of the file, at most 1 MB.</summary>
    public string? Text { get; init; }

    /// <summary>The file hash its dry run answered; read by the import only.</summary>
    public string? FileHash { get; init; }

    /// <summary>The dry run of this file.</summary>
    public DryRunStatementQuery ToQuery() => new()
    {
        ChannelId = ChannelId ?? Guid.Empty,
        FileName = FileName ?? string.Empty,
        Text = Text ?? string.Empty,
    };

    /// <summary>The import of this file.</summary>
    public ImportStatementCommand ToCommand() => new()
    {
        ChannelId = ChannelId ?? Guid.Empty,
        FileName = FileName ?? string.Empty,
        Text = Text ?? string.Empty,
        ExpectedFileHash = FileHash ?? string.Empty,
    };
}
