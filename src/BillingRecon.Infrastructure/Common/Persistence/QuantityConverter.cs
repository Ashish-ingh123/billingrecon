using BillingRecon.Domain.Common;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BillingRecon.Infrastructure.Common.Persistence;

/// <summary>Stores <see cref="Quantity"/> as its decimal value, in a <c>decimal(9,3)</c> column; registered by convention.</summary>
public sealed class QuantityConverter() : ValueConverter<Quantity, decimal>(quantity => quantity.Value, value => Quantity.Of(value))
{
}
