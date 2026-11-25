namespace BillingRecon.Application.Billing;

/// <summary>Adds an entry to the tenant's service catalogue. The code is fixed from then on.</summary>
public sealed record CreateServiceItemCommand : IServiceItemDetailsInput
{
    /// <summary>The code of the item, unique in the tenant.</summary>
    public required string Code { get; init; }

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
