namespace BillingRecon.Application.Payments;

/// <summary>
/// What Payments answers to the matching of bank statements (R2, ADR 0066): the payments and refunds a statement line
/// may be, read only, filtered by the tenant. Never cash (it reaches the bank as a shift's deposit, BR-1008), never a
/// reversed payment, only a posted refund. Reconciliation reads them through this port and names no entity of Payments.
/// </summary>
public interface IMatchablePayments
{
    /// <summary>
    /// The posted, unreversed payments dated <paramref name="from"/> to <paramref name="to"/>: bank transfers and cheques,
    /// and UPI and card ones too when <paramref name="withUpiAndCard"/> (a bank that no gateway settles into).
    /// </summary>
    public Task<IReadOnlyList<MatchableDocument>> PaymentsAsync(DateOnly from, DateOnly to, bool withUpiAndCard, CancellationToken ct);

    /// <summary>The posted refunds dated <paramref name="from"/> to <paramref name="to"/>, by the same methods.</summary>
    public Task<IReadOnlyList<MatchableDocument>> RefundsAsync(DateOnly from, DateOnly to, bool withUpiAndCard, CancellationToken ct);
}
