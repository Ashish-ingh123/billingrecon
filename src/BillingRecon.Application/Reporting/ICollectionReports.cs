namespace BillingRecon.Application.Reporting;

/// <summary>The money received and paid back on a business date (US-42), read from the ledger of the current tenant.</summary>
public interface ICollectionReports
{
    /// <summary>
    /// The collection of <paramref name="date"/>: the Cash entries of the postings of that business date that record,
    /// reverse or refund a payment, each under the method of its payment or refund.
    /// </summary>
    public Task<DailyCollectionDto> DailyCollectionAsync(DateOnly date, CancellationToken ct);
}
