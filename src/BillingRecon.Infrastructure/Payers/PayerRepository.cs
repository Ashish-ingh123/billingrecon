using BillingRecon.Application.Payers;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payers;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.Infrastructure.Payers;

/// <summary>Loads payers for a change and adds new ones, through the request's context: filtered by its tenant, saved by its unit of work.</summary>
public sealed class PayerRepository(BillingReconDbContext context) : IPayerRepository
{
    /// <inheritdoc />
    public Task<Payer?> GetAsync(PayerId id, CancellationToken ct)
        => context.Set<Payer>().SingleOrDefaultAsync(payer => payer.Id == id, ct);

    /// <inheritdoc />
    public void Add(Payer payer) => context.Set<Payer>().Add(payer);
}
