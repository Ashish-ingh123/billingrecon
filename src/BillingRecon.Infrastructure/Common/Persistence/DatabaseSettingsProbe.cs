using System.Data.Common;
using BillingRecon.Infrastructure.Common.Settings;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace BillingRecon.Infrastructure.Common.Persistence;

/// <summary>
/// Reads two settings of the application database that the application relies on and cannot set (ADR 0055):
/// whether <c>READ_COMMITTED_SNAPSHOT</c> is on (ADR 0034: readers do not wait for writers) and whether the database
/// is encrypted at rest by SQL Server (TDE). It asks the catalogue of the server about the database it is connected
/// to, on a connection of its own: no table of the application, no tenant, no EF Core.
/// </summary>
public sealed class DatabaseSettingsProbe(IOptions<DatabaseOptions> options)
{
    /// <summary>The message of a refused start. It names no server and no database.</summary>
    public const string SnapshotOffMessage =
        "The application database has READ_COMMITTED_SNAPSHOT off. The application relies on it being on (ADR 0034): "
        + "ALTER DATABASE ... SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE (docs/runbook.md section 19).";

    private const string Query =
        "SELECT is_read_committed_snapshot_on, is_encrypted FROM sys.databases WHERE database_id = DB_ID()";

    /// <summary>The settings, or null when the database does not answer.</summary>
    public async Task<DatabaseSettings?> ReadAsync(CancellationToken ct)
    {
        try
        {
            await using var connection = new SqlConnection(options.Value.ConnectionString);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = Query;
            await using var reader = await command.ExecuteReaderAsync(ct);

            return await reader.ReadAsync(ct)
                ? new DatabaseSettings(reader.GetBoolean(0), reader.GetBoolean(1))
                : null;
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException)
        {
            return null;
        }
    }
}
