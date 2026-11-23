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
/// <para>
/// The application database is made as <c>docs/runbook.md</c> section 10 makes one (ADR 0034): created empty by the
/// administrator, migrated by a login that holds exactly the migrator's four roles, and used by a login that is a member
/// of the application's role and nothing else. So the API host and every context of the production code in these tests
/// act with the grants the application has in production, and every run proves that the migrator's roles suffice.
/// Emptying the tables, reading the catalogue and seeding a defect the application could never cause use the
/// administrator's connection.
/// </para>
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime, IAsyncDisposable
{
    /// <summary>The database the API is pointed at: created by the migrations, like the real one.</summary>
    public const string ApiDatabase = "BillingRecon_Api";

    /// <summary>The database of the persistence tests: the test model, probe entities included, created in one go.</summary>
    public const string ProbeDatabase = "BillingRecon_Probe";

    /// <summary>The database role of the application, which migration <c>0020_DatabaseRoles</c> creates (ADR 0034).</summary>
    public const string ApplicationRole = "billingrecon_application";

    /// <summary>The login of the application in these tests: a member of <see cref="ApplicationRole"/> only.</summary>
    public const string ApplicationLogin = "billingrecon_app_tests";

    /// <summary>The login that migrates <see cref="ApiDatabase"/>: the roles of the migrator and no others.</summary>
    public const string MigratorLogin = "billingrecon_migrator_tests";

    /// <summary>What the migrator holds in the application database (<c>docs/database.md</c> section 7).</summary>
    public static readonly IReadOnlyList<string> MigratorRoles = ["db_ddladmin", "db_datareader", "db_datawriter", "db_securityadmin"];

    private static readonly DateTimeOffset SetupInstant = new(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);

    // A password per test run for each login, never written down.
    private readonly string _applicationPassword = NewPassword();
    private readonly string _migratorPassword = NewPassword();

    // Created in InitializeAsync, not here: building the container already looks for the Docker endpoint, and that
    // failure must come out as the message below, not as a fixture that could not be constructed.
    private MsSqlContainer? _container;

    private Respawner? _apiRespawner;
    private Respawner? _probeRespawner;

    /// <summary>
    /// The connection of the application to <see cref="ApiDatabase"/>, as <see cref="ApplicationLogin"/>: the API host and
    /// every context of the production code in these tests use it.
    /// </summary>
    public string ApiConnectionString => LoginConnectionString(ApiDatabase, ApplicationLogin, _applicationPassword);

    /// <summary>
    /// The administrator's connection to <see cref="ApiDatabase"/>, for what the application never does: emptying the
    /// tables, reading the catalogue, seeding a defect.
    /// </summary>
    public string ApiAdminConnectionString => AdminConnectionStringFor(ApiDatabase);

    /// <summary>Connection string of <see cref="ProbeDatabase"/>, the administrator's: the probe tables have no grants.</summary>
    public string ProbeConnectionString => AdminConnectionStringFor(ProbeDatabase);

    /// <summary>
    /// The connection the API uses on <paramref name="database"/>: the application's on <see cref="ApiDatabase"/>, the
    /// administrator's on any other database, where no application login is mapped (a database a test creates).
    /// </summary>
    public string ConnectionStringFor(string database)
        => database == ApiDatabase ? ApiConnectionString : AdminConnectionStringFor(database);

    /// <summary>The administrator's connection to any database on the server.</summary>
    public string AdminConnectionStringFor(string database)
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

        // The application database as the runbook makes one (section 10): the administrator creates it empty and maps
        // the two logins, the migrator migrates it, and the application's login joins the role the migrations created.
        await CreateApplicationDatabaseAsync();
        await CreateLoginAsync(MigratorLogin, _migratorPassword);
        await CreateLoginAsync(ApplicationLogin, _applicationPassword);
        await AddDatabaseUserAsync(ApiDatabase, MigratorLogin, MigratorRoles);

        await using (var context = new BillingReconDbContext(
            TestContexts.Options<BillingReconDbContext>(LoginConnectionString(ApiDatabase, MigratorLogin, _migratorPassword)),
            SystemIdentity.Instance,
            SystemIdentity.Instance,
            new FakeClock(SetupInstant)))
        {
            await context.Database.MigrateAsync();
        }

        await AddDatabaseUserAsync(ApiDatabase, ApplicationLogin, [ApplicationRole]);

        await using (var context = new TestDbContext(
            TestContexts.Options<TestDbContext>(ProbeConnectionString), SystemIdentity.Instance, SystemIdentity.Instance, new FakeClock(SetupInstant)))
        {
            await context.Database.EnsureCreatedAsync();
        }

        _apiRespawner = await CreateRespawnerAsync(ApiAdminConnectionString);
        _probeRespawner = await CreateRespawnerAsync(ProbeConnectionString);
    }

    /// <summary>Empties every table of both databases. The schema and the migrations history stay.</summary>
    public async Task ResetAsync()
    {
        await ResetAsync(ApiAdminConnectionString, _apiRespawner);
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

    // Created as EF Core creates a database, and as docs/runbook.md section 10 tells the administrator to: readers see the
    // last committed version of a row instead of waiting for a writer (READ_COMMITTED_SNAPSHOT), which the application
    // relies on (ADR 0034). The statement is the one EF Core's own creation sends after CREATE DATABASE.
    private async Task CreateApplicationDatabaseAsync()
    {
        await CreateEmptyDatabaseAsync(ApiDatabase);
        await ExecuteAsAdministratorAsync(
            "master",
            $"IF SERVERPROPERTY('EngineEdition') <> 5 BEGIN ALTER DATABASE [{ApiDatabase}] SET READ_COMMITTED_SNAPSHOT ON; END;");
    }

    // Letters and digits only, so that it can stand in the text of CREATE LOGIN, which takes no parameter for it.
    private static string NewPassword() => $"Br1{Guid.NewGuid():N}";

    private string LoginConnectionString(string database, string login, string password)
        => new SqlConnectionStringBuilder(Container.GetConnectionString())
        {
            InitialCatalog = database,
            IntegratedSecurity = false,
            UserID = login,
            Password = password,
        }.ConnectionString;

    // A login of the server, or a new password for one that a previous run on the same server left (LocalDB keeps its
    // logins between runs; a container starts without them).
    private Task CreateLoginAsync(string login, string password) => ExecuteAsAdministratorAsync(
        "master",
        $"IF SUSER_ID(N'{login}') IS NULL BEGIN CREATE LOGIN [{login}] WITH PASSWORD = N'{password}', CHECK_POLICY = OFF; END ELSE BEGIN ALTER LOGIN [{login}] WITH PASSWORD = N'{password}'; END");

    // The login's user in the database, a member of the roles given and of no other.
    private Task AddDatabaseUserAsync(string database, string login, IEnumerable<string> roles) => ExecuteAsAdministratorAsync(
        database,
        $"IF DATABASE_PRINCIPAL_ID(N'{login}') IS NULL CREATE USER [{login}] FOR LOGIN [{login}];"
            + string.Concat(roles.Select(role => $" ALTER ROLE [{role}] ADD MEMBER [{login}];")));

    // Every name and password in these statements is a constant of this class or made of letters and digits.
    private async Task ExecuteAsAdministratorAsync(string database, string sql)
    {
        await using var connection = new SqlConnection(AdminConnectionStringFor(database));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

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
