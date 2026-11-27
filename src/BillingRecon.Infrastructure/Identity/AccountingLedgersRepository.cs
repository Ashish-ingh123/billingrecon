using BillingRecon.Application.Identity;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Identity;

/// <summary>The hospital's names for its accountant's ledgers, through the context and so through the tenant filter.</summary>
public sealed class AccountingLedgersRepository(BillingReconDbContext context) : IAccountingLedgersRepository
{
    /// <inheritdoc />
    public Task<AccountingLedgers?> FindAsync(CancellationToken ct)
        => context.Set<AccountingLedgers>().SingleOrDefaultAsync(ct);

    /// <inheritdoc />
    public void Add(AccountingLedgers ledgers) => context.Set<AccountingLedgers>().Add(ledgers);
}
