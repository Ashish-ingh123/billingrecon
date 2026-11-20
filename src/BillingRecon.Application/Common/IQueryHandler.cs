using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Common;

/// <summary>
/// A use case that reads and changes nothing (ADR 0018): one plain class per query, registered by convention and called
/// by its controller, without a mediator. It reads through a read service, never through a repository.
/// </summary>
/// <typeparam name="TQuery">The query: what the caller wants to know.</typeparam>
/// <typeparam name="TResult">The answer.</typeparam>
public interface IQueryHandler<in TQuery, TResult>
{
    /// <summary>Answers <paramref name="query"/>. A record that does not exist for the tenant is a failed result, not an exception.</summary>
    public Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken ct);
}
