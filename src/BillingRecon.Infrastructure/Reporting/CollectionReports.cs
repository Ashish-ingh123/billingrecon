using BillingRecon.Application.Reporting;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using BillingRecon.Domain.Receivables;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Reporting;

/// <summary>
/// The collection of a business date (US-42), taken from the ledger (ADR 0036): the Cash entries of that date's postings
/// that record a payment (received), reverse one (reversed) or post a refund (refunded), each counted under the method of
/// its payment or refund. So its net is the movement of the Cash account on that date by construction.
/// </summary>
public sealed class CollectionReports(BillingReconDbContext context) : ICollectionReports
{
    /// <inheritdoc />
    public async Task<DailyCollectionDto> DailyCollectionAsync(DateOnly date, CancellationToken ct)
    {
        var cash = await (
                from posting in context.Set<LedgerPosting>().AsNoTracking()
                join entry in context.Set<LedgerEntry>().AsNoTracking() on posting.Id equals entry.PostingId
                where posting.BusinessDate == date
                    && (posting.Kind == LedgerPostingKind.PaymentRecorded || posting.Kind == LedgerPostingKind.PaymentReversed || posting.Kind == LedgerPostingKind.RefundPosted)
                    && entry.Account == LedgerAccount.Cash
                select new { posting.Kind, posting.DocumentId, entry.Debit, entry.Credit })
            .ToListAsync(ct);

        var paymentIds = cash.Where(line => line.Kind != LedgerPostingKind.RefundPosted).Select(line => new PaymentId(line.DocumentId)).Distinct().ToList();
        var refundIds = cash.Where(line => line.Kind == LedgerPostingKind.RefundPosted).Select(line => new RefundId(line.DocumentId)).Distinct().ToList();

        var paymentMethods = await context.Set<Payment>().AsNoTracking()
            .Where(payment => paymentIds.Contains(payment.Id))
            .Select(payment => new { payment.Id, payment.Method })
            .ToDictionaryAsync(payment => payment.Id.Value, payment => payment.Method, ct);
        var refundMethods = await context.Set<Refund>().AsNoTracking()
            .Where(refund => refundIds.Contains(refund.Id))
            .Select(refund => new { refund.Id, refund.Method })
            .ToDictionaryAsync(refund => refund.Id.Value, refund => refund.Method, ct);

        var byMethod = Enum.GetValues<ReportPaymentMethod>().ToDictionary(method => method, _ => new Tally());
        foreach (var line in cash)
        {
            var methods = line.Kind == LedgerPostingKind.RefundPosted ? refundMethods : paymentMethods;
            var tally = byMethod[ReportMapping.ToReport(methods[line.DocumentId])];
            switch (line.Kind)
            {
                case LedgerPostingKind.PaymentRecorded:
                    tally.Payments++;
                    tally.Received += line.Debit.Amount - line.Credit.Amount;
                    break;
                case LedgerPostingKind.PaymentReversed:
                    tally.Reversed += line.Credit.Amount - line.Debit.Amount;
                    break;
                default:
                    tally.Refunded += line.Credit.Amount - line.Debit.Amount;
                    break;
            }
        }

        CollectionByMethodDto[] rows =
        [
            .. byMethod.Select(pair => new CollectionByMethodDto(
                pair.Key,
                pair.Value.Payments,
                pair.Value.Received,
                pair.Value.Reversed,
                pair.Value.Refunded,
                pair.Value.Received - pair.Value.Reversed - pair.Value.Refunded)),
        ];

        var totals = new CollectionTotalsDto(
            rows.Sum(row => row.Payments),
            rows.Sum(row => row.Received),
            rows.Sum(row => row.Reversed),
            rows.Sum(row => row.Refunded),
            rows.Sum(row => row.Net));

        return new DailyCollectionDto(date, rows, totals);
    }

