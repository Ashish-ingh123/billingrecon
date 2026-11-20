using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// Answers <see cref="IBillSettlement"/> with the module's own repository: the bill is tracked by the context of the
/// request, so the commit of the calling use case saves it with everything else, and a bill somebody changed in the
/// meantime fails that commit with <c>concurrency_conflict</c> (BR-804).
/// </summary>
public sealed class BillSettlement(IBillRepository bills) : IBillSettlement
{
    /// <inheritdoc />
    public async Task<Result<SettledBill>> ApplyAsync(BillId billId, PayerId payerId, Money amount, CancellationToken ct)
    {
        var bill = await bills.GetAsync(billId, ct);
        if (bill is null)
        {
            return BillingErrors.BillNotFound();
        }

        if (bill.PayerId != payerId)
        {
            return BillingErrors.AllocationPayerMismatch();
        }

        var applied = bill.ApplyCredit(amount);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        // A posted bill has its number; ApplyCredit refused one that has none.
        return new SettledBill(bill.Number ?? string.Empty, bill.BalanceDue);
    }
}
