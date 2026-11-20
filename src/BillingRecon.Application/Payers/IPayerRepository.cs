using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;

namespace BillingRecon.Application.Payers;

/// <summary>
/// Loads and adds payers for the commands that change them (docs/domain-model.md, section 9). Reads are filtered by the
/// current tenant. There is no remove: a payer is deactivated, never deleted (BR-105).
/// </summary>
public interface IPayerRepository
{
    /// <summary>The payer, tracked for a change, or null when the tenant has no payer with that id.</summary>
    public Task<Payer?> GetAsync(PayerId id, CancellationToken ct);

    /// <summary>Adds a new payer to the unit of work; it is saved by the commit.</summary>
    public void Add(Payer payer);
}
