using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Replaces every line of a draft bill with the lines sent, in their order; an empty list removes them all (BR-204).
/// <see cref="ExpectedVersion"/> is the version of the bill the client read (BR-804).
/// </summary>
public sealed record ReplaceBillLinesCommand
{
    /// <summary>The bill whose lines are replaced.</summary>
    public required BillId Id { get; init; }

    /// <summary>The version the client read, from its If-Match header: the row version in base64.</summary>
    public required string ExpectedVersion { get; init; }

    /// <summary>The lines, in the order they appear on the bill.</summary>
    public IReadOnlyList<BillLineInput> Lines { get; init; } = [];
}
