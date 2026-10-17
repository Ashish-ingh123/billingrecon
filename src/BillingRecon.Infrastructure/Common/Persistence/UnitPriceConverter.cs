using BillingRecon.Domain.Common;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BillingRecon.Infrastructure.Common.Persistence;

/// <summary>Stores <see cref="UnitPrice"/> as its decimal value, in a <c>decimal(19,4)</c> column; registered by convention.</summary>
public sealed class UnitPriceConverter() : ValueConverter<UnitPrice, decimal>(price => price.Value, value => UnitPrice.Of(value))
{
}
