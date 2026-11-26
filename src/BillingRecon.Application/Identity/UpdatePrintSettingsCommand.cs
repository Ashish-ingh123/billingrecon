using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>A TenantAdmin sets how one kind of document is printed (ADR 0059). Every part is sent.</summary>
public sealed record UpdatePrintSettingsCommand
{
    /// <summary>The kind of document.</summary>
    public required PrintedDocumentType DocumentType { get; init; }

    /// <summary>The layout as it is to be.</summary>
    public required PrintLayout Layout { get; init; }

    /// <summary>The version the client read; <see cref="PrintSettingsMapping.NoVersion"/> for settings never saved.</summary>
    public required string ExpectedVersion { get; init; }
}
