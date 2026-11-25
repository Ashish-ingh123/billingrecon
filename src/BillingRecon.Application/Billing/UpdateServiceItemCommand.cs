using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Replaces the name, category, price and tax rate of a service item: every field is sent, and a category left out is
/// cleared. The code and the status are not part of it. <see cref="ExpectedVersion"/> is the version the client read
/// (BR-804).
/// </summary>
public sealed record UpdateServiceItemCommand : IServiceItemDetailsInput
{
    /// <summary>The item to correct.</summary>
    public required ServiceItemId Id { get; init; }

    /// <summary>The version the client read, from its If-Match header: the row version in base64.</summary>
    public required string ExpectedVersion { get; init; }

    /// <inheritdoc />
    public required string Name { get; init; }

    /// <inheritdoc />
    public string? Category { get; init; }

    /// <inheritdoc />
    public decimal? DefaultUnitPrice { get; init; }

    /// <inheritdoc />
    public decimal? TaxRatePercent { get; init; }

    /// <inheritdoc />
    public string? SacCode { get; init; }
}
