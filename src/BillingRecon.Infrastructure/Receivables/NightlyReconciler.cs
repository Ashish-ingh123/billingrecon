using System.Data.Common;
using BillingRecon.Application.Common;
using BillingRecon.Infrastructure.Common.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BillingRecon.Infrastructure.Receivables;

/// <summary>
/// Runs <see cref="NightlyReconciliation"/> at <see cref="RunsAt"/> in India every night, for as long as the API runs
/// (ADR 0056). One instance of the API is one run: a second instance would reconcile twice, which costs reads and two
/// mails and changes nothing (docs/security.md, the limits of one instance). At startup it says whether its alerts are
/// mailed or only logged.
/// </summary>
public sealed partial class NightlyReconciler(IServiceScopeFactory scopeFactory, ILogger<NightlyReconciler> logger) : BackgroundService
{
    /// <summary>The time of day in India the reconciliation runs: after the day's work, before the 01:30 backup's copy.</summary>
    public static readonly TimeOnly RunsAt = new(2, 0);

    private static readonly TimeSpan India = TimeSpan.FromHours(5.5);

    /// <summary>The first instant after <paramref name="now"/> that is <see cref="RunsAt"/> in India.</summary>
    public static DateTimeOffset NextRunAfter(DateTimeOffset now)
    {
        var local = now.ToOffset(India);
        var today = new DateTimeOffset(DateOnly.FromDateTime(local.DateTime).ToDateTime(RunsAt), India);

        return (today > now ? today : today.AddDays(1)).ToUniversalTime();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                if (!scope.ServiceProvider.GetRequiredService<IOperationsAlerts>().Delivers)
                {
                    LogNotMailed(logger);
                }
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                TimeSpan wait;
                await using (var scope = scopeFactory.CreateAsyncScope())
                {
                    var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
                    wait = NextRunAfter(now) - now;
                }

                await Task.Delay(wait, stoppingToken);
                await RunOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The application is stopping.
        }
    }

    /// <summary>One run, as the schedule makes it: a failure that stops the whole run is an alert of its own.</summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        try
        {
            await scope.ServiceProvider.GetRequiredService<NightlyReconciliation>().RunAsync(ct);
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or TimeoutException)
        {
            LogFailed(logger, exception);
            scope.ServiceProvider.GetRequiredService<IOperationsAlerts>().Raise(
                "The nightly reconciliation did not run",
                $"It failed with {exception.GetType().Name} and runs again tomorrow at 02:00 IST. The error is in the log; run the reconciliation report by hand meanwhile (docs/runbook.md section 16.1).");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Alerts:MailTo or Mail:Host is not set: operations alerts (drift, failed jobs) are written to the log only")]
    private static partial void LogNotMailed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "The nightly reconciliation did not run")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
