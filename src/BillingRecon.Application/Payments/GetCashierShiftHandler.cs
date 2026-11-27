using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Application.Payments;

/// <summary>
/// Reads one shift with the cash the ledger has for it (ADR 0060). A user who reads only their own shifts
/// (<see cref="ShiftReaders"/>) gets 404 for the shift of another cashier.
/// </summary>
public sealed class GetCashierShiftHandler(ICashierShiftReadService shifts, ICurrentUser currentUser) : IQueryHandler<GetCashierShiftQuery, CashierShiftDto>
{
    /// <inheritdoc />
    public async Task<Result<CashierShiftDto>> HandleAsync(GetCashierShiftQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var shift = await shifts.GetAsync(query.Id, ct);
        if (shift is null)
        {
            return PaymentsErrors.ShiftNotFound();
        }

        return ShiftReaders.SeeAll(currentUser.Roles) || currentUser.UserId?.Value == shift.CashierUserId
            ? shift
            : PaymentsErrors.ShiftNotFound();
    }
}
