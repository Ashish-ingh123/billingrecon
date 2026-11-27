using BillingRecon.Application.Reporting;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using BillingRecon.Domain.Payments;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Reporting;

/// <summary>
/// The bill and payment registers (BR-901), read from the tables of Billing, Payments and Payers without tracking and
/// filtered by the tenant of the context (ADR 0036). Chronological: by date, then by number within a day.
/// </summary>
public sealed class RegisterReports(BillingReconDbContext context) : IRegisterReports
{
    /// <inheritdoc />
    public async Task<ReportPage<BillRegisterRowDto, BillRegisterTotalsDto>> BillRegisterAsync(ReportPeriodDto period, BillRegisterQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(query);

        // A bill is in the register once it is posted: it has its number and one of the three states of a posted bill.
        var bills = context.Set<Bill>().AsNoTracking()
            .Where(bill => bill.Number != null && bill.BillDate >= period.From && bill.BillDate <= period.To)
            .Where(bill => bill.Status == BillStatus.Posted || bill.Status == BillStatus.PartiallyPaid || bill.Status == BillStatus.Settled);

        if (query.PayerId is { } payerId)
        {
            bills = bills.Where(bill => bill.PayerId == payerId);
        }

        if (query.Status is { } status)
        {
            var billStatus = ReportMapping.ToBill(status);
            bills = bills.Where(bill => bill.Status == billStatus);
        }

        // The filters of Prompt 12.2 (ADR 0063); the totals below are of the rows they leave.
        if (query.TotalFrom is { } totalFrom)
        {
            bills = bills.Where(bill => bill.Total >= Money.Of(totalFrom));
        }

        if (query.TotalTo is { } totalTo)
        {
            bills = bills.Where(bill => bill.Total <= Money.Of(totalTo));
        }

        var amounts = await bills.Select(bill => new { bill.Total, bill.BalanceDue }).ToListAsync(ct);
        var totals = new BillRegisterTotalsDto(
            amounts.Count,
            ReportMapping.Sum(amounts.Select(amount => amount.Total)),
            ReportMapping.Sum(amounts.Select(amount => amount.BalanceDue)));

        var joined =
            from bill in bills
            join patient in context.Set<Patient>().AsNoTracking() on bill.PatientId equals patient.Id
            join payer in context.Set<Payer>().AsNoTracking() on bill.PayerId equals payer.Id
            select new
            {
                bill.Id,
                bill.Number,
                bill.BillDate,
                bill.DueDate,
                bill.PatientId,
                PatientName = patient.FullName,
                bill.PayerId,
                PayerName = payer.Name,
                bill.Total,
                bill.BalanceDue,
                bill.Status,
                bill.PostedAtUtc,
                bill.FiscalYear,
                bill.SequenceNumber,
            };

        // The order asked for by name, or the register's own; each ends on a key that is one of a kind (ADR 0063).
        var ordered = query.Sort switch
        {
            { Column: "number", Descending: var down } => joined.By(row => row.FiscalYear, down).Then(row => row.SequenceNumber, down).Then(row => row.Id, down),
            { Column: "billDate", Descending: var down } => joined.By(row => row.BillDate, down).Then(row => row.Id, down),
            { Column: "total", Descending: var down } => joined.By(row => row.Total, down).Then(row => row.Id, down),
            { Column: "balanceDue", Descending: var down } => joined.By(row => row.BalanceDue, down).Then(row => row.Id, down),
            null => joined.OrderBy(row => row.BillDate).ThenBy(row => row.FiscalYear).ThenBy(row => row.SequenceNumber),
            _ => throw new InvalidOperationException($"The order {query.Sort} is not one of this report; its validator should have refused it."),
        };

        var rows = await ordered
            .Skip(ReportMapping.Skip(query.Page, query.PageSize))
            .Take(query.PageSize)
            .ToListAsync(ct);

        BillRegisterRowDto[] items =
        [
            .. rows.Select(row => new BillRegisterRowDto(
                row.Id.Value,
                row.Number!,
                row.BillDate,
                row.DueDate,
                row.PatientId.Value,
                row.PatientName,
                row.PayerId.Value,
                row.PayerName,
                row.Total.Amount,
                row.BalanceDue.Amount,
                ReportMapping.ToReport(row.Status),
                row.PostedAtUtc ?? throw new InvalidOperationException($"The posted bill {row.Number} has no instant of posting."))),
        ];

        return new ReportPage<BillRegisterRowDto, BillRegisterTotalsDto>(period, items, query.Page, query.PageSize, totals.Bills, totals);
    }

