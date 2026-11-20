using BillingRecon.Application.Common;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Receivables;
using BillingRecon.Infrastructure.Common.Persistence;

namespace BillingRecon.Infrastructure.Receivables;

/// <summary>
/// Opens the cached balances of a new payer (BR-404): a row at zero, added to the context of the request, so the
/// commit that saves the payer saves it too. The tenant guard gives the row the current tenant.
/// </summary>
public sealed class PayerAccounts(BillingReconDbContext context, IClock clock) : IPayerAccounts
{
    /// <inheritdoc />
    public void Open(PayerId payerId) => context.Set<PayerBalance>().Add(PayerBalance.Open(payerId, clock.UtcNow));
}
