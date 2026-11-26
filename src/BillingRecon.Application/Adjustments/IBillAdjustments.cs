using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Adjustments;

/// <summary>
/// What Adjustments answers when a bill is about to be credited (ADR 0025, amended 2026-11-26): how much of the bill
/// its posted adjustments have already taken off. A credit note may credit only what was billed and not written off,
/// discounted or rounded away; otherwise the credit of money never owed could be refunded as cash.
/// </summary>
public interface IBillAdjustments
{
    /// <summary>The sum of the amounts of the posted adjustments of the bill, read now and never from what is tracked; zero when it has none.</summary>
    public Task<Money> SumPostedAsync(BillId billId, CancellationToken ct);
}
