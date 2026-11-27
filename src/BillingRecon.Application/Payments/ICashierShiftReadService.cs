using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payments;

/// <summary>Reads shifts with the cash the ledger has for each (ADR 0060). It never tracks or changes one.</summary>
public interface ICashierShiftReadService
{
    /// <summary>The shift with that id; null when the tenant has none.</summary>
    public Task<CashierShiftDto?> GetAsync(CashierShiftId id, CancellationToken ct);

    /// <summary>A page of shifts, the newest first.</summary>
    public Task<PageOf<CashierShiftDto>> ListAsync(ListCashierShiftsQuery query, CancellationToken ct);
}
