namespace BillingRecon.IntegrationTests.Support;

/// <summary>
/// What a test tells <see cref="IdempotencyProbeController"/> to do, and what the controller reports back. One per API
/// host, shared by its requests: the tests need to see across requests how often the action really ran, and to hold a
/// request open while another arrives.
/// </summary>
public sealed class IdempotencyProbeState
{
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _executions;
    private int _faultsLeft;

    /// <summary>How many times the body of the probe action ran, replays not counted: they never reach it.</summary>
    public int Executions => Volatile.Read(ref _executions);

    /// <summary>Completes when a request that was told to wait has entered the action.</summary>
    public Task Entered => _entered.Task;

    /// <summary>Lets the requests that were told to wait go on.</summary>
    public void OpenGate() => _gate.TrySetResult();

    /// <summary>The next <paramref name="count"/> requests that are told to fault once throw; the ones after succeed.</summary>
    public void FaultNext(int count) => Volatile.Write(ref _faultsLeft, count);

    internal void CountExecution() => Interlocked.Increment(ref _executions);

    internal bool TakeFault() => Interlocked.Decrement(ref _faultsLeft) >= 0;

    internal async Task WaitAtGateAsync(CancellationToken ct)
    {
        _entered.TrySetResult();
        await _gate.Task.WaitAsync(ct);
    }
}
