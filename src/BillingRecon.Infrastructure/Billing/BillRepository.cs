using BillingRecon.Application.Billing;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Billing;

/// <summary>
/// Loads bills for a change and adds new ones, through the request's context: filtered by its tenant, saved by its unit
/// of work. A bill is always loaded with its lines, because the aggregate changes as a whole.
/// </summary>
public sealed class BillRepository(BillingReconDbContext context) : IBillRepository
{
    /// <inheritdoc />
    public Task<Bill?> GetAsync(BillId id, CancellationToken ct)
        => context.Set<Bill>().Include(bill => bill.Lines).SingleOrDefaultAsync(bill => bill.Id == id, ct);

    /// <inheritdoc />
    public void Add(Bill bill) => context.Set<Bill>().Add(bill);
}
