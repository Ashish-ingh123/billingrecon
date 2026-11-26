using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>A TenantAdmin removes the logo or the signature from the letterhead (ADR 0059).</summary>
public sealed record RemoveLetterheadImageCommand
{
    /// <summary>Which image.</summary>
    public required LetterheadImageKind Kind { get; init; }

    /// <summary>The version of the settings the client read.</summary>
    public required string ExpectedVersion { get; init; }
}
