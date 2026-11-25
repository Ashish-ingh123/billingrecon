namespace BillingRecon.Infrastructure.Common.Persistence;

/// <summary>What <see cref="DatabaseSettingsProbe"/> read of the application database.</summary>
/// <param name="ReadCommittedSnapshot">Whether <c>READ_COMMITTED_SNAPSHOT</c> is on.</param>
/// <param name="Encrypted">Whether SQL Server encrypts the database at rest (TDE).</param>
public sealed record DatabaseSettings(bool ReadCommittedSnapshot, bool Encrypted);
