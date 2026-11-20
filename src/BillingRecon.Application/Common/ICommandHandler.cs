using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Common;

/// <summary>
/// A use case that changes state (ADR 0018): one plain class per command, registered by convention and called by its
/// controller, without a mediator. It validates the command, loads and commands one aggregate, and commits one unit of
/// work (BR-007).
/// </summary>
/// <typeparam name="TCommand">The command: what the caller asks for.</typeparam>
/// <typeparam name="TResult">What the caller gets back when the command succeeds.</typeparam>
public interface ICommandHandler<in TCommand, TResult>
{
    /// <summary>Carries out <paramref name="command"/>. An expected failure is a failed result; an exception is a bug or a fault.</summary>
    public Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken ct);
}
