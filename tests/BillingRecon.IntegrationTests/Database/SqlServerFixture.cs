using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.IntegrationTests.Persistence;
using BillingRecon.IntegrationTests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Respawn;
using Respawn.Graph;
using Testcontainers.MsSql;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// One SQL Server 2022 for the whole test run (ADR 0016), the image <c>docker-compose.yml</c> names, with two
/// databases: the application database with every migration applied, as the API expects it, and a probe database
/// created from the test model, so the probe entities of the persistence tests have real tables. Every test of the
/// Database collection starts from empty tables: <see cref="ResetAsync()"/> deletes the data and leaves the schema and
/// the migrations history alone. The fixture needs a running Docker engine; without one its first use fails with one
/// message, and nothing is skipped silently.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime, IAsyncDisposable
{
    /// <summary>The database the API is pointed at: created by the migrations, like the real one.</summary>
    public const string ApiDatabase = "BillingRecon_Api";

    /// <summary>The database of the persistence tests: the test model, probe entities included, created in one go.</summary>
    public const string ProbeDatabase = "BillingRecon_Probe";

    private static readonly DateTimeOffset SetupInstant = new(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);

    // Created in InitializeAsync, not here: building the container already looks for the Docker endpoint, and that
    // failure must come out as the message below, not as a fixture that could not be constructed.
    private MsSqlContainer? _container;

    private Respawner? _apiRespawner;
    private Respawner? _probeRespawner;

    /// <summary>Connection string of <see cref="ApiDatabase"/>.</summary>
    public string ApiConnectionString => ConnectionStringFor(ApiDatabase);

    /// <summary>Connection string of <see cref="ProbeDatabase"/>.</summary>
    public string ProbeConnectionString => ConnectionStringFor(ProbeDatabase);

    /// <summary>Connection string of any database on the container.</summary>
    public string ConnectionStringFor(string database)
        => new SqlConnectionStringBuilder(Container.GetConnectionString()) { InitialCatalog = database }.ConnectionString;

    private MsSqlContainer Container => _container ?? throw new InvalidOperationException("The fixture has not been initialised.");

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        var image = SqlServerImage.FromDockerCompose();

        try
        {
            _container = new MsSqlBuilder(image).Build();
            await _container.StartAsync();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "The tests of the Database collection need a running Docker engine: they start SQL Server 2022 in a container. "
                + "On a machine without Docker they fail, the other tests still run (CLAUDE.md section 2, docs/runbook.md section 4).",
                exception);
        }

        await using (var context = new BillingReconDbContext(
            TestContexts.Options<BillingReconDbContext>(ApiConnectionString), SystemIdentity.Instance, SystemIdentity.Instance, new FakeClock(SetupInstant)))
        {
            await context.Database.MigrateAsync();
        }

        await using (var context = new TestDbContext(
            TestContexts.Options<TestDbContext>(ProbeConnectionString), SystemIdentity.Instance, SystemIdentity.Instance, new FakeClock(SetupInstant)))
        {
            await context.Database.EnsureCreatedAsync();
        }

        _apiRespawner = await CreateRespawnerAsync(ApiConnectionString);
        _probeRespawner = await CreateRespawnerAsync(ProbeConnectionString);
    }

    /// <summary>Empties every table of both databases. The schema and the migrations history stay.</summary>
    public async Task ResetAsync()
    {
        await ResetAsync(ApiConnectionString, _apiRespawner);
        await ResetAsync(ProbeConnectionString, _probeRespawner);
    }

    /// <summary>Creates <paramref name="database"/> with no tables and no migrations history, if it does not exist yet.</summary>
    public async Task CreateEmptyDatabaseAsync(string database)
    {
        if (!database.All(character => char.IsAsciiLetterOrDigit(character) || character == '_'))
        {
            throw new ArgumentException("A database name is letters, digits and underscores.", nameof(database));
        }

        await using var connection = new SqlConnection(Container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"IF DB_ID(N'{database}') IS NULL CREATE DATABASE [{database}]";
        await command.ExecuteNonQueryAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <inheritdoc />
    ValueTask IAsyncDisposable.DisposeAsync() => _container?.DisposeAsync() ?? ValueTask.CompletedTask;

    private static async Task<Respawner> CreateRespawnerAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        return await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.SqlServer,
            // The roles are reference data the migration (or the creation of the probe database) wrote once (docs/database.md section 8).
            TablesToIgnore = [new Table("__EFMigrationsHistory"), new Table("Roles")],
        });
    }

    private static async Task ResetAsync(string connectionString, Respawner? respawner)
    {
        if (respawner is null)
        {
            throw new InvalidOperationException("The fixture has not been initialised.");
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await respawner.ResetAsync(connection);
    }
}
