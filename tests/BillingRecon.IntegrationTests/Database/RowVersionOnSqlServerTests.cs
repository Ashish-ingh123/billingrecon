using BillingRecon.IntegrationTests.Persistence;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>Optimistic concurrency against SQL Server (BR-804, ADR 0007): the engine owns the row version, and a stale one loses.</summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class RowVersionOnSqlServerTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private readonly FixedIdentity _identity = FixedIdentity.Random();

    [Fact]
    public async Task The_database_assigns_the_row_version_on_insert_and_changes_it_on_update()
    {
        var widget = TestData.NewWidget();
        await using var context = ProbeContext(_identity);
        context.Add(widget);
        widget.RowVersion.ShouldBeEmpty();

        await context.SaveChangesAsync();
        var afterInsert = widget.RowVersion.ToArray();

        widget.Rename("changed");
        await context.SaveChangesAsync();

        afterInsert.Length.ShouldBe(8);
        widget.RowVersion.Length.ShouldBe(8);
        widget.RowVersion.ShouldNotBe(afterInsert);
    }

    [Fact]
    public async Task The_second_of_two_writers_that_read_the_same_version_fails_and_changes_nothing()
    {
        var widget = TestData.NewWidget();
        await using (var context = ProbeContext(_identity))
        {
            context.Add(widget);
            await context.SaveChangesAsync();
        }

        await using var first = ProbeContext(_identity);
        await using var second = ProbeContext(_identity);
        var firstCopy = await first.Set<Widget>().SingleAsync(candidate => candidate.Id == widget.Id);
        var secondCopy = await second.Set<Widget>().SingleAsync(candidate => candidate.Id == widget.Id);

        firstCopy.Rename("first wins");
        await first.SaveChangesAsync();

        secondCopy.Rename("second loses");
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        await using var reader = ProbeContext(_identity);
        (await reader.Set<Widget>().SingleAsync(candidate => candidate.Id == widget.Id)).Name.ShouldBe("first wins");
    }
}