    /// <inheritdoc />
    public async Task<DayEndDto> DayEndAsync(DateOnly date, CancellationToken ct)
    {
        var shifts = await context.Set<CashierShift>().AsNoTracking()
            .Where(shift => shift.BusinessDate == date)
            .OrderBy(shift => shift.OpenedAtUtc)
            .ThenBy(shift => shift.Id)
            .ToListAsync(ct);

        // The same entries the daily collection reads, with the shift each posting names.
        var entries = await (
                from posting in context.Set<LedgerPosting>().AsNoTracking()
                join entry in context.Set<LedgerEntry>().AsNoTracking() on posting.Id equals entry.PostingId
                where posting.BusinessDate == date
                    && (posting.Kind == LedgerPostingKind.PaymentRecorded || posting.Kind == LedgerPostingKind.PaymentReversed || posting.Kind == LedgerPostingKind.RefundPosted)
                    && entry.Account == LedgerAccount.Cash
                select new { posting.Kind, posting.DocumentId, posting.ShiftId, entry.Debit, entry.Credit })
            .ToListAsync(ct);

        var paymentIds = entries.Where(line => line.Kind != LedgerPostingKind.RefundPosted).Select(line => new PaymentId(line.DocumentId)).Distinct().ToList();
        var refundIds = entries.Where(line => line.Kind == LedgerPostingKind.RefundPosted).Select(line => new RefundId(line.DocumentId)).Distinct().ToList();

        // Only money in notes and coins is the cash of a drawer: the documents whose method is Cash.
        var cashPayments = (await context.Set<Payment>().AsNoTracking()
            .Where(payment => paymentIds.Contains(payment.Id) && payment.Method == PaymentMethod.Cash)
            .Select(payment => payment.Id)
            .ToListAsync(ct)).Select(id => id.Value).ToHashSet();
        var cashRefunds = (await context.Set<Refund>().AsNoTracking()
            .Where(refund => refundIds.Contains(refund.Id) && refund.Method == PaymentMethod.Cash)
            .Select(refund => refund.Id)
            .ToListAsync(ct)).Select(id => id.Value).ToHashSet();

        var ofShifts = shifts.ToDictionary(shift => shift.Id, _ => new Drawer());
        var outside = new Drawer();
        foreach (var line in entries)
        {
            if (!(line.Kind == LedgerPostingKind.RefundPosted ? cashRefunds : cashPayments).Contains(line.DocumentId))
            {
                continue;
            }

            // A posting names a shift of its own business date (BR-311); anything else is cash outside the shifts.
            var drawer = line.ShiftId is { } shiftId && ofShifts.TryGetValue(shiftId, out var named) ? named : outside;
            drawer.Received += line.Debit.Amount;
            drawer.PaidOut += line.Credit.Amount;
        }

        DayEndShiftDto[] rows =
        [
            .. shifts.Select(shift =>
            {
                var drawer = ofShifts[shift.Id];
                return new DayEndShiftDto(
                    shift.Id.Value,
                    shift.CashierUserId.Value,
                    CashierName: null,
                    ReportMapping.ToReport(shift.Status),
                    shift.OpenedAtUtc,
                    shift.ClosedAtUtc,
                    shift.OpeningFloat.Amount,
                    drawer.Received,
                    drawer.PaidOut,
                    drawer.Received - drawer.PaidOut,
                    shift.OpeningFloat.Amount + drawer.Received - drawer.PaidOut,
                    shift.CountedCash?.Amount,
                    shift.Difference?.Amount);
            }),
        ];

        var shiftsTotal = new DayEndCashDto(rows.Sum(row => row.Received), rows.Sum(row => row.PaidOut), rows.Sum(row => row.Net));
        var outsideShifts = new DayEndCashDto(outside.Received, outside.PaidOut, outside.Received - outside.PaidOut);

        return new DayEndDto(
            date,
            rows,
            shiftsTotal,
            outsideShifts,
            new DayEndCashDto(shiftsTotal.Received + outsideShifts.Received, shiftsTotal.PaidOut + outsideShifts.PaidOut, shiftsTotal.Net + outsideShifts.Net),
            rows.Sum(row => row.Difference ?? 0m),
            rows.Count(row => row.Status != ReportShiftStatus.Closed));
    }

    private sealed class Drawer
    {
        public decimal Received { get; set; }

        public decimal PaidOut { get; set; }
    }

    private sealed class Tally
    {
        public int Payments { get; set; }

        public decimal Received { get; set; }

        public decimal Reversed { get; set; }

        public decimal Refunded { get; set; }
    }
}