    /// <inheritdoc />
    public async Task<ReportPage<PaymentRegisterRowDto, PaymentRegisterTotalsDto>> PaymentRegisterAsync(ReportPeriodDto period, PaymentRegisterQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(query);

        var payments = context.Set<Payment>().AsNoTracking()
            .Where(payment => payment.PaymentDate >= period.From && payment.PaymentDate <= period.To);

        if (query.PayerId is { } payerId)
        {
            payments = payments.Where(payment => payment.PayerId == payerId);
        }

        if (query.Method is { } method)
        {
            var paymentMethod = ReportMapping.ToPayment(method);
            payments = payments.Where(payment => payment.Method == paymentMethod);
        }

        if (query.Status is { } status)
        {
            var paymentStatus = ReportMapping.ToPayment(status);
            payments = payments.Where(payment => payment.Status == paymentStatus);
        }

        // The filters of Prompt 12.2 (ADR 0063); the totals below are of the rows they leave.
        if (query.AmountFrom is { } amountFrom)
        {
            payments = payments.Where(payment => payment.Amount >= Money.Of(amountFrom));
        }

        if (query.AmountTo is { } amountTo)
        {
            payments = payments.Where(payment => payment.Amount <= Money.Of(amountTo));
        }

        var amounts = await payments.Select(payment => new { payment.Amount, payment.UnappliedAmount }).ToListAsync(ct);
        var totals = new PaymentRegisterTotalsDto(
            amounts.Count,
            ReportMapping.Sum(amounts.Select(amount => amount.Amount)),
            ReportMapping.Sum(amounts.Select(amount => amount.UnappliedAmount)));

        var joined =
            from payment in payments
            join payer in context.Set<Payer>().AsNoTracking() on payment.PayerId equals payer.Id
            select new
            {
                payment.Id,
                payment.Number,
                payment.PaymentDate,
                payment.FiscalYear,
                payment.SequenceNumber,
                payment.PayerId,
                PayerName = payer.Name,
                payment.Method,
                payment.Reference,
                payment.Amount,
                payment.UnappliedAmount,
                payment.Status,
                payment.ReversedAtUtc,
            };

        // The order asked for by name (ADR 0063); or chronological, or the newest first for the dashboard's recent
        // payments (ADR 0037).
        var ordered = query.Sort switch
        {
            { Column: "number", Descending: var down } => joined.By(row => row.FiscalYear, down).Then(row => row.SequenceNumber, down).Then(row => row.Id, down),
            { Column: "paymentDate", Descending: var down } => joined.By(row => row.PaymentDate, down).Then(row => row.Id, down),
            { Column: "amount", Descending: var down } => joined.By(row => row.Amount, down).Then(row => row.Id, down),
            { Column: "unappliedAmount", Descending: var down } => joined.By(row => row.UnappliedAmount, down).Then(row => row.Id, down),
            null => query.Order == ReportOrder.NewestFirst
                ? joined.OrderByDescending(row => row.PaymentDate).ThenByDescending(row => row.FiscalYear).ThenByDescending(row => row.SequenceNumber)
                : joined.OrderBy(row => row.PaymentDate).ThenBy(row => row.FiscalYear).ThenBy(row => row.SequenceNumber),
            _ => throw new InvalidOperationException($"The order {query.Sort} is not one of this report; its validator should have refused it."),
        };

        var rows = await ordered
            .Skip(ReportMapping.Skip(query.Page, query.PageSize))
            .Take(query.PageSize)
            .ToListAsync(ct);

        PaymentRegisterRowDto[] items =
        [
            .. rows.Select(row => new PaymentRegisterRowDto(
                row.Id.Value,
                row.Number,
                row.PaymentDate,
                row.PayerId.Value,
                row.PayerName,
                ReportMapping.ToReport(row.Method),
                row.Reference,
                row.Amount.Amount,
                row.UnappliedAmount.Amount,
                ReportMapping.ToReport(row.Status),
                row.ReversedAtUtc)),
        ];

        return new ReportPage<PaymentRegisterRowDto, PaymentRegisterTotalsDto>(period, items, query.Page, query.PageSize, totals.Payments, totals);
    }
}
