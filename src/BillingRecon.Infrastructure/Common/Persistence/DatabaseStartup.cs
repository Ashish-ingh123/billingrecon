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
/// </list>
/// </summary>
public sealed class DatabaseStartup(IServiceScopeFactory scopeFactory, IOptions<DatabaseOptions> options, ILogger<DatabaseStartup> logger)
    : IHostedLifecycleService
{
    /// <inheritdoc />
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingReconDbContext>();

        // Reading the model builds and validates it; an invalid model throws here.
        _ = context.Model;

        if (!options.Value.ApplyMigrationsOnStartup)
        {
            return;
        }

        logger.LogInformation("Applying pending database migrations at startup (Development only)");
        await context.Database.MigrateAsync(cancellationToken);
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
