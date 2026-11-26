namespace BillingRecon.IntegrationTests.Support;

/// <summary>
/// Puts two requests in the one order that makes the second stale (review 2026-11, finding R11): the first is held
/// where it has taken its lock and has not committed, the second is let in only then and says when it has read, and
/// only then does the first commit. Two requests merely started together meet that way by luck, mostly not at all, and
/// a test of them passes whether or not the guard under the lock works. Not armed while a test sets its scene.
/// </summary>
public sealed class RaceGate
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly TaskCompletionSource _firstHolds = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _secondHasRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _armed;
    private int _arrivals;

    /// <summary>
    /// A gated call that takes a lock. The first to arrive makes the call, so it holds the lock, and waits there. The
    /// second says it has arrived, which it does only with everything its use case reads before this call read, and
    /// then makes the call, where it waits for the lock.
    /// </summary>
    /// <typeparam name="T">What the call answers.</typeparam>
    /// <param name="call">The call that is gated.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>What the call answered.</returns>
    public async Task<T> AfterAsync<T>(Func<Task<T>> call, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(call);

        if (!_armed)
        {
            return await call();
        }

        switch (Interlocked.Increment(ref _arrivals))
        {
            case 1:
                var answer = await call();
                _firstHolds.TrySetResult();
                await _release.Task.WaitAsync(Patience, ct);

                return answer;
            case 2:
                _secondHasRead.TrySetResult();

                return await call();
            default:
                return await call();
        }
    }

    /// <summary>
    /// A gated call that takes no lock before it: the first to arrive waits in front of it, with everything its use
    /// case read already read, until it is released. Every later arrival passes.
    /// </summary>
    /// <typeparam name="T">What the call answers.</typeparam>
    /// <param name="call">The call that is gated.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>What the call answered.</returns>
    public async Task<T> BeforeAsync<T>(Func<Task<T>> call, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(call);

        if (_armed && Interlocked.Increment(ref _arrivals) == 1)
        {
            _firstHolds.TrySetResult();
            await _release.Task.WaitAsync(Patience, ct);
        }

        return await call();
    }

    /// <summary>
    /// Runs the two requests through a call gated by <see cref="AfterAsync{T}"/>: the winner holds the lock, the loser
    /// reads and waits for it, the winner commits.
    /// </summary>
    /// <param name="winner">The request that commits first.</param>
    /// <param name="loser">The request that has read before that commit and arrives at the lock after it.</param>
    /// <returns>The answer of the winner, then that of the loser.</returns>
    public async Task<HttpResponseMessage[]> RaceAsync(Func<Task<HttpResponseMessage>> winner, Func<Task<HttpResponseMessage>> loser)
    {
        _armed = true;
        var first = Task.Run(winner);
        await ReachedAsync(_firstHolds, first, "the first request never reached the gate");
        var second = Task.Run(loser);
        await ReachedAsync(_secondHasRead, second, "the second request never reached the gate");
        _release.TrySetResult();

        return await Task.WhenAll(first, second);
    }

    /// <summary>
    /// Runs the two requests through a call gated by <see cref="BeforeAsync{T}"/>: the loser reads and waits in front
    /// of its commit, the winner runs from start to end, the loser commits.
    /// </summary>
    /// <param name="winner">The request that commits first.</param>
    /// <param name="loser">The request that has read before that commit and commits after it.</param>
    /// <returns>The answer of the winner, then that of the loser.</returns>
    public async Task<HttpResponseMessage[]> OvertakeAsync(Func<Task<HttpResponseMessage>> winner, Func<Task<HttpResponseMessage>> loser)
    {
        _armed = true;
        var held = Task.Run(loser);
        await ReachedAsync(_firstHolds, held, "the held request never reached the gate");
        var passed = await winner();
        _release.TrySetResult();

        return [passed, await held];
    }

    // A request that answers before it reaches the gate never will: its answer says why.
    private static async Task ReachedAsync(TaskCompletionSource reached, Task<HttpResponseMessage> request, string otherwise)
    {
        var done = await Task.WhenAny(reached.Task, request).WaitAsync(Patience);
        if (done == reached.Task || reached.Task.IsCompleted)
        {
            return;
        }

        using var response = await request;

        throw new InvalidOperationException($"{otherwise}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }
}
