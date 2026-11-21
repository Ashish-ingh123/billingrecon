using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Replaces every line of a draft credit note with the lines sent, in their order. <see cref="ExpectedVersion"/> is
/// the version of the credit note the client read (BR-804).
/// </summary>
public sealed record ReplaceCreditNoteLinesCommand
{
    /// <summary>The credit note whose lines are replaced.</summary>
    public required CreditNoteId Id { get; init; }

    /// <summary>The version the client read, from its If-Match header: the row version in base64.</summary>
    public required string ExpectedVersion { get; init; }

    /// <summary>The lines, in the order they appear on the credit note.</summary>
    public IReadOnlyList<CreditNoteLineInput> Lines { get; init; } = [];
}
