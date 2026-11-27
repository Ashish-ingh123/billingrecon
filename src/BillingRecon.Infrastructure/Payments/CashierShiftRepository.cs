using BillingRecon.Application.Payments;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Payments;

/// <summary>The shifts of the tenant, through the context and its tenant filter.</summary>
public sealed class CashierShiftRepository(BillingReconDbContext context) : ICashierShiftRepository
{
    /// <inheritdoc />
    public Task<CashierShift?> GetAsync(CashierShiftId id, CancellationToken ct)
        => context.Set<CashierShift>().SingleOrDefaultAsync(shift => shift.Id == id, ct);

    /// <inheritdoc />
    public Task<CashierShift?> FindOpenOfAsync(UserId cashier, CancellationToken ct)
        => context.Set<CashierShift>().SingleOrDefaultAsync(shift => shift.CashierUserId == cashier && shift.Status == CashierShiftStatus.Open, ct);

    /// <inheritdoc />
    public Task<bool> AnyUnclosedOfAsync(UserId cashier, CancellationToken ct)
        => context.Set<CashierShift>().AnyAsync(shift => shift.CashierUserId == cashier && shift.Status != CashierShiftStatus.Closed, ct);

    /// <inheritdoc />
    public void Add(CashierShift shift) => context.Set<CashierShift>().Add(shift);
}
