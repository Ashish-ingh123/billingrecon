using System.Text.Json.Serialization;
using BillingRecon.Application.Billing;

namespace BillingRecon.Api.Billing;

/// <summary>
/// The body of <c>POST /api/v1/service-items</c>. A member the API does not know is refused with 400, so a misspelt
/// field cannot pass unnoticed.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateServiceItemRequest
{
    /// <summary>The code of the item, unique in the tenant: letters, digits and <c>. - _ /</c>. It cannot be changed later.</summary>
    public string? Code { get; init; }

    /// <summary>The name of the service, as a bill line shows it.</summary>
    public string? Name { get; init; }

    /// <summary>A free grouping for lists and reports.</summary>
    public string? Category { get; init; }

    /// <summary>The price of one unit that a new bill line starts from: 0 or more, at most four decimals.</summary>
    public decimal? DefaultUnitPrice { get; init; }

    /// <summary>The tax rate, in percent, that a new bill line starts from: 0 to 100, at most two decimals.</summary>
    public decimal? TaxRatePercent { get; init; }

    /// <summary>The SAC code of the service, six digits; left out for none.</summary>
    public string? SacCode { get; init; }

    /// <summary>The command; a missing text becomes empty and a missing number stays null, which the validator reports.</summary>
    public CreateServiceItemCommand ToCommand() => new()
    {
        Code = Code ?? string.Empty,
        Name = Name ?? string.Empty,
        Category = Category,
        DefaultUnitPrice = DefaultUnitPrice,
        TaxRatePercent = TaxRatePercent,
        SacCode = SacCode,
    };
}
