using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Applies available credit of a posted credit note to one or more posted bills of its payer (BR-213), all of them or
/// none. <see cref="ExpectedVersion"/> is the version of the credit note the client read (BR-804).
/// </summary>
public sealed record ApplyCreditNoteCommand
{
    /// <summary>The most bills one request may name.</summary>
    public const int MaxApplications = 50;

    /// <summary>The credit note whose available credit is applied.</summary>
    public required CreditNoteId Id { get; init; }

    /// <summary>The version the client read, from its If-Match header: the row version in base64.</summary>
    public required string ExpectedVersion { get; init; }

    /// <summary>The bills and the amount for each. A bill is named once.</summary>
    public IReadOnlyList<CreditApplicationInput> Applications { get; init; } = [];
}
