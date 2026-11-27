namespace BillingRecon.Application.Reporting;

/// <summary>The money received and paid back on a business date (US-42), read from the ledger of the current tenant.</summary>
public interface ICollectionReports
{
    /// <summary>
    /// The collection of <paramref name="date"/>: the Cash entries of the postings of that business date that record,
    /// reverse or refund a payment, each under the method of its payment or refund.
    /// </summary>
    public Task<DailyCollectionDto> DailyCollectionAsync(DateOnly date, CancellationToken ct);

    /// <summary>
    /// The day-end report of <paramref name="date"/> (BR-903; ADR 0060): the shifts of that business date, each with the
    /// Cash entries of the postings that name it, and the cash of the date that no shift holds. The cashiers are not
    /// named here: the use case asks Identity.
    /// </summary>
    public Task<DayEndDto> DayEndAsync(DateOnly date, CancellationToken ct);
}
