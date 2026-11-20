using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Common;

/// <summary>
/// Commits everything a use case changed, in one transaction (BR-007, docs/domain-model.md section 9). Losing a race
/// to another request is an expected outcome and comes back as a failed result: a row that changed since it was read
/// is <c>concurrency_conflict</c> (BR-804), and a row that a unique index refused is <c>duplicate_key</c>, which the
/// use case replaces with the error of the rule it checked before. Anything else is a fault and throws.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>Saves the changes of the use case.</summary>
    public Task<Result> CommitAsync(CancellationToken ct);
}
