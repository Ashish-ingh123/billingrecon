using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payments;

/// <summary>
/// Allocates a payment to one or more posted bills of its payer (US-31; BR-303, BR-304), all of them or none.
/// <see cref="ExpectedVersion"/> is the version of the payment the client read (BR-804).
/// </summary>
public sealed record AllocatePaymentCommand
{
    /// <summary>The most bills one request may name.</summary>
    public const int MaxAllocations = 50;

    /// <summary>The payment whose unapplied amount is allocated.</summary>
    public required PaymentId Id { get; init; }

    /// <summary>The version the client read, from its If-Match header: the row version in base64.</summary>
    public required string ExpectedVersion { get; init; }

    /// <summary>The bills and the amount for each. A bill is named once.</summary>
    public IReadOnlyList<AllocationInput> Allocations { get; init; } = [];
}
