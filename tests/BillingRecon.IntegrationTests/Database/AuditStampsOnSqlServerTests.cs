using BillingRecon.Domain.Common;
using BillingRecon.IntegrationTests.Persistence;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The audit columns against SQL Server (ADR 0015): what an insert stores, what an update moves and what it leaves
/// alone even when the entity in memory claims otherwise, and the null user of a scope without one.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class AuditStampsOnSqlServerTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private readonly FixedIdentity _creator = FixedIdentity.Random();

    [Fact]
    public async Task An_insert_stores_the_creation_stamp_and_the_touch_with_the_same_instant_and_user()
    {
        await AddProbeTenantRowsAsync(_creator);
        var widget = TestData.NewWidget();

        await using (var context = ProbeContext(_creator))
        {
            context.Add(widget);
            await context.SaveChangesAsync();
        }

        var stored = await ReadAsync(widget.Id);

        stored.CreatedAtUtc.ShouldBe(Now);
        stored.CreatedByUserId.ShouldBe(_creator.UserId);
        stored.UpdatedAtUtc.ShouldBe(Now);
        stored.UpdatedByUserId.ShouldBe(_creator.UserId);
    }

    [Fact]
    public async Task An_update_moves_the_touch_and_leaves_the_creation_stamp_whatever_the_entity_claims()
    {
        await AddProbeTenantRowsAsync(_creator);
        var widget = TestData.NewWidget();
        await using (var context = ProbeContext(_creator))
        {
            context.Add(widget);
            await context.SaveChangesAsync();
        }

        var editor = new FixedIdentity(_creator.TenantId, new UserId(Guid.NewGuid()));
        var later = new FakeClock(Now.AddHours(3));
        await using (var context = ProbeContext(editor, later))
        {
            var tracked = await context.Set<Widget>().SingleAsync(candidate => candidate.Id == widget.Id);
            tracked.Rename("renamed");

            // Tampered creation values: the model leaves these columns out of every UPDATE.
            context.Entry(tracked).Property(candidate => candidate.CreatedAtUtc).CurrentValue = Now.AddYears(-10);
            context.Entry(tracked).Property(candidate => candidate.CreatedByUserId).CurrentValue = editor.UserId;
            await context.SaveChangesAsync();
        }

        var stored = await ReadAsync(widget.Id);

        stored.Name.ShouldBe("renamed");
        stored.CreatedAtUtc.ShouldBe(Now);
        stored.CreatedByUserId.ShouldBe(_creator.UserId);
        stored.UpdatedAtUtc.ShouldBe(Now.AddHours(3));
        stored.UpdatedByUserId.ShouldBe(editor.UserId);
    }

    [Fact]
    public async Task A_scope_with_a_tenant_but_no_user_stores_null_in_the_user_columns()
    {
        await AddProbeTenantRowsAsync(_creator);
        var noUser = new FixedIdentity(_creator.TenantId, userId: null);
        var widget = TestData.NewWidget();

        await using (var context = ProbeContext(noUser))
        {
            context.Add(widget);
            await context.SaveChangesAsync();
        }

        var stored = await ReadAsync(widget.Id);

        stored.CreatedAtUtc.ShouldBe(Now);
        stored.CreatedByUserId.ShouldBeNull();
        stored.UpdatedByUserId.ShouldBeNull();
    }

    [Fact]
    public async Task An_append_only_row_stores_its_creation_stamp()
    {
        await AddProbeTenantRowsAsync(_creator);
        var line = new LogLine("appended");

        await using (var context = ProbeContext(_creator))
        {
            context.Add(line);
            await context.SaveChangesAsync();
        }

        await using var reader = ProbeContext(_creator);
        var stored = await reader.Set<LogLine>().SingleAsync(candidate => candidate.Id == line.Id);

        stored.Text.ShouldBe("appended");
        stored.CreatedAtUtc.ShouldBe(Now);
        stored.CreatedByUserId.ShouldBe(_creator.UserId);
    }

    private async Task<Widget> ReadAsync(WidgetId id)
    {
        await using var context = ProbeContext(_creator);

        return await context.Set<Widget>().SingleAsync(widget => widget.Id == id);
    }
}
