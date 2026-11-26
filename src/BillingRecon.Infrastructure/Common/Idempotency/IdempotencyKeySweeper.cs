using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BillingRecon.Infrastructure.Common.Idempotency;

/// <summary>
/// Runs the <see cref="IdempotencyKeySweep"/> once a day while the application runs (review 2026-11, finding R3). The
/// first run waits a few minutes after the start, so that a start is never slowed by it. A run that fails is logged
/// and tried again the next day: nothing depends on an expired key being gone.
/// </summary>
public sealed class IdempotencyKeySweeper(IServiceScopeFactory scopeFactory, ILogger<IdempotencyKeySweeper> logger) : BackgroundService
{
    private static readonly TimeSpan FirstAfter = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Every = TimeSpan.FromHours(24);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstAfter, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await RunOnceAsync(stoppingToken);
                await Task.Delay(Every, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The application is stopping.
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var deleted = await scope.ServiceProvider.GetRequiredService<IdempotencyKeySweep>().RunAsync(ct);

            logger.LogInformation("Deleted {Deleted} expired idempotency keys", deleted);
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException or InvalidOperationException or TimeoutException)
        {
            logger.LogError(exception, "The sweep of expired idempotency keys failed; it runs again in {Hours} hours", Every.TotalHours);
        }
    }
}
