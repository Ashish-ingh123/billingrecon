using BillingRecon.Infrastructure.Common.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BillingRecon.Infrastructure.Common.Persistence;

/// <summary>
/// What the database layer does before the host starts anything else:
/// <list type="number">
/// <item>It builds the EF Core model, which runs the model conventions. A decimal or text column whose size was
/// left to a default therefore stops the application at startup, in every environment, without a database.</item>
/// <item>It applies pending migrations, but only when <see cref="DatabaseOptions.ApplyMigrationsOnStartup"/> is on,
/// which the options validation allows in the Development environment only.</item>
/// <item>Outside Development it refuses to start on a database that answers and has <c>READ_COMMITTED_SNAPSHOT</c> off,
/// and warns when the database is not encrypted at rest by SQL Server (ADR 0055).</item>
/// </list>
/// </summary>
public sealed class DatabaseStartup(
    IServiceScopeFactory scopeFactory,
    IOptions<DatabaseOptions> options,
    DatabaseSettingsProbe probe,
    IHostEnvironment environment,
    ILogger<DatabaseStartup> logger)
    : IHostedLifecycleService
{
    /// <inheritdoc />
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingReconDbContext>();

        // Reading the model builds and validates it; an invalid model throws here.
        _ = context.Model;

        if (options.Value.ApplyMigrationsOnStartup)
        {
            logger.LogInformation("Applying pending database migrations at startup (Development only)");
            await context.Database.MigrateAsync(cancellationToken);
        }

        if (!environment.IsDevelopment())
        {
            await CheckSettingsAsync(cancellationToken);
        }
    }

    // A database that does not answer yet does not stop the start: readiness says so, and refuses the same setting.
    private async Task CheckSettingsAsync(CancellationToken ct)
    {
        var settings = await probe.ReadAsync(ct);
        if (settings is null)
        {
            logger.LogWarning("The application database did not answer at startup; its settings are checked by the readiness check");
            return;
        }

        if (!settings.ReadCommittedSnapshot)
        {
            throw new InvalidOperationException(DatabaseSettingsProbe.SnapshotOffMessage);
        }

        if (!settings.Encrypted)
        {
            logger.LogWarning("The application database is not encrypted by SQL Server (TDE); the disk it is on must be encrypted instead");
        }
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
