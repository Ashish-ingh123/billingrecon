using BillingRecon.Application.Reporting;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using BillingRecon.Domain.Payments;
using BillingRecon.Domain.Receivables;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Reporting;

/// <summary>
/// What payers owe and what is held for them (US-40, BR-901), read without tracking and filtered by the tenant of the
/// context (ADR 0036). The figures are the cached balances of payers, payments and credit notes, which the posting that
/// changes the ledger changes in the same transaction (BR-404); the reconciliation report finds any difference.
/// </summary>
public sealed class BalanceReports(BillingReconDbContext context) : IBalanceReports
{
    /// <inheritdoc />
    public async Task<ReportPage<PayerOutstandingDto, OutstandingTotalsDto>> OutstandingByPayerAsync(OutstandingByPayerQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var balances =
            from balance in context.Set<PayerBalance>().AsNoTracking()
            join payer in context.Set<Payer>().AsNoTracking() on balance.PayerId equals payer.Id
            where balance.Receivable != Money.Zero || balance.UnappliedCash != Money.Zero || balance.CreditAvailable != Money.Zero
            select new
            {
                balance.PayerId,
                payer.Name,
                balance.Receivable,
                balance.UnappliedCash,
                balance.CreditAvailable,
                balance.NetOutstanding,
            };

        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            balances = balances.Where(balance => balance.Name.Contains(search));
        }

        var all = await balances.Select(balance => new { balance.Receivable, balance.UnappliedCash, balance.CreditAvailable, balance.NetOutstanding }).ToListAsync(ct);
        var totals = new OutstandingTotalsDto(
            all.Count,
            ReportMapping.Sum(all.Select(balance => balance.Receivable)),
            ReportMapping.Sum(all.Select(balance => balance.UnappliedCash)),
            ReportMapping.Sum(all.Select(balance => balance.CreditAvailable)),
            ReportMapping.Sum(all.Select(balance => balance.NetOutstanding)));

        // The order asked for by name, or the report's own: who owes most first (ADR 0063).
        var ordered = query.Sort switch
        {
            { Column: "name", Descending: var down } => balances.By(balance => balance.Name, down).Then(balance => balance.PayerId, down),
            { Column: "receivable", Descending: var down } => balances.By(balance => balance.Receivable, down).Then(balance => balance.PayerId, down),
            { Column: "netOutstanding", Descending: var down } => balances.By(balance => balance.NetOutstanding, down).Then(balance => balance.PayerId, down),
            null => balances.OrderByDescending(balance => balance.NetOutstanding).ThenBy(balance => balance.Name).ThenBy(balance => balance.PayerId),
            _ => throw new InvalidOperationException($"The order {query.Sort} is not one of this report; its validator should have refused it."),
        };
        var rows = await ordered
            .Skip(ReportMapping.Skip(query.Page, query.PageSize))
            .Take(query.PageSize)
            .ToListAsync(ct);

        PayerOutstandingDto[] items =
        [
            .. rows.Select(row => new PayerOutstandingDto(
                row.PayerId.Value,
                row.Name,
                row.Receivable.Amount,
                row.UnappliedCash.Amount,
                row.CreditAvailable.Amount,
                row.NetOutstanding.Amount)),
        ];

