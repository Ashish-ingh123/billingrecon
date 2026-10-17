using BillingRecon.Domain.Common;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BillingRecon.Infrastructure.Common.Persistence;

/// <summary>
/// Stores <see cref="Money"/> as its decimal amount, in a <c>decimal(18,2)</c> column (docs/database.md, section 1).
/// <see cref="BillingReconDbContext"/> registers it by convention for every <see cref="Money"/> property, nullable
/// ones included. Entity Framework creates the instance, which is why the class is public with a parameterless
/// constructor.
/// </summary>
public sealed class MoneyConverter() : ValueConverter<Money, decimal>(money => money.Amount, amount => Money.Of(amount))
{
}
