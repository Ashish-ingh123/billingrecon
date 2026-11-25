using BillingRecon.Application.Common;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Reporting;

/// <summary>
/// Answers whether a bill, a payment or a refund can be printed (ADR 0054) by reading its status. It is in Reporting,
/// the one folder that may name the entities of every business module and that writes nothing (ADR 0036): the use case
/// that records a print is the same for the three kinds and belongs to none of their modules.
/// </summary>
public sealed class PrintableDocuments(BillingReconDbContext context) : IPrintableDocuments
{
    /// <inheritdoc />
    public async Task<Result> EnsurePrintableAsync(PrintedDocumentType documentType, Guid documentId, CancellationToken ct)
    {
        switch (documentType)
        {
            case PrintedDocumentType.Bill:
            {
                var id = new BillId(documentId);
                var status = await context.Set<Bill>().AsNoTracking().Where(bill => bill.Id == id).Select(bill => (BillStatus?)bill.Status).SingleOrDefaultAsync(ct);
                return status switch
                {
                    null => BillingErrors.BillNotFound(),
                    BillStatus.Posted or BillStatus.PartiallyPaid or BillStatus.Settled => Result.Success(),
                    _ => CommonErrors.DocumentNotPrintable(),
                };
            }

            case PrintedDocumentType.Payment:
            {
                var id = new PaymentId(documentId);
                var status = await context.Set<Payment>().AsNoTracking().Where(payment => payment.Id == id).Select(payment => (PaymentStatus?)payment.Status).SingleOrDefaultAsync(ct);
                return status switch
                {
                    null => PaymentsErrors.PaymentNotFound(),
                    PaymentStatus.Reversed => CommonErrors.DocumentNotPrintable(),
                    _ => Result.Success(),
                };
            }

            case PrintedDocumentType.Refund:
            {
                var id = new RefundId(documentId);
                var status = await context.Set<Refund>().AsNoTracking().Where(refund => refund.Id == id).Select(refund => (RefundStatus?)refund.Status).SingleOrDefaultAsync(ct);
                return status switch
                {
                    null => PaymentsErrors.RefundNotFound(),
                    RefundStatus.Posted => Result.Success(),
                    _ => CommonErrors.DocumentNotPrintable(),
                };
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(documentType), documentType, "Not a kind of document that is printed.");
        }
    }
}
