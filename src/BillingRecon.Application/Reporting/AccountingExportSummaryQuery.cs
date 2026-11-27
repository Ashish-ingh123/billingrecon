
namespace BillingRecon.Application.Reporting;

/// <summary>The control totals of an export of a period of business dates.</summary>
public sealed record AccountingExportSummaryQuery
{
    /// <summary>The first business date.</summary>
    public DateOnly? From { get; init; }

    /// <summary>The last business date.</summary>
    public DateOnly? To { get; init; }
}
