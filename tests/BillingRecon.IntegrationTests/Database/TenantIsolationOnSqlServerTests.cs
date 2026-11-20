using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.IntegrationTests.Persistence;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// Tenant isolation against SQL Server (BR-002, ADR 0009, ADR 0015), with two real tenants: what each one reads, what a
/// forged statement does to another tenant's row (nothing: the tenant is in the WHERE clause of every UPDATE and
/// DELETE), what system work may touch, and the Tenants table itself.
/// </summary>
[Collection(DatabaseCollectionDefinition.Name)]
public sealed class TenantIsolationOnSqlServerTests(SqlServerFixture sqlServer) : DatabaseTest(sqlServer)
{
    private readonly FixedIdentity _tenantA = FixedIdentity.Random();
    private readonly FixedIdentity _tenantB = FixedIdentity.Random();

    [Fact]
    public async Task Each_tenant_reads_only_its_own_rows()
    {
        await AddProbeTenantRowsAsync(_tenantA, _tenantB);
        await SaveWidgetAsync(_tenantA, "a-first");
        await SaveWidgetAsync(_tenantA, "a-second");
        var theirs = await SaveWidgetAsync(_tenantB, "b-only");

        await using var asA = ProbeContext(_tenantA);
        await using var asB = ProbeContext(_tenantB);

        (await asA.Set<Widget>().Select(widget => widget.Name).ToListAsync()).ShouldBe(["a-first", "a-second"], ignoreOrder: true);
        (await asA.Set<Widget>().FindAsync(theirs.Id)).ShouldBeNull();
        (await asB.Set<Widget>().Select(widget => widget.Name).ToListAsync()).ShouldBe(["b-only"]);
    }

    [Fact]
    public async Task A_forged_update_of_another_tenants_row_changes_no_row()
    {
        await AddProbeTenantRowsAsync(_tenantA, _tenantB);
        var theirs = await SaveWidgetAsync(_tenantA, "original");

        await using (var asB = ProbeContext(_tenantB))
        {
            // A detached copy that passes every in-memory check: the row's id, the right row version, and the current
            // tenant. Only the tenant predicate of the UPDATE statement stands between it and the other tenant's row.
            var forged = new Widget("hijacked");
            var entry = asB.Entry(forged);
            entry.Property(widget => widget.Id).CurrentValue = theirs.Id;
            entry.Property(widget => widget.TenantId).CurrentValue = _tenantB.TenantId!.Value;
            entry.Property(widget => widget.RowVersion).CurrentValue = theirs.RowVersion;
            asB.Update(forged);

            await Should.ThrowAsync<DbUpdateConcurrencyException>(() => asB.SaveChangesAsync());
        }

        (await ReadWidgetAsync(_tenantA, theirs.Id)).Name.ShouldBe("original");
    }

    [Fact]
    public async Task A_forged_delete_of_another_tenants_row_deletes_no_row()
    {
        await AddProbeTenantRowsAsync(_tenantA, _tenantB);
        var theirs = await SaveWidgetAsync(_tenantA, "kept");

        await using (var asB = ProbeContext(_tenantB))
        {
            var forged = new Widget("to delete");
            var entry = asB.Entry(forged);
            entry.Property(widget => widget.Id).CurrentValue = theirs.Id;
            entry.Property(widget => widget.TenantId).CurrentValue = _tenantB.TenantId!.Value;
            entry.Property(widget => widget.RowVersion).CurrentValue = theirs.RowVersion;
            asB.Remove(forged);

            await Should.ThrowAsync<DbUpdateConcurrencyException>(() => asB.SaveChangesAsync());
        }

        (await ReadWidgetAsync(_tenantA, theirs.Id)).Name.ShouldBe("kept");
    }

    [Fact]
    public async Task System_work_writes_and_reads_tables_that_are_not_tenant_scoped_and_nothing_that_is()
    {
        await AddProbeTenantRowsAsync(_tenantA);
        await SaveWidgetAsync(_tenantA, "a");
        await using var system = ProbeContext(SystemIdentity.Instance);
        system.Add(new Setting("colour"));
        await system.SaveChangesAsync();

        (await system.Set<Setting>().CountAsync()).ShouldBe(1);
        var exception = await Should.ThrowAsync<Exception>(() => system.Set<Widget>().ToListAsync());
        exception.WithInnerExceptions().ShouldContain(inner => inner is TenantIsolationException);
    }

    [Fact]
    public async Task Each_tenant_reads_its_own_tenant_row_and_system_work_reads_every_tenant()
    {
        await using (var system = ProbeContext(SystemIdentity.Instance))
        {
            AddTenantRow(system, _tenantA);
            AddTenantRow(system, _tenantB);
            await system.SaveChangesAsync();

            (await system.Set<Tenant>().CountAsync()).ShouldBe(2);
        }

        await using var asA = ProbeContext(_tenantA);
        await using var asB = ProbeContext(_tenantB);

        (await asA.Set<Tenant>().Select(tenant => tenant.Id).ToListAsync()).ShouldBe([_tenantA.TenantId!.Value]);
        (await asB.Set<Tenant>().Select(tenant => tenant.Id).ToListAsync()).ShouldBe([_tenantB.TenantId!.Value]);
    }

    [Fact]
    public async Task A_tenant_cannot_write_another_tenants_row_of_the_tenants_table()
    {
        await using (var system = ProbeContext(SystemIdentity.Instance))
        {
            AddTenantRow(system, _tenantA);
            await system.SaveChangesAsync();
        }

        await using var asB = ProbeContext(_tenantB);
        var forged = TestData.NewTenant(Now);
        asB.Entry(forged).Property(tenant => tenant.Id).CurrentValue = _tenantA.TenantId!.Value;
        asB.Update(forged);

        await Should.ThrowAsync<TenantIsolationException>(() => asB.SaveChangesAsync());
    }

    private static void AddTenantRow(DbContext context, FixedIdentity identity)
    {
        var tenant = TestData.NewTenant(Now);
        context.Entry(tenant).Property(candidate => candidate.Id).CurrentValue = identity.TenantId!.Value;
        context.Add(tenant);
    }

    private async Task<Widget> SaveWidgetAsync(FixedIdentity identity, string name)
    {
        await using var context = ProbeContext(identity);
        var widget = new Widget(name);
        context.Add(widget);
        await context.SaveChangesAsync();

        return widget;
    }

    private async Task<Widget> ReadWidgetAsync(FixedIdentity identity, WidgetId id)
    {
        await using var context = ProbeContext(identity);

        return await context.Set<Widget>().SingleAsync(widget => widget.Id == id);
    }
}
