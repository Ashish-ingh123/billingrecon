using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Infrastructure.Billing;
using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.Infrastructure.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The ServiceItems table as the migrations create it on SQL Server 2022 (docs/database.md, section 3.3): its
/// constraints, indexes and foreign key, and the unique index on the code at work.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class ServiceItemsOnSqlServerTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    [Fact]
    public async Task The_table_has_the_documented_check_constraints()
    {
        (await NamesAsync("SELECT [name] AS [Value] FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.ServiceItems')"))
            .ShouldBe(["CK_ServiceItems_DefaultUnitPrice", "CK_ServiceItems_TaxRatePercent", "CK_ServiceItems_Status"], ignoreOrder: true);
    }

    [Fact]
    public async Task The_table_has_the_documented_indexes_each_led_by_the_tenant_and_no_other()
    {
        var indexes = await NamesAsync(
            """
            SELECT i.[name] + ':' + c.[name] + ':' + CAST(i.is_unique AS varchar(1)) + ':' + CAST(i.has_filter AS varchar(1)) AS [Value]
            FROM sys.indexes AS i
            JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1
            JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(N'dbo.ServiceItems') AND i.is_primary_key = 0
            """);

        indexes.ShouldBe(["UX_ServiceItems_TenantId_Code:TenantId:1:0", "IX_ServiceItems_TenantId_Name:TenantId:0:0"], ignoreOrder: true);
    }

    [Fact]
    public async Task The_foreign_key_to_the_tenant_exists_and_deletes_nothing()
    {
        // delete_referential_action 0 is NO ACTION.
        (await NamesAsync("SELECT [name] + ':' + CAST(delete_referential_action AS varchar(1)) AS [Value] FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.ServiceItems')"))
            .ShouldBe(["FK_ServiceItems_TenantId:0", "FK_ServiceItems_CreatedByUserId:0", "FK_ServiceItems_UpdatedByUserId:0"], ignoreOrder: true);
    }

    [Fact]
    public async Task An_item_round_trips_with_its_price_and_tax_rate_exact_and_the_reads_find_it_whatever_the_case_of_the_code()
    {
        var clerk = await NewApiTenantAsync();
        await using (var context = ApiContext(clerk))
        {
            context.Add(Item("LAB/CBC"));
            await context.SaveChangesAsync();
        }

        await using var reader = ApiContext(clerk);
        var stored = await reader.Set<ServiceItem>().SingleAsync();
        var items = new ServiceItemReadService(reader);

        stored.Code.ShouldBe(ServiceItemCode.Of("LAB/CBC"));
        stored.Name.ShouldBe("Complete blood count");
        stored.Category.ShouldBe("Laboratory");
        stored.DefaultUnitPrice.ShouldBe(UnitPrice.Of(333.3333m));
        stored.TaxRate.ShouldBe(Percent.Of(2.25m));
        stored.Status.ShouldBe(ServiceItemStatus.Active);
        stored.TenantId.ShouldBe(clerk.TenantId!.Value);
        stored.CreatedAtUtc.ShouldBe(stored.UpdatedAtUtc);
        stored.RowVersion.Length.ShouldBe(8);
        (await items.CodeExistsAsync(ServiceItemCode.Of("lab/cbc"), CancellationToken.None)).ShouldBeTrue();
        (await items.CodeExistsAsync(ServiceItemCode.Of("LAB/CB"), CancellationToken.None)).ShouldBeFalse();
        (await items.GetAsync(stored.Id, CancellationToken.None)).ShouldNotBeNull().TaxRatePercent.ShouldBe(2.25m);
    }

    [Fact]
    public async Task The_unique_index_refuses_a_second_item_with_the_code_in_another_case_and_allows_it_in_another_tenant()
    {
        var clerk = await NewApiTenantAsync();
        var elsewhere = await NewApiTenantAsync();
        await using (var context = ApiContext(clerk))
        {
            context.Add(Item("LAB/CBC"));
            (await new UnitOfWork(context).CommitAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
        }

        await using (var second = ApiContext(clerk))
        {
            second.Add(Item("lab/cbc"));
            (await new UnitOfWork(second).CommitAsync(CancellationToken.None)).Error.Code.ShouldBe(CommonErrors.DuplicateKeyCode);
        }

        await using var other = ApiContext(elsewhere);
        other.Add(Item("LAB/CBC"));
        (await new UnitOfWork(other).CommitAsync(CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("UPDATE [dbo].[ServiceItems] SET [DefaultUnitPrice] = -0.0001")]
    [InlineData("UPDATE [dbo].[ServiceItems] SET [TaxRatePercent] = -0.01")]
    [InlineData("UPDATE [dbo].[ServiceItems] SET [TaxRatePercent] = 100.01")]
    [InlineData("UPDATE [dbo].[ServiceItems] SET [Status] = 'Deleted'")]
    public async Task A_value_the_check_constraints_forbid_is_refused_by_the_database(string update)
    {
        // The value objects cannot hold these values, so the row is changed underneath them, as a script would.
        var clerk = await NewApiTenantAsync();
        await using var context = ApiContext(clerk);
        context.Add(Item("LAB/CBC"));
        await context.SaveChangesAsync();

        var exception = await Should.ThrowAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(update));

        // 547: a CHECK constraint refused the row.
        exception.Number.ShouldBe(547);
    }

    [Fact]
    public async Task A_status_the_enum_does_not_have_is_refused_by_the_database()
    {
        var clerk = await NewApiTenantAsync();
        await using var context = ApiContext(clerk);
        var item = Item("LAB/CBC");
        context.Entry(item).Property(candidate => candidate.Status).CurrentValue = (ServiceItemStatus)42;
        context.Add(item);

        var exception = await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());

        exception.InnerException.ShouldBeOfType<SqlException>().Number.ShouldBe(547);
    }

    private static ServiceItem Item(string code) => ServiceItem.Create(
        ServiceItemCode.Of(code),
        new ServiceItemDetails
        {
            Name = "Complete blood count",
            Category = "Laboratory",
            DefaultUnitPrice = UnitPrice.Of(333.3333m),
            TaxRate = Percent.Of(2.25m),
        });

    private async Task<List<string>> NamesAsync(string sql)
    {
        await using var context = ApiContext(SystemIdentity.Instance);

        return await context.Database.SqlQueryRaw<string>(sql).ToListAsync();
    }
}
