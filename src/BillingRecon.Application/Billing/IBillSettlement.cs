using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Billing;

/// <summary>
/// What the Billing module offers a module that settles bills (BR-208, BR-303): apply an amount to a posted bill of a
/// given payer. The bill is loaded and changed inside Billing, joins the unit of work of the request, and is saved,
/// under its row version, by the commit of the use case that called (docs/domain-model.md, section 6). So Payments
/// reaches a bill only through this contract and never sees the entity (ADR 0003, ADR 0024).
/// </summary>
public interface IBillSettlement
{
    /// <summary>
    /// Lowers what is owed on <paramref name="billId"/> by <paramref name="amount"/> and lets its status follow the
    /// balance. Fails, and changes nothing, with <c>bill_not_found</c>, <c>allocation_payer_mismatch</c> when the bill
    /// is owed by another payer than <paramref name="payerId"/>, <c>bill_not_posted</c>, or
    /// <c>allocation_exceeds_balance_due</c>. Nothing is saved here: the caller commits.
    /// </summary>
    public Task<Result<SettledBill>> ApplyAsync(BillId billId, PayerId payerId, Money amount, CancellationToken ct);
}
