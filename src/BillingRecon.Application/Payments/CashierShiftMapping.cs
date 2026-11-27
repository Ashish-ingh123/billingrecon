using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Application.Payments;

/// <summary>Maps a shift to its DTO. Explicit and by hand (CLAUDE.md, section 5).</summary>
public static class CashierShiftMapping
{
    /// <summary>The shift with the cash the ledger has for it and the name of its cashier.</summary>
    public static CashierShiftDto ToDto(this CashierShift shift, ShiftCash cash, string? cashierName)
    {
        ArgumentNullException.ThrowIfNull(shift);
        ArgumentNullException.ThrowIfNull(cash);

        return new CashierShiftDto(
            Id: shift.Id.Value,
            CashierUserId: shift.CashierUserId.Value,
            CashierName: cashierName,
            BusinessDate: shift.BusinessDate,
            Status: shift.Status,
            OpeningFloat: shift.OpeningFloat.Amount,
            OpenedAtUtc: shift.OpenedAtUtc,
            CashDocuments: shift.CashDocuments,
            CashReceived: cash.Received.Amount,
            CashPaidOut: cash.PaidOut.Amount,
            ExpectedCash: (shift.OpeningFloat + cash.Movement).Amount,
            Count: shift.Denominations is { } count ? [.. count.Pieces().Select(piece => new CashCountLineDto(piece.Denomination, piece.Pieces))] : null,
            LooseChange: shift.LooseChange?.Amount,
            CountedCash: shift.CountedCash?.Amount,
            Difference: shift.Difference?.Amount,
            DifferenceReason: shift.DifferenceReason,
            CountedAtUtc: shift.CountedAtUtc,
            ClosedAtUtc: shift.ClosedAtUtc,
            Version: RowVersionText.Of(shift));
    }

    /// <summary>The shift as it is after a use case committed: its cash read from the ledger, its cashier by name.</summary>
    public static async Task<CashierShiftDto> ToDtoAsync(this CashierShift shift, IShiftCash shiftCash, IUserNames userNames, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(shift);
        ArgumentNullException.ThrowIfNull(shiftCash);
        ArgumentNullException.ThrowIfNull(userNames);

        return shift.ToDto(await shiftCash.OfAsync(shift.Id, ct), await userNames.FindDisplayNameAsync(shift.CashierUserId, ct));
    }
}
