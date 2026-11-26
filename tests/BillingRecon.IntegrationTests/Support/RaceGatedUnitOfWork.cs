using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;

namespace BillingRecon.IntegrationTests.Support;

/// <summary>
/// The unit of work with its commit gated, for a use case that takes no lock before it commits: the first to arrive
/// has read and changed its documents in memory, and waits in front of the save.
/// </summary>
public sealed class RaceGatedUnitOfWork(IUnitOfWork inner, RaceGate gate) : IUnitOfWork
{
    /// <inheritdoc />
    public Task<Result> CommitAsync(CancellationToken ct) => gate.BeforeAsync(() => inner.CommitAsync(ct), ct);
}
