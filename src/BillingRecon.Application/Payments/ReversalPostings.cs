using BillingRecon.Application.Billing;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Application.Payments;

/// <summary>What reversing one allocation does outside the payment: the bill and the ledger.</summary>
internal static class ReversalPostings
{
    /// <summary>
    /// Gives the amount of a reversed <paramref name="allocation"/> back to its bill, through the Billing module's
    /// contract, enters the mirror pair that names the allocation's own posting, and records that posting on the
    /// allocation (BR-305).
    /// </summary>
    public static async Task<Result> EnterAsync(
        Payment payment,
        PaymentAllocation allocation,
        DateOnly reversalDate,
        IBillSettlement bills,
        IReceivablesLedger ledger,
        CancellationToken ct)
    {
        var restored = await bills.RestoreAsync(allocation.BillId, payment.PayerId, allocation.Amount, ct);
        if (restored.IsFailure)
        {
            return restored.Error;
        }

        var template = PostingTemplate.AllocationReversed(
            allocation.Id, payment.Id, allocation.BillId, payment.PayerId, allocation.Amount, reversalDate, payment.Number, restored.Value.Number, allocation.PostingId);
        var entered = await ledger.PostAsync(template, ct);
        if (entered.IsFailure)
        {
            return entered.Error;
        }

        payment.AttachAllocationReversalPosting(allocation, entered.Value);

        return Result.Success();
    }
}
