using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Payers;

/// <summary>
/// What the Payers module answers when another module is about to record money for a payer (BR-105, BR-309): the
/// payer exists in the tenant and is active. The same rule as <see cref="IBillableParties.EnsureCanOweAsync"/>, under
/// the name it has for payments and refunds; one implementation serves both.
/// </summary>
public interface IActivePayers
{
    /// <summary>Succeeds when the tenant has the payer and it is active; fails with <c>payer_not_found</c> or <c>payer_inactive</c>.</summary>
    public Task<Result> EnsureActiveAsync(PayerId payerId, CancellationToken ct);
}
