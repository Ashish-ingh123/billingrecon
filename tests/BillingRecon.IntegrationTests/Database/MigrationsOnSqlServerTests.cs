using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.IntegrationTests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The migrations against SQL Server 2022: they apply and are recorded, the Tenants table is what docs/database.md
/// section 3.1 describes, a tenant round-trips, and the constraints refuse what the domain refuses (the database is
/// the last line of defence, not the first).
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class MigrationsOnSqlServerTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    [Fact]
    public async Task Every_migration_is_applied_and_recorded_and_none_is_pending()
    {
        await using var context = ApiContext(SystemIdentity.Instance);

        (await context.Database.GetAppliedMigrationsAsync()).ShouldBe(["20261003192738_0001_Tenants"]);
        (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_tenants_table_has_the_documented_check_constraints_and_unique_index()
    {
        await using var context = ApiContext(SystemIdentity.Instance);

        var checks = await context.Database
            .SqlQueryRaw<string>("SELECT [name] AS [Value] FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.Tenants')")
            .ToListAsync();
        var uniqueIndexes = await context.Database
            .SqlQueryRaw<string>("SELECT [name] AS [Value] FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Tenants') AND is_unique = 1 AND is_primary_key = 0")
            .ToListAsync();

        checks.ShouldBe(
            [
                "CK_Tenants_Status", "CK_Tenants_MaxBackdateDays", "CK_Tenants_DefaultDueDays", "CK_Tenants_BillPrefix",
                "CK_Tenants_CreditNotePrefix", "CK_Tenants_PaymentPrefix", "CK_Tenants_AdjustmentPrefix", "CK_Tenants_RefundPrefix",
            ],
            ignoreOrder: true);
        uniqueIndexes.ShouldBe(["UX_Tenants_Code"]);
    }

    [Fact]
    public async Task A_tenant_round_trips_with_an_id_generated_here_and_a_row_version_assigned_there()
    {
        var tenant = TestData.NewTenant(Now);

        await using (var writer = ApiContext(SystemIdentity.Instance))
        {
            writer.Add(tenant);
            await writer.SaveChangesAsync();
        }

        tenant.Id.Value.ShouldNotBe(Guid.Empty);
        tenant.RowVersion.Length.ShouldBe(8);

        await using var reader = ApiContext(SystemIdentity.Instance);
        var stored = await reader.Set<Tenant>().SingleAsync(candidate => candidate.Id == tenant.Id);

        stored.Code.ShouldBe(tenant.Code);
        stored.Name.ShouldBe(tenant.Name);
        stored.Status.ShouldBe(TenantStatus.Active);
        stored.PostingApprovalThreshold.ShouldBeNull();
        stored.MaxBackdateDays.ShouldBe(Tenant.InitialMaxBackdateDays);
        stored.DefaultDueDays.ShouldBe(0);
        stored.BillPrefix.ShouldBe("BIL");
        stored.CreatedAtUtc.ShouldBe(Now);
        stored.RowVersion.ShouldBe(tenant.RowVersion);
    }

    [Fact]
    public async Task Two_tenants_cannot_share_a_code()
    {
        await using (var first = ApiContext(SystemIdentity.Instance))
        {
            first.Add(Tenant.Create("city-hospital", "City Hospital", Now).Value);
            await first.SaveChangesAsync();
        }

        await using var second = ApiContext(SystemIdentity.Instance);
        second.Add(Tenant.Create("city-hospital", "The Other City Hospital", Now).Value);

        var exception = await Should.ThrowAsync<DbUpdateException>(() => second.SaveChangesAsync());

        // 2601: a unique index refused the row.
        exception.InnerException.ShouldBeOfType<SqlException>().Number.ShouldBe(2601);
    }

    [Theory]
    [InlineData(nameof(Tenant.BillPrefix), "bil")] // lower case: the binary collation of the CHECK refuses it
    [InlineData(nameof(Tenant.RefundPrefix), "R-F")] // a character outside A-Z0-9
    [InlineData(nameof(Tenant.PaymentPrefix), "")] // shorter than one character
    [InlineData(nameof(Tenant.MaxBackdateDays), 366)]
    [InlineData(nameof(Tenant.DefaultDueDays), -1)]
    public async Task A_value_the_check_constraints_forbid_is_refused_by_the_database(string property, object value)
    {
        await using var context = ApiContext(SystemIdentity.Instance);
        var tenant = TestData.NewTenant(Now);
        context.Entry(tenant).Property(property).CurrentValue = value;
        context.Add(tenant);

        var exception = await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());

        // 547: a CHECK constraint refused the row.
        exception.InnerException.ShouldBeOfType<SqlException>().Number.ShouldBe(547);
    }

    [Fact]
    public async Task A_status_the_enum_does_not_have_is_refused_by_the_database()
    {
        await using var context = ApiContext(SystemIdentity.Instance);
        var tenant = TestData.NewTenant(Now);
        context.Entry(tenant).Property(candidate => candidate.Status).CurrentValue = (TenantStatus)42;
        context.Add(tenant);

        var exception = await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());

        exception.InnerException.ShouldBeOfType<SqlException>().Number.ShouldBe(547);
    }
}
