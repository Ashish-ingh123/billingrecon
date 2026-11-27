
namespace BillingRecon.Application.Reporting;

/// <summary>The export of a period of business dates as a file.</summary>
public sealed record AccountingExportQuery
{
    /// <summary>The first business date.</summary>
    public DateOnly? From { get; init; }

    /// <summary>The last business date.</summary>
    public DateOnly? To { get; init; }
}
