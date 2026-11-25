using System.Text.Json.Serialization;
using BillingRecon.Application.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Api.Billing;

/// <summary>
/// The body of <c>PATCH /api/v1/service-items/{id}</c>: the name, the price, the tax rate and the category, and a
/// category left out is cleared. The code and the status cannot be changed here: a body that names them, or any member
/// the API does not know, is refused with 400.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateServiceItemRequest
{
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

    /// <summary>The command for service item <paramref name="id"/>, at the version the client read.</summary>
    public UpdateServiceItemCommand ToCommand(ServiceItemId id, string expectedVersion) => new()
    {
        Id = id,
        ExpectedVersion = expectedVersion,
        Name = Name ?? string.Empty,
        Category = Category,
        DefaultUnitPrice = DefaultUnitPrice,
        TaxRatePercent = TaxRatePercent,
        SacCode = SacCode,
    };
}
