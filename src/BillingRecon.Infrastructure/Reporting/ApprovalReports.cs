using BillingRecon.Application.Common;
using BillingRecon.Application.Reporting;
using BillingRecon.Domain.Adjustments;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using BillingRecon.Domain.Payments;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Reporting;

/// <summary>
/// The maker-checker requests that wait (BR-901), the oldest first, read without tracking and filtered by the tenant of the
/// context (ADR 0036). For the page asked for, the payer and the amount of each request's document are read from the
/// document's own table: one query per kind of document on the page, and one for the names of the payers.
/// </summary>
public sealed class ApprovalReports(BillingReconDbContext context, IClock clock) : IApprovalReports
{
    /// <inheritdoc />
    public async Task<ReportPage<PendingApprovalDto, PendingApprovalsTotalsDto>> PendingApprovalsAsync(PendingApprovalsQuery query, DateOnly todayIst, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var pending = context.Set<ApprovalRequest>().AsNoTracking().Where(request => request.Status == ApprovalStatus.Pending);
        if (query.DocumentType is { } documentType)
        {
            pending = pending.Where(request => request.DocumentType == documentType);
        }

        var count = await pending.CountAsync(ct);
        var oldest = count == 0 ? null : (DateTimeOffset?)await pending.MinAsync(request => request.RequestedAtUtc, ct);
        var requests = await pending
            .OrderBy(request => request.RequestedAtUtc)
            .ThenBy(request => request.Id)
            .Skip(ReportMapping.Skip(query.Page, query.PageSize))
            .Take(query.PageSize)
            .ToListAsync(ct);

        var documents = await DocumentsAsync(requests, ct);
        var payerIds = documents.Values.Select(document => document.PayerId).Distinct().ToList();
        var payerNames = await context.Set<Payer>().AsNoTracking()
            .Where(payer => payerIds.Contains(payer.Id))
            .Select(payer => new { payer.Id, payer.Name })
            .ToDictionaryAsync(payer => payer.Id, payer => payer.Name, ct);

        PendingApprovalDto[] items =
        [
            .. requests.Select(request =>
            {
                var document = documents.GetValueOrDefault((request.DocumentType, request.DocumentId));

                return new PendingApprovalDto(
                    request.Id.Value,
                    request.DocumentType,
                    request.Action,
                    request.DocumentId,
                    request.MakerUserId.Value,
                    request.RequestedAtUtc,
                    todayIst.DayNumber - clock.ToIstDate(request.RequestedAtUtc).DayNumber,
                    document?.PayerId.Value,
                    document is null ? null : payerNames.GetValueOrDefault(document.PayerId),
                    document?.Amount.Amount);
            }),
        ];

        return new ReportPage<PendingApprovalDto, PendingApprovalsTotalsDto>(
            null, items, query.Page, query.PageSize, count, new PendingApprovalsTotalsDto(count, oldest));
    }

    // The payer and the amount of the documents of the page, by kind and id. A prefix change has neither.
    private async Task<Dictionary<(ApprovalDocumentType, Guid), DocumentFacts>> DocumentsAsync(IReadOnlyList<ApprovalRequest> requests, CancellationToken ct)
    {
        List<Guid> Ids(ApprovalDocumentType type) => [.. requests.Where(request => request.DocumentType == type).Select(request => request.DocumentId)];

        var facts = new Dictionary<(ApprovalDocumentType, Guid), DocumentFacts>();
        void Add(ApprovalDocumentType type, Guid id, PayerId payerId, Money amount) => facts[(type, id)] = new DocumentFacts(payerId, amount);

        var billIds = Ids(ApprovalDocumentType.Bill).Select(id => new BillId(id)).ToList();
        foreach (var bill in await context.Set<Bill>().AsNoTracking().Where(bill => billIds.Contains(bill.Id)).Select(bill => new { bill.Id, bill.PayerId, bill.Total }).ToListAsync(ct))
        {
            Add(ApprovalDocumentType.Bill, bill.Id.Value, bill.PayerId, bill.Total);
        }

        var creditNoteIds = Ids(ApprovalDocumentType.CreditNote).Select(id => new CreditNoteId(id)).ToList();
        foreach (var creditNote in await context.Set<CreditNote>().AsNoTracking().Where(creditNote => creditNoteIds.Contains(creditNote.Id)).Select(creditNote => new { creditNote.Id, creditNote.PayerId, creditNote.Total }).ToListAsync(ct))
        {
            Add(ApprovalDocumentType.CreditNote, creditNote.Id.Value, creditNote.PayerId, creditNote.Total);
        }

        var adjustmentIds = Ids(ApprovalDocumentType.Adjustment).Select(id => new AdjustmentId(id)).ToList();
        foreach (var adjustment in await context.Set<Adjustment>().AsNoTracking().Where(adjustment => adjustmentIds.Contains(adjustment.Id)).Select(adjustment => new { adjustment.Id, adjustment.PayerId, adjustment.Amount }).ToListAsync(ct))
        {
            Add(ApprovalDocumentType.Adjustment, adjustment.Id.Value, adjustment.PayerId, adjustment.Amount);
        }

        var refundIds = Ids(ApprovalDocumentType.Refund).Select(id => new RefundId(id)).ToList();
        foreach (var refund in await context.Set<Refund>().AsNoTracking().Where(refund => refundIds.Contains(refund.Id)).Select(refund => new { refund.Id, refund.PayerId, refund.Amount }).ToListAsync(ct))
        {
            Add(ApprovalDocumentType.Refund, refund.Id.Value, refund.PayerId, refund.Amount);
        }

        var paymentIds = Ids(ApprovalDocumentType.Payment).Select(id => new PaymentId(id)).ToList();
        foreach (var payment in await context.Set<Payment>().AsNoTracking().Where(payment => paymentIds.Contains(payment.Id)).Select(payment => new { payment.Id, payment.PayerId, payment.Amount }).ToListAsync(ct))
        {
            Add(ApprovalDocumentType.Payment, payment.Id.Value, payment.PayerId, payment.Amount);
        }

        var allocationIds = Ids(ApprovalDocumentType.PaymentAllocation).Select(id => new PaymentAllocationId(id)).ToList();
        var allocations = await (
                from allocation in context.Set<PaymentAllocation>().AsNoTracking()
                join payment in context.Set<Payment>().AsNoTracking() on allocation.PaymentId equals payment.Id
                where allocationIds.Contains(allocation.Id)
                select new { allocation.Id, payment.PayerId, allocation.Amount })
            .ToListAsync(ct);
        foreach (var allocation in allocations)
        {
            Add(ApprovalDocumentType.PaymentAllocation, allocation.Id.Value, allocation.PayerId, allocation.Amount);
        }

        var applicationIds = Ids(ApprovalDocumentType.CreditNoteAllocation).Select(id => new CreditNoteAllocationId(id)).ToList();
        var applications = await (
                from application in context.Set<CreditNoteAllocation>().AsNoTracking()
                join creditNote in context.Set<CreditNote>().AsNoTracking() on application.CreditNoteId equals creditNote.Id
                where applicationIds.Contains(application.Id)
                select new { application.Id, creditNote.PayerId, application.Amount })
            .ToListAsync(ct);
        foreach (var application in applications)
        {
            Add(ApprovalDocumentType.CreditNoteAllocation, application.Id.Value, application.PayerId, application.Amount);
        }

        return facts;
    }

    private sealed record DocumentFacts(PayerId PayerId, Money Amount);
}
