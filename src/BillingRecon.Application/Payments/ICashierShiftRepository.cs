using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Application.Payments;

/// <summary>The shifts of the tenant's cashiers, for the use cases that change one (ADR 0060).</summary>
public interface ICashierShiftRepository
{
    /// <summary>The shift with that id, tracked; null when the tenant has none.</summary>
    public Task<CashierShift?> GetAsync(CashierShiftId id, CancellationToken ct);

    /// <summary>The open shift of <paramref name="cashier"/>, tracked; null when the user has none (BR-310: at most one).</summary>
    public Task<CashierShift?> FindOpenOfAsync(UserId cashier, CancellationToken ct);

    /// <summary>Whether <paramref name="cashier"/> has a shift that is open or waits for approval (BR-310).</summary>
    public Task<bool> AnyUnclosedOfAsync(UserId cashier, CancellationToken ct);

    /// <summary>Makes a new shift part of the unit of work, which gives it its id.</summary>
    public void Add(CashierShift shift);
}
