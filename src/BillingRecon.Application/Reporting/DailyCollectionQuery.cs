namespace BillingRecon.Application.Reporting;

/// <summary>
/// The collection of one business date by method (US-42; BR-003, BR-901): what was received, reversed and refunded that
/// day, taken from the ledger postings of that date.
/// </summary>
public sealed record DailyCollectionQuery
{
    /// <summary>The business date in India; today when left out.</summary>
    public DateOnly? Date { get; init; }
}
