using System.ComponentModel.DataAnnotations;
using Microsoft.Data.SqlClient;

namespace BillingRecon.Infrastructure.Common.Settings;

/// <summary>
/// How the API reaches SQL Server. Bound from the <c>Database</c> configuration section and validated at startup.
/// The connection string is a secret: it comes from user-secrets or the <c>Database__ConnectionString</c>
/// environment variable, never from a file in git.
/// </summary>
public sealed class DatabaseOptions
{
    /// <summary>Name of the configuration section.</summary>
    public const string SectionName = "Database";

    /// <summary>Message used when the connection string cannot be parsed. It never repeats the value, which is a secret.</summary>
    public const string MalformedMessage = "Database:ConnectionString is not a valid SQL Server connection string (the value is not shown because it is a secret).";

    /// <summary>Message used when migrations at startup are switched on outside the Development environment.</summary>
    public const string MigrationsOutsideDevelopmentMessage =
        "Database:ApplyMigrationsOnStartup may be true in the Development environment only. Everywhere else migrations are a deployment step, run with the migration login (docs/runbook.md).";

    /// <summary>SQL Server connection string.</summary>
    [Required(
        AllowEmptyStrings = false,
        ErrorMessage = "Database:ConnectionString is required. Set it with user-secrets or the Database__ConnectionString environment variable.")]
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>
    /// Apply pending migrations when the API starts. A convenience for the Development environment, switched on in
    /// <c>appsettings.Development.json</c>; in any other environment the API refuses to start with it switched on.
    /// </summary>
    public bool ApplyMigrationsOnStartup { get; init; }

    /// <summary>True when the connection string parses and names a server. A missing value is reported by <c>[Required]</c> instead.</summary>
    public static bool IsWellFormed(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return true;
        }

        try
        {
            return !string.IsNullOrWhiteSpace(new SqlConnectionStringBuilder(connectionString).DataSource);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException
            or InvalidCastException or NotSupportedException)
        {
            // Unknown keyword, bad syntax, or a value of the wrong kind, out of range or unsupported: unusable either way.
            return false;
        }
    }
}
