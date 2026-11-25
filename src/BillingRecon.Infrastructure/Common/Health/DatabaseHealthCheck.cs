using System.Data.Common;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BillingRecon.Infrastructure.Common.Health;

/// <summary>
/// Readiness check: is the application database reachable, and is its schema the one this build expects? It asks
/// the database which migrations it has applied and compares them with the migrations in this build. A server that
/// does not answer, a missing database and a migration that has not been applied all mean "not ready".
/// </summary>
public sealed class DatabaseHealthCheck(BillingReconDbContext dbContext, DatabaseSettingsProbe probe) : IHealthCheck
{
    /// <summary>Registration name of the check.</summary>
    public const string Name = "database";

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).Count();

            if (pending != 0)
            {
                return new HealthCheckResult(context.Registration.FailureStatus, $"{pending} database migration(s) have not been applied.");
            }

            // The setting the application relies on (ADR 0034, ADR 0055), also when the database was not there at startup.
            return await probe.ReadAsync(cancellationToken) is { ReadCommittedSnapshot: false }
                ? new HealthCheckResult(context.Registration.FailureStatus, "The application database has READ_COMMITTED_SNAPSHOT off.")
                : HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException)
        {
            // The description is fixed text: the exception, which may name the server, goes to the log only.
            return new HealthCheckResult(context.Registration.FailureStatus, "The application database is not reachable.", exception);
        }
    }
}
