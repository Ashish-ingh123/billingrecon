using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>The hospital's names for its accountant's ledgers (ADR 0062): at most one row for a tenant.</summary>
public interface IAccountingLedgersRepository
{
    /// <summary>The row of the tenant, tracked, or null when no name was ever saved.</summary>
    public Task<AccountingLedgers?> FindAsync(CancellationToken ct);

    /// <summary>Adds the first row of the tenant.</summary>
    public void Add(AccountingLedgers ledgers);
}