        return new ReportPage<PayerOutstandingDto, OutstandingTotalsDto>(null, items, query.Page, query.PageSize, totals.Payers, totals);
    }

    /// <inheritdoc />
    public async Task<PayerOutstandingDto> PayerBalanceAsync(PayerId payerId, string payerName, CancellationToken ct)
    {
        var balance = await context.Set<PayerBalance>().AsNoTracking().SingleOrDefaultAsync(candidate => candidate.PayerId == payerId, ct);

        // Every payer has its row since it was created (ADR 0005, built note); none would mean nothing was ever posted.
        return balance is null
            ? new PayerOutstandingDto(payerId.Value, payerName, 0m, 0m, 0m, 0m)
            : new PayerOutstandingDto(
                payerId.Value,
                payerName,
                balance.Receivable.Amount,
                balance.UnappliedCash.Amount,
                balance.CreditAvailable.Amount,
                balance.NetOutstanding.Amount);
    }

    /// <inheritdoc />
    public async Task<ReportPage<UnappliedPaymentDto, UnappliedCashTotalsDto>> UnappliedCashAsync(UnappliedCashQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var payments = context.Set<Payment>().AsNoTracking().Where(payment => payment.UnappliedAmount != Money.Zero);
        if (query.PayerId is { } payerId)
        {
            payments = payments.Where(payment => payment.PayerId == payerId);
        }

        // The filters of Prompt 12.2 (ADR 0063); the totals below are of the rows they leave.
        if (query.DateFrom is { } dateFrom)
        {
            payments = payments.Where(payment => payment.PaymentDate >= dateFrom);
        }

        if (query.DateTo is { } dateTo)
        {
            payments = payments.Where(payment => payment.PaymentDate <= dateTo);
        }

        var amounts = await payments.Select(payment => payment.UnappliedAmount).ToListAsync(ct);
        var totals = new UnappliedCashTotalsDto(amounts.Count, ReportMapping.Sum(amounts));

        var joined =
            from payment in payments
            join payer in context.Set<Payer>().AsNoTracking() on payment.PayerId equals payer.Id
            select new
            {
                payment.Id,
                payment.Number,
                payment.PaymentDate,
                payment.PayerId,
                PayerName = payer.Name,
                payment.Method,
                payment.Amount,
                payment.UnappliedAmount,
                payment.Status,
                payment.FiscalYear,
                payment.SequenceNumber,
            };

        // The order asked for by name, or the report's own, the oldest first (ADR 0063).
        var ordered = query.Sort switch
        {
            { Column: "number", Descending: var down } => joined.By(row => row.FiscalYear, down).Then(row => row.SequenceNumber, down).Then(row => row.Id, down),
            { Column: "paymentDate", Descending: var down } => joined.By(row => row.PaymentDate, down).Then(row => row.Id, down),
            { Column: "unappliedAmount", Descending: var down } => joined.By(row => row.UnappliedAmount, down).Then(row => row.Id, down),
            null => joined.OrderBy(row => row.PaymentDate).ThenBy(row => row.FiscalYear).ThenBy(row => row.SequenceNumber),
            _ => throw new InvalidOperationException($"The order {query.Sort} is not one of this report; its validator should have refused it."),
        };

        var rows = await ordered
            .Skip(ReportMapping.Skip(query.Page, query.PageSize))
            .Take(query.PageSize)
            .ToListAsync(ct);

        UnappliedPaymentDto[] items =
        [
            .. rows.Select(row => new UnappliedPaymentDto(
                row.Id.Value,
                row.Number,
                row.PaymentDate,
                row.PayerId.Value,
                row.PayerName,
                ReportMapping.ToReport(row.Method),
                row.Amount.Amount,
                row.UnappliedAmount.Amount,
                ReportMapping.ToReport(row.Status))),
        ];

        return new ReportPage<UnappliedPaymentDto, UnappliedCashTotalsDto>(null, items, query.Page, query.PageSize, totals.Payments, totals);
    }

    /// <inheritdoc />
    public async Task<ReportPage<AvailableCreditNoteDto, AvailableCreditTotalsDto>> AvailableCreditAsync(AvailableCreditQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var creditNotes = context.Set<CreditNote>().AsNoTracking()
            .Where(creditNote => creditNote.Status == CreditNoteStatus.Posted && creditNote.AvailableAmount != Money.Zero);
        if (query.PayerId is { } payerId)
        {
            creditNotes = creditNotes.Where(creditNote => creditNote.PayerId == payerId);
        }

        // The filters of Prompt 12.2 (ADR 0063); the totals below are of the rows they leave.
        if (query.DateFrom is { } dateFrom)
        {
            creditNotes = creditNotes.Where(creditNote => creditNote.CreditNoteDate >= dateFrom);
        }

        if (query.DateTo is { } dateTo)
        {
            creditNotes = creditNotes.Where(creditNote => creditNote.CreditNoteDate <= dateTo);
        }

        var amounts = await creditNotes.Select(creditNote => creditNote.AvailableAmount).ToListAsync(ct);
        var totals = new AvailableCreditTotalsDto(amounts.Count, ReportMapping.Sum(amounts));

        var joined =
            from creditNote in creditNotes
            join bill in context.Set<Bill>().AsNoTracking() on creditNote.BillId equals bill.Id
            join payer in context.Set<Payer>().AsNoTracking() on creditNote.PayerId equals payer.Id
            select new
            {
                creditNote.Id,
                creditNote.Number,
                creditNote.CreditNoteDate,
                creditNote.BillId,
                BillNumber = bill.Number,
                creditNote.PayerId,
                PayerName = payer.Name,
                creditNote.Total,
                creditNote.AvailableAmount,
                creditNote.FiscalYear,
                creditNote.SequenceNumber,
            };

        // The order asked for by name, or the report's own, the oldest first (ADR 0063).
        var ordered = query.Sort switch
        {
            { Column: "number", Descending: var down } => joined.By(row => row.FiscalYear, down).Then(row => row.SequenceNumber, down).Then(row => row.Id, down),
            { Column: "creditNoteDate", Descending: var down } => joined.By(row => row.CreditNoteDate, down).Then(row => row.Id, down),
            { Column: "availableAmount", Descending: var down } => joined.By(row => row.AvailableAmount, down).Then(row => row.Id, down),
            null => joined.OrderBy(row => row.CreditNoteDate).ThenBy(row => row.FiscalYear).ThenBy(row => row.SequenceNumber),
            _ => throw new InvalidOperationException($"The order {query.Sort} is not one of this report; its validator should have refused it."),
        };

        var rows = await ordered
            .Skip(ReportMapping.Skip(query.Page, query.PageSize))
            .Take(query.PageSize)
            .ToListAsync(ct);

        AvailableCreditNoteDto[] items =
        [
            .. rows.Select(row => new AvailableCreditNoteDto(
                row.Id.Value,
                row.Number!,
                row.CreditNoteDate,
                row.BillId.Value,
                row.BillNumber!,
                row.PayerId.Value,
                row.PayerName,
                row.Total.Amount,
                row.AvailableAmount.Amount)),
        ];

        return new ReportPage<AvailableCreditNoteDto, AvailableCreditTotalsDto>(null, items, query.Page, query.PageSize, totals.CreditNotes, totals);
    }
}
