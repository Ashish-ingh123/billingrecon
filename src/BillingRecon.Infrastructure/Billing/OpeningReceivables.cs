using BillingRecon.Application.Billing;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Billing;

/// <summary>
/// The opening receivables of an opening batch (ADR 0053): posted bills of the opening series, added to the context of
/// the approval, which saves them with the batch.
/// </summary>
public sealed class OpeningReceivables(BillingReconDbContext context) : IOpeningReceivables
{
    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> FindBroughtForwardAsync(IReadOnlyCollection<string> legacyNumbers, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(legacyNumbers);

        var wanted = legacyNumbers.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (wanted.Count == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var found = await context.Set<Bill>()
            .AsNoTracking()
            .Where(bill => bill.LegacyNumber != null && wanted.Contains(bill.LegacyNumber))
            .Select(bill => bill.LegacyNumber!)
            .ToListAsync(ct);

        return found.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public BillId BringForward(OpeningReceivable receivable, DocumentNumber number, DateTimeOffset postedAtUtc, UserId? postedBy)
    {
        ArgumentNullException.ThrowIfNull(receivable);

        var bill = Bill.BringForward(
            receivable.PatientId,
            receivable.PayerId,
            receivable.BillDate,
            receivable.DueDate,
            receivable.Outstanding,
            receivable.LegacyNumber,
            number,
            postedAtUtc,
            postedBy);

        // Adding the bill gives it its id.
        context.Set<Bill>().Add(bill);

        return bill.Id;
    }
}
