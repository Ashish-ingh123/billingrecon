using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Payments;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Payments;

/// <summary>
/// Reads shifts without tracking them. The cash of each comes from the ledger through the Receivables contract, the
/// name of its cashier through the Identity contract: neither table is read here (ADR 0003, ADR 0060).
/// </summary>
public sealed class CashierShiftReadService(BillingReconDbContext context, IShiftCash shiftCash, IUserNames userNames) : ICashierShiftReadService
{
    /// <inheritdoc />
    public async Task<CashierShiftDto?> GetAsync(CashierShiftId id, CancellationToken ct)
    {
        var shift = await context.Set<CashierShift>().AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == id, ct);

        return shift is null ? null : await shift.ToDtoAsync(shiftCash, userNames, ct);
    }

    /// <inheritdoc />
    public async Task<PageOf<CashierShiftDto>> ListAsync(ListCashierShiftsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var shifts = context.Set<CashierShift>().AsNoTracking();
        if (query.CashierUserId is { } cashier)
        {
            shifts = shifts.Where(shift => shift.CashierUserId == cashier);
        }

        if (query.Status is { } status)
        {
            shifts = shifts.Where(shift => shift.Status == status);
        }

        if (query.Unclosed)
        {
            shifts = shifts.Where(shift => shift.Status != CashierShiftStatus.Closed);
        }

        if (query.Date is { } date)
        {
            shifts = shifts.Where(shift => shift.BusinessDate == date);
        }

        // The filters of Prompt 12.2 (ADR 0063): each narrows the rows the search and the page are taken from.
        if (query.DateFrom is { } dateFrom)
        {
            shifts = shifts.Where(shift => shift.BusinessDate >= dateFrom);
        }

        if (query.DateTo is { } dateTo)
        {
            shifts = shifts.Where(shift => shift.BusinessDate <= dateTo);
        }

        var totalCount = await shifts.CountAsync(ct);
        // The order asked for by name, or the list's own; each ends on a key that is one of a kind (ADR 0063).
        IOrderedQueryable<CashierShift> ordered = query.Order switch
        {
            { Column: "businessDate", Descending: var down } => shifts.By(shift => shift.BusinessDate, down).Then(shift => shift.OpenedAtUtc, down).Then(shift => shift.Id, down),
            { Column: "difference", Descending: var down } => shifts.By(shift => shift.Difference, down).Then(shift => shift.Id, down),
            null => shifts
                .OrderByDescending(shift => shift.OpenedAtUtc)
                .ThenBy(shift => shift.Id),
            _ => throw new InvalidOperationException($"The order {query.Order} is not one of this list; its validator should have refused it."),
        };
        var page = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        var cash = await shiftCash.OfAsync([.. page.Select(shift => shift.Id)], ct);
        var names = new Dictionary<UserId, string?>();
        foreach (var user in page.Select(shift => shift.CashierUserId).Distinct())
        {
            names[user] = await userNames.FindDisplayNameAsync(user, ct);
        }

        return new PageOf<CashierShiftDto>(
            [.. page.Select(shift => shift.ToDto(cash.GetValueOrDefault(shift.Id, ShiftCash.None), names[shift.CashierUserId]))],
            query.Page,
            query.PageSize,
            totalCount);
    }
}
