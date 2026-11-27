using BillingRecon.Domain.Common;
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
        var migrations = context.Database.GetMigrations().ToArray();

        // The second migration creates Patients and Payers; it keeps the name it was scaffolded under
        // (docs/database.md, section 8). The ids of the third to the twenty-fourth start with the time they are scaffolded,
        // which is not known here; their names are.
        migrations.Length.ShouldBe(32);
        migrations[..2].ShouldBe(["20261003192738_0001_Tenants", "20261018182819_0002_SyncCurrentModel"]);
        migrations[2].ShouldEndWith("_0003_ServiceItems");
        migrations[3].ShouldEndWith("_0004_Bills");
        migrations[4].ShouldEndWith("_0005_NumberSeries");
        migrations[5].ShouldEndWith("_0006_IdempotencyKeys");
        migrations[6].ShouldEndWith("_0007_Audit");
        migrations[7].ShouldEndWith("_0008_Receivables");
        migrations[8].ShouldEndWith("_0009_BillPosting");
        migrations[9].ShouldEndWith("_0010_Approvals");
        migrations[10].ShouldEndWith("_0011_Payments");
        migrations[11].ShouldEndWith("_0012_PaymentAllocations");
        migrations[12].ShouldEndWith("_0013_CreditNotes");
        migrations[13].ShouldEndWith("_0014_Adjustments");
        migrations[14].ShouldEndWith("_0015_PaymentReversals");
        migrations[15].ShouldEndWith("_0016_CreditApplicationReversals");
        migrations[16].ShouldEndWith("_0017_Refunds");
        migrations[17].ShouldEndWith("_0018_Users");
        migrations[18].ShouldEndWith("_0019_PrefixChanges");
        migrations[19].ShouldEndWith("_0020_DatabaseRoles");
        migrations[20].ShouldEndWith("_0021_PasswordChange");
        migrations[21].ShouldEndWith("_0022_UserSessions");
        migrations[22].ShouldEndWith("_0023_OpeningBalances");
        migrations[23].ShouldEndWith("_0024_PrintedDocuments");
        migrations[24].ShouldEndWith("_0025_LetterheadImages");
        migrations[25].ShouldEndWith("_0026_PrintSettings");
        migrations[26].ShouldEndWith("_0027_PaymentReferenceOnce");
        migrations[27].ShouldEndWith("_0028_DocumentSearch");
        migrations[28].ShouldEndWith("_0029_CashierShifts");
        migrations[29].ShouldEndWith("_0030_ShiftClosingSlip");
        migrations[30].ShouldEndWith("_0031_SecondStep");
        migrations[31].ShouldEndWith("_0032_PasswordResetLink");
        (await context.Database.GetAppliedMigrationsAsync()).ShouldBe(migrations);
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
                "CK_Tenants_PrefixesDistinct", "CK_Tenants_PostingApprovalThreshold",
            ],
            ignoreOrder: true);
        uniqueIndexes.ShouldBe(["UX_Tenants_Code"]);
    }

    [Fact]
    public async Task A_tenant_round_trips_with_an_id_generated_here_and_a_row_version_assigned_there()
    {
        var tenant = TestData.NewTenant(Now);
        var threshold = Money.Of(5000.50m);

        await using (var writer = ApiContext(SystemIdentity.Instance))
        {
            // Written here through the entry rather than through ChangeSettings (BR-607, ADR 0033), so the test stays
            // about the round trip: the Money column goes through its converter on a real SQL Server.
            writer.Entry(tenant).Property(candidate => candidate.PostingApprovalThreshold).CurrentValue = threshold;
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
        stored.PostingApprovalThreshold.ShouldBe(threshold);
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
    [InlineData(nameof(Tenant.RefundPrefix), "BIL")] // the bills' prefix: a prefix belongs to one document type (ADR 0033)
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
    public async Task A_threshold_of_zero_is_refused_by_the_database()
    {
        // No threshold is null; a threshold is above zero (BR-607, ADR 0033).
        await using var context = ApiContext(SystemIdentity.Instance);
        var tenant = TestData.NewTenant(Now);
        context.Entry(tenant).Property(candidate => candidate.PostingApprovalThreshold).CurrentValue = Money.Zero;
        context.Add(tenant);

        var exception = await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());

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
