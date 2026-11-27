using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Payments;

/// <summary>
/// Lists shifts (ADR 0060). Who reads every shift is <see cref="ShiftReaders"/>; anyone else, and anyone who asks for
/// <see cref="ListCashierShiftsQuery.Mine"/>, gets the shifts they opened.
/// </summary>
public sealed class ListCashierShiftsHandler(
    IValidator<ListCashierShiftsQuery> validator,
    ICashierShiftReadService shifts,
    ICurrentUser currentUser) : IQueryHandler<ListCashierShiftsQuery, PageOf<CashierShiftDto>>
{
    /// <inheritdoc />
    public async Task<Result<PageOf<CashierShiftDto>>> HandleAsync(ListCashierShiftsQuery query, CancellationToken ct)
    {
        if (await validator.GetErrorAsync(query, ct) is { } invalid)
        {
            return invalid;
        }

        if (query.Mine || !ShiftReaders.SeeAll(currentUser.Roles))
        {
            // A request with no user has no shifts of its own: the empty id matches none.
            query = query with { CashierUserId = currentUser.UserId ?? new UserId(Guid.Empty) };
        }

        return await shifts.ListAsync(query, ct);
    }
}
