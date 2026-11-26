using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>A TenantAdmin sets or replaces the logo or the signature of the letterhead (ADR 0059).</summary>
public sealed record SetLetterheadImageCommand
{
    /// <summary>Which image.</summary>
    public required LetterheadImageKind Kind { get; init; }

    /// <summary>The file, byte for byte.</summary>
    public required byte[] Content { get; init; }

    /// <summary>The media type the client said the file has; it must be the one the bytes state.</summary>
    public string? DeclaredContentType { get; init; }

    /// <summary>The version of the settings the client read.</summary>
    public required string ExpectedVersion { get; init; }
}
