using System.Net;
using BillingRecon.Api.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.IntegrationTests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The grants of the application's database login on a real SQL Server (BR-005, BR-402, BR-702; ADR 0034;
/// docs/database.md section 7): the login the API uses is a member of the application's role and nothing else; it
/// cannot delete a bill or change an audit record or the ledger, whatever the code above it does; and every table grants
/// it exactly what the markers of its entities allow, so a new table that should be append-only or deletable cannot
/// slip through with the schema's defaults.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class DatabasePermissionsOnSqlServerTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    // "The %ls permission was denied on the object '%.*ls', database '%.*ls', schema '%.*ls'."
    private const int PermissionDenied = 229;

    private const string MigrationsHistory = "__EFMigrationsHistory";

    // 17 October 2026 in India.
    private static readonly DateOnly Today = new(2026, 10, 17);

    [Fact]
    public async Task BR005_BR702_the_application_login_cannot_delete_a_bill_nor_change_or_delete_an_audit_record_or_the_ledger()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        var admin = await NewApiTenantAsync();
        using var client = factory.CreateClientFor(admin, "TenantAdmin");
        var (patient, payer) = await client.NewBillPartiesAsync();
        var draft = await client.CreateBillAsync(new CreateBillRequest { PatientId = patient.Id, PayerId = payer.Id, BillDate = Today, DueDate = Today, Lines = [BillingApi.FreeTextLine("Dressing", 2m, 60m, 5m)] });
        using var posted = await client.SendWithIfMatchAsync(HttpMethod.Post, BillingApi.Bill(draft.Id, "post"), body: null, PayersApi.ETag(draft.Version));
        posted.StatusCode.ShouldBe(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync());

        // The bill is posted: it has its audit records and its ledger posting, written by the API as this same login.
        await using var application = new SqlConnection(SqlServer.ApiConnectionString);
        await application.OpenAsync();

        (await RefusedAsync(application, "DELETE FROM [dbo].[Bills] WHERE [Id] = @id", draft.Id)).ShouldBe(PermissionDenied);
        (await RefusedAsync(application, "UPDATE [dbo].[AuditRecords] SET [Action] = N'Forged' WHERE [EntityId] = @id", draft.Id)).ShouldBe(PermissionDenied);
        (await RefusedAsync(application, "DELETE FROM [dbo].[AuditRecords] WHERE [EntityId] = @id", draft.Id)).ShouldBe(PermissionDenied);
        (await RefusedAsync(application, "UPDATE [dbo].[LedgerEntries] SET [TenantId] = [TenantId]")).ShouldBe(PermissionDenied);
        (await RefusedAsync(application, "DELETE FROM [dbo].[LedgerEntries]")).ShouldBe(PermissionDenied);
        (await RefusedAsync(application, "UPDATE [dbo].[LedgerPostings] SET [TenantId] = [TenantId]")).ShouldBe(PermissionDenied);
        (await RefusedAsync(application, "DELETE FROM [dbo].[LedgerPostings]")).ShouldBe(PermissionDenied);
        (await RefusedAsync(application, "DELETE FROM [dbo].[Patients] WHERE [Id] = @id", patient.Id)).ShouldBe(PermissionDenied);
        (await RefusedAsync(application, "DELETE FROM [dbo].[Payers] WHERE [Id] = @id", payer.Id)).ShouldBe(PermissionDenied);

        // Nothing changed: the bill and its records are there as the API wrote them.
        (await CountAsync(application, "SELECT COUNT(*) FROM [dbo].[Bills] WHERE [Id] = @id", draft.Id)).ShouldBe(1);
        (await CountAsync(application, "SELECT COUNT(*) FROM [dbo].[AuditRecords] WHERE [EntityId] = @id", draft.Id)).ShouldBeGreaterThanOrEqualTo(2);
        (await CountAsync(application, "SELECT COUNT(*) FROM [dbo].[AuditRecords] WHERE [EntityId] = @id AND [Action] = N'Forged'", draft.Id)).ShouldBe(0);
        (await CountAsync(application, "SELECT COUNT(*) FROM [dbo].[LedgerPostings]")).ShouldBe(1);
    }

    [Fact]
    public async Task ADR0034_the_api_connects_as_the_application_login_which_is_a_member_of_the_applications_role_and_nothing_else()
    {
        using var factory = new DatabaseApiFactory(SqlServer);
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<BillingReconDbContext>();
            (await context.Database.SqlQueryRaw<string>("SELECT SUSER_SNAME() AS [Value]").ToListAsync()).ShouldBe([SqlServerFixture.ApplicationLogin]);
        }

        await using var application = new SqlConnection(SqlServer.ApiConnectionString);
        await application.OpenAsync();
        await using var command = new SqlCommand(
            """
            SELECT IS_SRVROLEMEMBER(N'sysadmin'), IS_MEMBER(N'db_owner'), IS_MEMBER(N'db_ddladmin'), IS_MEMBER(N'db_datareader'),
                   IS_MEMBER(N'db_datawriter'), IS_MEMBER(N'db_securityadmin'), IS_MEMBER(N'billingrecon_application'),
                   HAS_PERMS_BY_NAME(NULL, N'DATABASE', N'CREATE TABLE'), HAS_PERMS_BY_NAME(N'dbo', N'SCHEMA', N'ALTER'),
                   HAS_PERMS_BY_NAME(N'dbo', N'SCHEMA', N'DELETE'), HAS_PERMS_BY_NAME(NULL, N'DATABASE', N'ALTER ANY ROLE')
            """,
            application);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        var answers = Enumerable.Range(0, reader.FieldCount).Select(reader.GetInt32).ToArray();

        // Not an administrator, not an owner, no fixed role; the application's role; no schema change, no delete of the
        // schema's tables, no change of roles.
        answers.ShouldBe([0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0]);
    }

    [Fact]
    public async Task ADR0034_the_applications_role_and_every_grant_belong_to_dbo_though_the_migrator_ran_the_migration()
    {
        // The fixture migrates as a login with the migrator's four roles only. What it created must not depend on that
        // login, so that the migrator's user can be dropped or replaced (docs/runbook.md section 10).
        await using var administrator = new SqlConnection(SqlServer.ApiAdminConnectionString);
        await administrator.OpenAsync();

        var owner = await ReadAsync(administrator, $"SELECT USER_NAME([owning_principal_id]) FROM sys.database_principals WHERE [name] = N'{SqlServerFixture.ApplicationRole}'");
        var grantors = await ReadAsync(administrator, $"SELECT DISTINCT USER_NAME([grantor_principal_id]) FROM sys.database_permissions WHERE [grantee_principal_id] = DATABASE_PRINCIPAL_ID(N'{SqlServerFixture.ApplicationRole}')");
        var migrated = await ReadAsync(administrator, $"SELECT [name] FROM sys.database_principals WHERE [name] = N'{SqlServerFixture.MigratorLogin}'");

        owner.ShouldBe(["dbo"]);
        grantors.ShouldBe(["dbo"]);
        migrated.ShouldBe([SqlServerFixture.MigratorLogin], "the migrations ran as the migrator's user");
    }

    [Fact]
    public async Task ADR0034_the_application_database_gives_readers_the_last_committed_row_as_a_database_ef_core_creates_does()
    {
        // The application relies on it: a read does not wait for a writer's open transaction (an idempotent request holds
        // one while its answer is stored, ADR 0006). EF Core switches it on when it creates a database, as it did for the
        // probe database; a database the administrator creates by hand (docs/runbook.md section 10) must switch it on.
        await using var administrator = new SqlConnection(SqlServer.ApiAdminConnectionString);
        await administrator.OpenAsync();
        var snapshots = await ReadAsync(
            administrator,
            $"SELECT [name] + N'=' + CONVERT(nvarchar(1), [is_read_committed_snapshot_on]) FROM sys.databases WHERE [name] IN (N'{SqlServerFixture.ApiDatabase}', N'{SqlServerFixture.ProbeDatabase}') ORDER BY [name]");

        snapshots.ShouldBe([$"{SqlServerFixture.ApiDatabase}=1", $"{SqlServerFixture.ProbeDatabase}=1"]);
    }

    [Fact]
    public async Task ADR0034_every_table_grants_the_application_exactly_what_the_markers_of_its_entities_allow()
    {
        Dictionary<string, string[]> expected;
        await using (var context = ApiContext(SystemIdentity.Instance))
        {
            expected = context.Model.GetEntityTypes()
                .Where(entity => entity.GetTableName() is not null)
                .GroupBy(entity => entity.GetTableName()!)
                .ToDictionary(table => table.Key, table => Allowed([.. table.Select(entity => entity.ClrType)]));
        }

        expected[MigrationsHistory] = ["SELECT"];

        // The tables as the administrator sees them, and what the application's login may do with each.
        await using var administrator = new SqlConnection(SqlServer.ApiAdminConnectionString);
        await administrator.OpenAsync();
        var tables = await ReadAsync(administrator, "SELECT [name] FROM sys.tables WHERE SCHEMA_NAME([schema_id]) = N'dbo'");

        await using var application = new SqlConnection(SqlServer.ApiConnectionString);
        await application.OpenAsync();
        var actual = new Dictionary<string, string[]>();
        foreach (var table in tables)
        {
            actual[table] = [.. await ReadAsync(application, "SELECT [permission_name] FROM fn_my_permissions(@object, N'OBJECT') WHERE [subentity_name] = N''", $"[dbo].[{table}]")];
        }

        actual.Keys.Order().ShouldBe(expected.Keys.Order(), "every table of the database is a table of the model or the migrations history");
        foreach (var (table, allowed) in expected)
        {
            actual[table].Order().ShouldBe(allowed.Order(), Advice(table, allowed));
        }
    }

    // What the application may do with a table, from the entities stored in it (CLAUDE.md section 4, rules 10 and 11).
    private static string[] Allowed(Type[] entities)
    {
        if (entities.Any(typeof(IAppendOnly).IsAssignableFrom))
        {
            return ["INSERT", "SELECT"];
        }

        if (entities.Any(typeof(IHardDeletable).IsAssignableFrom))
        {
            return ["DELETE", "INSERT", "SELECT", "UPDATE"];
        }

        // The roles are reference data that migration 0018 wrote; nothing in the application changes them.
        return entities.Contains(typeof(RoleDefinition)) ? ["SELECT"] : ["INSERT", "SELECT", "UPDATE"];
    }

    // The line a new migration needs when a table does not have the grants its entities call for (ADR 0034).
    private static string Advice(string table, string[] allowed) => allowed switch
    {
        ["INSERT", "SELECT"] => $"{table} is append-only: a migration needs DENY UPDATE, DELETE ON [dbo].[{table}] TO [{SqlServerFixture.ApplicationRole}];",
        ["DELETE", "INSERT", "SELECT", "UPDATE"] => $"{table} holds rows that are deleted: a migration needs GRANT DELETE ON [dbo].[{table}] TO [{SqlServerFixture.ApplicationRole}];",
        ["SELECT"] => $"{table} is read only: a migration needs DENY INSERT, UPDATE, DELETE ON [dbo].[{table}] TO [{SqlServerFixture.ApplicationRole}];",
        _ => $"{table} takes the schema's grants: SELECT, INSERT, UPDATE and nothing else (migration 0020_DatabaseRoles).",
    };

    private static async Task<int> RefusedAsync(SqlConnection connection, string sql, Guid? id = null)
    {
        await using var command = Command(connection, sql, id);

        return (await Should.ThrowAsync<SqlException>(() => command.ExecuteNonQueryAsync())).Number;
    }

    private static async Task<int> CountAsync(SqlConnection connection, string sql, Guid? id = null)
    {
        await using var command = Command(connection, sql, id);

        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<List<string>> ReadAsync(SqlConnection connection, string sql, string? securable = null)
    {
        await using var command = new SqlCommand(sql, connection);
        if (securable is not null)
        {
            command.Parameters.Add(new SqlParameter("@object", System.Data.SqlDbType.NVarChar, 300) { Value = securable });
        }

        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static SqlCommand Command(SqlConnection connection, string sql, Guid? id)
    {
        var command = new SqlCommand(sql, connection);
        if (id is { } value)
        {
            command.Parameters.Add(new SqlParameter("@id", value));
        }

        return command;
    }
}
