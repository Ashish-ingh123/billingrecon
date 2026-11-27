using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Receivables;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Receivables;

/// <summary>
/// The cash of a shift, read from the ledger (BR-312; ADR 0060): the Cash entries of the postings that name it. Plain
/// queries through the context and its tenant filter; the amounts are summed here, exactly, as decimals.
/// </summary>
public sealed class ShiftCashQuery(BillingReconDbContext context) : IShiftCash
{
    /// <inheritdoc />
    public async Task<ShiftCash> OfAsync(CashierShiftId shiftId, CancellationToken ct)
        => (await OfAsync([shiftId], ct)).GetValueOrDefault(shiftId, ShiftCash.None);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<CashierShiftId, ShiftCash>> OfAsync(IReadOnlyCollection<CashierShiftId> shiftIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(shiftIds);

        if (shiftIds.Count == 0)
        {
            return new Dictionary<CashierShiftId, ShiftCash>();
        }

        List<CashierShiftId?> asked = [.. shiftIds.Select(id => (CashierShiftId?)id)];
        var lines = await (
                from posting in context.Set<LedgerPosting>().AsNoTracking()
                join entry in context.Set<LedgerEntry>().AsNoTracking() on posting.Id equals entry.PostingId
                where asked.Contains(posting.ShiftId) && entry.Account == LedgerAccount.Cash
                select new { posting.ShiftId, entry.Debit, entry.Credit })
            .ToListAsync(ct);

        return lines
            .GroupBy(line => line.ShiftId!.Value)
            .ToDictionary(
                shift => shift.Key,
                shift => new ShiftCash(
                    Money.Of(shift.Sum(line => line.Debit.Amount)),
                    Money.Of(shift.Sum(line => line.Credit.Amount))));
    }

    /// <inheritdoc />
    public Task<CashierShiftId?> ShiftOfAsync(LedgerPostingId postingId, CancellationToken ct)
        => context.Set<LedgerPosting>().AsNoTracking()
            .Where(posting => posting.Id == postingId)
            .Select(posting => posting.ShiftId)
            .SingleOrDefaultAsync(ct);
}
