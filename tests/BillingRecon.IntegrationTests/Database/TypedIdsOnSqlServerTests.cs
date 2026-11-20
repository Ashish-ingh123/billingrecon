using BillingRecon.IntegrationTests.Persistence;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>Typed ids against SQL Server (ADR 0015): stored as the plain uniqueidentifier, and in the order they were generated.</summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class TypedIdsOnSqlServerTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private readonly FixedIdentity _identity = FixedIdentity.Random();

    [Fact]
    public async Task A_typed_id_is_stored_as_the_guid_it_wraps()
    {
        await AddProbeTenantRowsAsync(_identity);
        var widget = TestData.NewWidget();
        await using var context = ProbeContext(_identity);
        context.Add(widget);
        await context.SaveChangesAsync();

        var stored = await context.Database.SqlQueryRaw<Guid>("SELECT [Id] AS [Value] FROM [Widget]").ToListAsync();

        stored.ShouldBe([widget.Id.Value]);
    }

    [Fact]
    public async Task Ids_generated_in_the_application_sort_in_the_order_the_rows_were_added()
    {
        await AddProbeTenantRowsAsync(_identity);
        var widgets = Enumerable.Range(1, 5).Select(index => new Widget($"widget {index}")).ToArray();
        await using (var context = ProbeContext(_identity))
        {
            context.AddRange(widgets);
            await context.SaveChangesAsync();
        }

        await using var reader = ProbeContext(_identity);
        var names = await reader.Set<Widget>().OrderBy(widget => widget.Id).Select(widget => widget.Name).ToListAsync();

        names.ShouldBe(widgets.Select(widget => widget.Name));
    }
}
