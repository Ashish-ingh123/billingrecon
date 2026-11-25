namespace BillingRecon.Application.Billing;

/// <summary>
/// The name, category, price and tax rate a command about a service item carries, as the client sent them: creating an
/// item and correcting one share them, and share their validator. A missing required text arrives as an empty string,
/// a missing number as null.
/// </summary>
public interface IServiceItemDetailsInput
{
    /// <summary>The name of the service.</summary>
    public string Name { get; }

    /// <summary>A free grouping for lists and reports.</summary>
    public string? Category { get; }

    /// <summary>The price of one unit that a new bill line starts from; null when the client sent none.</summary>
    public decimal? DefaultUnitPrice { get; }

    /// <summary>The tax rate, in percent, that a new bill line starts from; null when the client sent none.</summary>
    public decimal? TaxRatePercent { get; }

    /// <summary>The SAC code of the service, six digits; left out for none (ADR 0054).</summary>
    public string? SacCode { get; }
}
