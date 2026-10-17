using System.Collections.Concurrent;
using System.Diagnostics;
using BillingRecon.Api.Common.Correlation;
using Serilog.Core;
using Serilog.Events;

namespace BillingRecon.IntegrationTests.Support;

/// <summary>Collects every log event the API writes, after enrichment and masking, exactly as a real sink receives it.</summary>
public sealed class InMemoryLogSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    /// <summary>Everything written so far.</summary>
    public IReadOnlyList<LogEvent> Events => [.. _events];

    /// <inheritdoc />
    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);

    /// <summary>The events that carry this correlation id, which are the events of one request.</summary>
    public IReadOnlyList<LogEvent> ForCorrelationId(Guid correlationId)
        => [.. _events.Where(logEvent => logEvent.Scalar(CorrelationIdMiddleware.LogPropertyName) is Guid id && id == correlationId)];

    /// <summary>Waits until an event of the request satisfies <paramref name="predicate"/>; fails after the timeout.</summary>
    public async Task<LogEvent> WaitForAsync(Guid correlationId, Func<LogEvent, bool> predicate, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            var match = ForCorrelationId(correlationId).FirstOrDefault(predicate);
            if (match is not null)
            {
                return match;
            }

            if (stopwatch.Elapsed > timeout)
            {
                throw new TimeoutException($"No matching log event for correlation id {correlationId} within {timeout}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }
    }
}
