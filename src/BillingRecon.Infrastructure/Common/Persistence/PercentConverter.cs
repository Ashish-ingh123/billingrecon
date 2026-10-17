using BillingRecon.Domain.Common;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BillingRecon.Infrastructure.Common.Persistence;

/// <summary>Stores <see cref="Percent"/> as its decimal value, in a <c>decimal(5,2)</c> column; registered by convention.</summary>
public sealed class PercentConverter() : ValueConverter<Percent, decimal>(percent => percent.Value, value => Percent.Of(value))
{
}
