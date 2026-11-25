using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Billing;

/// <summary>
/// What a clerk enters and corrects about an entry of the service catalogue (docs/database.md, section 3.3): its
/// name, its category, and the price and tax rate a new bill line takes from it (BR-201). The code is fixed when the
/// item is created, and the status changes by deactivation. An optional text may be null or blank;
/// <see cref="ServiceItem"/> stores text without surrounding white space, and a blank optional one as null.
/// </summary>
public sealed record ServiceItemDetails
{
    /// <summary>The name of the service, as a bill line shows it.</summary>
    public required string Name { get; init; }

    /// <summary>A free grouping for lists and reports, such as Consultation or Laboratory.</summary>
    public string? Category { get; init; }

    /// <summary>The price of one unit that a new bill line starts from.</summary>
    public required UnitPrice DefaultUnitPrice { get; init; }

    /// <summary>The tax rate that a new bill line starts from.</summary>
    public required Percent TaxRate { get; init; }

    /// <summary>The SAC code of the service, six digits, when the hospital classifies it (ADR 0054).</summary>
    public string? SacCode { get; init; }
}
