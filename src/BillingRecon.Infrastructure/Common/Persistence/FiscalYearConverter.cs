using BillingRecon.Domain.Common;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BillingRecon.Infrastructure.Common.Persistence;

/// <summary>Stores a <see cref="FiscalYear"/> as its label, <c>2026-27</c>, in a <c>char(7)</c> column (BR-004; docs/database.md, section 1).</summary>
public sealed class FiscalYearConverter() : ValueConverter<FiscalYear, string>(fiscalYear => fiscalYear.Label, label => FiscalYear.Parse(label))
{
}
