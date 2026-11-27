using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Receivables;

/// <summary>
/// What Receivables answers about the cash of a cashier's shift (BR-312; ADR 0060). A ledger posting names the shift
/// whose drawer it moved cash in or out of, so the expected cash of a shift is read from the ledger and from nowhere
/// else, and Payments, which owns the shift, never reads the ledger's tables.
/// </summary>
public interface IShiftCash
{
    /// <summary>The Cash entries of the postings that name <paramref name="shiftId"/>; <see cref="ShiftCash.None"/> when none does.</summary>
    public Task<ShiftCash> OfAsync(CashierShiftId shiftId, CancellationToken ct);

    /// <summary>The same for several shifts in one read; a shift no posting names is absent from the answer.</summary>
    public Task<IReadOnlyDictionary<CashierShiftId, ShiftCash>> OfAsync(IReadOnlyCollection<CashierShiftId> shiftIds, CancellationToken ct);

    /// <summary>The shift the posting <paramref name="postingId"/> names; null when it names none or does not exist.</summary>
    public Task<CashierShiftId?> ShiftOfAsync(LedgerPostingId postingId, CancellationToken ct);
}
