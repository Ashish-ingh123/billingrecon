using BillingRecon.Infrastructure.Common.Time;
using BillingRecon.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BillingRecon.Infrastructure.Common.Persistence;

/// <summary>
/// Creates the context for the EF Core tools (<c>dotnet ef</c>). The tools use this factory instead of starting the
/// API, so adding a migration needs neither the API's configuration nor a reachable database.
/// <para>
/// The connection string comes from the <c>Database__ConnectionString</c> environment variable, or from the
/// <c>--connection</c> option of the commands that have one (<c>dotnet ef database update</c>). Without either, a
/// placeholder is used, which is enough for the commands that never connect: <c>migrations add</c>,
/// <c>migrations script</c> and <c>migrations has-pending-model-changes</c>.
/// </para>
/// <para>
/// The context is created with the system identity: no tenant and no user. The tools create and alter tables;
/// they never read or write tenant data.
/// </para>
/// </summary>
public sealed class BillingReconDbContextFactory : IDesignTimeDbContextFactory<BillingReconDbContext>
{
    /// <summary>The environment variable read for the connection string: the same one the API reads.</summary>
    public const string ConnectionStringVariable = "Database__ConnectionString";

    // A server name that cannot exist (the .invalid domain is reserved for that), so a command that does connect
    // fails quickly and the name in the error says what is missing.
    private const string PlaceholderConnectionString =
        "Server=no-connection-string-set.invalid;Database=BillingRecon;Integrated Security=true;Connect Timeout=5";

    /// <inheritdoc />
    public BillingReconDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = PlaceholderConnectionString;
        }

        var options = new DbContextOptionsBuilder<BillingReconDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new BillingReconDbContext(options, SystemIdentity.Instance, SystemIdentity.Instance, new SystemClock());
    }
}
