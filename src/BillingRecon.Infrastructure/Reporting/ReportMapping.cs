using BillingRecon.Application.Reporting;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Infrastructure.Reporting;

/// <summary>
/// The words of the other modules in the words of Reporting (ADR 0036), both ways, written out: no reflection. A value a
/// report never meets (a draft bill) is a bug and throws.
/// </summary>
internal static class ReportMapping
{
    public static ReportBillStatus ToReport(BillStatus status) => status switch
    {
        BillStatus.Posted => ReportBillStatus.Posted,
        BillStatus.PartiallyPaid => ReportBillStatus.PartiallyPaid,
        BillStatus.Settled => ReportBillStatus.Settled,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Only a posted bill is in the register."),
    };

    public static BillStatus ToBill(ReportBillStatus status) => status switch
    {
        ReportBillStatus.Posted => BillStatus.Posted,
        ReportBillStatus.PartiallyPaid => BillStatus.PartiallyPaid,
        ReportBillStatus.Settled => BillStatus.Settled,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Not a state of the register."),
    };

    public static ReportPaymentStatus ToReport(PaymentStatus status) => status switch
    {
        PaymentStatus.Posted => ReportPaymentStatus.Posted,
        PaymentStatus.PendingReversal => ReportPaymentStatus.PendingReversal,
        PaymentStatus.Reversed => ReportPaymentStatus.Reversed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Not a state of a payment."),
    };

    public static PaymentStatus ToPayment(ReportPaymentStatus status) => status switch
    {
        ReportPaymentStatus.Posted => PaymentStatus.Posted,
        ReportPaymentStatus.PendingReversal => PaymentStatus.PendingReversal,
        ReportPaymentStatus.Reversed => PaymentStatus.Reversed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Not a state of a payment."),
    };

    public static ReportPaymentMethod ToReport(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => ReportPaymentMethod.Cash,
        PaymentMethod.Card => ReportPaymentMethod.Card,
        PaymentMethod.UPI => ReportPaymentMethod.UPI,
        PaymentMethod.BankTransfer => ReportPaymentMethod.BankTransfer,
        PaymentMethod.Cheque => ReportPaymentMethod.Cheque,
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Not a payment method."),
    };

    public static PaymentMethod ToPayment(ReportPaymentMethod method) => method switch
    {
        ReportPaymentMethod.Cash => PaymentMethod.Cash,
        ReportPaymentMethod.Card => PaymentMethod.Card,
        ReportPaymentMethod.UPI => PaymentMethod.UPI,
        ReportPaymentMethod.BankTransfer => PaymentMethod.BankTransfer,
        ReportPaymentMethod.Cheque => PaymentMethod.Cheque,
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Not a payment method."),
    };

    /// <summary>
    /// The sum of amounts already rounded at their documents (BR-001): totals are added in memory, because the database
    /// cannot sum a value object it stores through a converter (ADR 0036, built note).
    /// </summary>
    public static decimal Sum(IEnumerable<Money> amounts) => amounts.Sum(amount => amount.Amount);

    /// <summary>The rows to skip for a page.</summary>
    public static int Skip(int page, int pageSize) => (page - 1) * pageSize;
}
