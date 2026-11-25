using BillingRecon.Application.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Application.Payments;

/// <summary>Explicit mapping from the payment to its DTO (CLAUDE.md section 5).</summary>
public static class PaymentsMapping
{
    /// <summary>The payment as the API returns it.</summary>
    public static PaymentDto ToDto(this Payment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);

        return new PaymentDto(
            Id: payment.Id.Value,
            Number: payment.Number,
            FiscalYear: payment.FiscalYear.Label,
            PayerId: payment.PayerId.Value,
            PaymentDate: payment.PaymentDate,
            Method: payment.Method,
            Reference: payment.Reference,
            Amount: payment.Amount.Amount,
            UnappliedAmount: payment.UnappliedAmount.Amount,
            Status: payment.Status,
            Notes: payment.Notes,
            Origin: payment.Origin,
            LegacyNumber: payment.LegacyNumber,
            ReversedAtUtc: payment.ReversedAtUtc,
            ReversalReason: payment.ReversalReason,
            CreatedAtUtc: payment.CreatedAtUtc,
            UpdatedAtUtc: payment.UpdatedAtUtc,
            Version: RowVersionText.Of(payment));
    }

    /// <summary>The allocation as the API returns it.</summary>
    public static PaymentAllocationDto ToDto(this PaymentAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(allocation);

        return new PaymentAllocationDto(
            Id: allocation.Id.Value,
            BillId: allocation.BillId.Value,
            Amount: allocation.Amount.Amount,
            Status: allocation.Status,
            AllocatedAtUtc: allocation.AllocatedAtUtc,
            AllocatedByUserId: allocation.AllocatedByUserId?.Value,
            ReversedAtUtc: allocation.ReversedAtUtc,
            ReversalReason: allocation.ReversalReason);
    }

    /// <summary>The payment with its allocations, the oldest first; allocations of one instant in the order of their ids.</summary>
    public static PaymentAllocationsDto ToDtoWithAllocations(this Payment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);

        return new PaymentAllocationsDto(
            payment.ToDto(),
            [.. payment.Allocations.OrderBy(allocation => allocation.AllocatedAtUtc).ThenBy(allocation => allocation.Id.Value).Select(allocation => allocation.ToDto())]);
    }
}
