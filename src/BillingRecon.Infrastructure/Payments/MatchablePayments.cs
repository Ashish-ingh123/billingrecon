using BillingRecon.Application.Payments;
using BillingRecon.Domain.Payments;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Payments;

/// <summary>
/// The payments and refunds the matching of statements may see (<see cref="IMatchablePayments"/>, ADR 0066), read
/// untracked through the request's context, filtered by its tenant. Payments' own entities stay inside this module.
/// </summary>
public sealed class MatchablePayments(BillingReconDbContext context) : IMatchablePayments
{
    private static readonly PaymentMethod[] BankMethods = [PaymentMethod.BankTransfer, PaymentMethod.Cheque];
    private static readonly PaymentMethod[] AllButCash = [PaymentMethod.BankTransfer, PaymentMethod.Cheque, PaymentMethod.UPI, PaymentMethod.Card];

    /// <inheritdoc />
    public async Task<IReadOnlyList<MatchableDocument>> PaymentsAsync(DateOnly from, DateOnly to, bool withUpiAndCard, CancellationToken ct)
    {
        var methods = withUpiAndCard ? AllButCash : BankMethods;
        var payments = await context.Set<Payment>().AsNoTracking()
            .Where(payment => payment.Status != PaymentStatus.Reversed && methods.Contains(payment.Method))
            .Where(payment => payment.PaymentDate >= from && payment.PaymentDate <= to)
            .Select(payment => new { payment.Id, payment.Number, payment.PayerId, payment.PaymentDate, payment.Method, payment.Reference, payment.Amount })
            .ToListAsync(ct);

        return [.. payments.Select(payment => new MatchableDocument(
            payment.Id.Value, payment.Number, payment.PayerId.Value, payment.PaymentDate, payment.Method.ToString(), payment.Reference, payment.Amount.Amount))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MatchableDocument>> RefundsAsync(DateOnly from, DateOnly to, bool withUpiAndCard, CancellationToken ct)
    {
        var methods = withUpiAndCard ? AllButCash : BankMethods;
        var refunds = await context.Set<Refund>().AsNoTracking()
            .Where(refund => refund.Status == RefundStatus.Posted && methods.Contains(refund.Method))
            .Where(refund => refund.RefundDate >= from && refund.RefundDate <= to)
            .Select(refund => new { refund.Id, refund.Number, refund.PayerId, refund.RefundDate, refund.Method, refund.Reference, refund.Amount })
            .ToListAsync(ct);

        return [.. refunds.Select(refund => new MatchableDocument(
            refund.Id.Value, refund.Number ?? string.Empty, refund.PayerId.Value, refund.RefundDate, refund.Method.ToString(), refund.Reference, refund.Amount.Amount))];
    }
}
