using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Persistence;

/// <summary>
/// The write side of tenant isolation (BR-002, ADR 0009, ADR 0015), exercised on the change tracker without a
/// database: what the interceptor stamps, what it refuses before anything is sent, and that a tenant may write its
/// own row of the Tenants table and no other. The same rules against a real SQL Server follow with the
/// Testcontainers tests.
/// </summary>
public sealed class TenantWriteGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 6, 30, 0, TimeSpan.Zero);

    private readonly FixedIdentity _identity = FixedIdentity.Random();
    private readonly TenantId _otherTenant = new(Guid.NewGuid());

    private TenantId CurrentTenant => _identity.TenantId!.Value;

    [Fact]
    public void An_added_entity_without_a_tenant_gets_the_current_tenant()
    {
        using var context = TestContexts.Create(_identity);
        var widget = new Widget("new");
        context.Add(widget);
        widget.TenantId.ShouldBe(default);

        Guard().Apply(context);

        widget.TenantId.ShouldBe(CurrentTenant);
        context.Entry(widget).State.ShouldBe(EntityState.Added);
    }

    [Fact]
    public void An_added_entity_that_already_carries_the_current_tenant_is_accepted()
    {
        using var context = TestContexts.Create(_identity);
        var widget = new Widget("new");
        context.Entry(widget).Property(entity => entity.TenantId).CurrentValue = CurrentTenant;
        context.Add(widget);

        Guard().Apply(context);

        widget.TenantId.ShouldBe(CurrentTenant);
    }

    [Fact]
    public void An_added_entity_that_carries_another_tenant_is_refused()
    {
        using var context = TestContexts.Create(_identity);
        var widget = new Widget("smuggled");
        context.Entry(widget).Property(entity => entity.TenantId).CurrentValue = _otherTenant;
        context.Add(widget);

        var exception = Should.Throw<TenantIsolationException>(() => Guard().Apply(context));

        exception.Message.ShouldContain("Cannot add Widget");
        exception.Message.ShouldContain(_otherTenant.ToString());
        exception.Message.ShouldContain(CurrentTenant.ToString());
        widget.TenantId.ShouldBe(_otherTenant);
    }

    [Fact]
    public void An_added_row_whose_tenant_is_part_of_its_key_gets_the_current_tenant_in_place_of_the_temporary_one()
    {
        // IdempotencyKeys is keyed by (TenantId, Key). Entity Framework tracks a new row under a temporary tenant
        // until the key is known; that placeholder is not a tenant anyone gave the row.
        using var context = TestContexts.CreateProduction(_identity);
        var key = NewIdempotencyKey();
        var entry = context.Add(key);
        entry.Property(row => row.TenantId).IsTemporary.ShouldBeTrue();

        Guard().Apply(context);

        key.TenantId.ShouldBe(CurrentTenant);
        entry.Property(row => row.TenantId).CurrentValue.ShouldBe(CurrentTenant);
        entry.Property(row => row.TenantId).IsTemporary.ShouldBeFalse();
    }

    [Fact]
    public void An_added_row_whose_tenant_is_part_of_its_key_and_names_another_tenant_is_refused()
    {
        using var context = TestContexts.CreateProduction(_identity);
        var key = NewIdempotencyKey();
        context.Entry(key).Property(row => row.TenantId).CurrentValue = _otherTenant;
        context.Add(key);

        var exception = Should.Throw<TenantIsolationException>(() => Guard().Apply(context));

        exception.Message.ShouldContain("Cannot add IdempotencyKey");
        exception.Message.ShouldContain(_otherTenant.ToString());
    }

    [Fact]
    public void A_modified_entity_of_the_current_tenant_is_accepted()
    {
        using var context = TestContexts.Create(_identity);
        var widget = context.TrackExisting(new Widget("mine"), CurrentTenant);
        widget.Rename("still mine");

        Guard().Apply(context);

        context.Entry(widget).State.ShouldBe(EntityState.Modified);
        widget.TenantId.ShouldBe(CurrentTenant);
    }

    [Fact]
    public void A_modified_entity_of_another_tenant_is_refused()
    {
        using var context = TestContexts.Create(_identity);
        var widget = context.TrackExisting(new Widget("theirs"), _otherTenant);
        widget.Rename("mine now");

        var exception = Should.Throw<TenantIsolationException>(() => Guard().Apply(context));

        exception.Message.ShouldContain("Cannot update Widget");
        exception.Message.ShouldContain($"belongs to tenant {_otherTenant}");
    }

    [Fact]
    public void A_deleted_entity_of_another_tenant_is_refused()
    {
        using var context = TestContexts.Create(_identity);
        var widget = context.TrackExisting(new Widget("theirs"), _otherTenant);
        context.Remove(widget);

        var exception = Should.Throw<TenantIsolationException>(() => Guard().Apply(context));

        exception.Message.ShouldContain("Cannot delete Widget");
    }

    [Fact]
    public void A_deleted_entity_of_the_current_tenant_is_no_tenant_violation()
    {
        // The guard decides about tenants only. Whether a row may be deleted at all is another rule (CLAUDE.md
        // section 4, rule 10), with its own guard still to come.
        using var context = TestContexts.Create(_identity);
        var widget = context.TrackExisting(new Widget("mine"), CurrentTenant);
        context.Remove(widget);

        Should.NotThrow(() => Guard().Apply(context));
    }

    [Fact]
    public void A_modified_entity_with_an_empty_tenant_is_refused()
    {
        using var context = TestContexts.Create(_identity);
        var widget = context.TrackExisting(new Widget("nobody's"), default);
        widget.Rename("mine now");

        var exception = Should.Throw<TenantIsolationException>(() => Guard().Apply(context));

        exception.Message.ShouldContain($"belongs to tenant {Guid.Empty}");
    }

    [Fact]
    public void Moving_a_row_of_the_current_tenant_to_another_tenant_is_refused()
    {
        using var context = TestContexts.Create(_identity);
        var widget = context.TrackExisting(new Widget("mine"), CurrentTenant);
        context.Entry(widget).Property(entity => entity.TenantId).CurrentValue = _otherTenant;

        var exception = Should.Throw<TenantIsolationException>(() => Guard().Apply(context));

        exception.Message.ShouldContain($"its tenant was changed from {CurrentTenant} to {_otherTenant}");
    }

    [Fact]
    public void Claiming_a_row_of_another_tenant_for_the_current_tenant_is_refused()
    {
        using var context = TestContexts.Create(_identity);
        var widget = context.TrackExisting(new Widget("theirs"), _otherTenant);
        context.Entry(widget).Property(entity => entity.TenantId).CurrentValue = CurrentTenant;

        var exception = Should.Throw<TenantIsolationException>(() => Guard().Apply(context));

        exception.Message.ShouldContain($"its tenant was changed from {_otherTenant} to {CurrentTenant}");
    }

    [Fact]
    public void An_unchanged_entity_is_not_inspected_because_nothing_is_written_for_it()
    {
        using var context = TestContexts.Create(_identity);
        context.TrackExisting(new Widget("theirs, untouched"), _otherTenant);

        Should.NotThrow(() => Guard().Apply(context));
    }

    [Fact]
    public void Saving_a_tenant_scoped_entity_in_a_scope_without_a_tenant_is_refused()
    {
        using var context = TestContexts.Create(SystemIdentity.Instance);
        context.Add(new Widget("orphan"));

        var exception = Should.Throw<TenantIsolationException>(() => new TenantWriteGuardInterceptor(SystemIdentity.Instance).Apply(context));

        exception.Message.ShouldContain("the current scope has no tenant");
    }

    [Fact]
    public void A_save_that_writes_no_tenant_scoped_entity_never_asks_for_the_tenant()
    {
        // This identity throws when it is asked for the tenant, so the test fails if the guard asks.
        var noIdentity = new ThrowingIdentity();
        using var context = TestContexts.Create(noIdentity);
        context.Add(new Setting("colour"));

        Should.NotThrow(() => new TenantWriteGuardInterceptor(noIdentity).Apply(context));
    }

    [Fact]
    public void The_message_names_the_entity_and_its_id_but_no_data()
    {
        using var context = TestContexts.Create(_identity);
        var widget = context.TrackExisting(new Widget("Priya Sharma 9876543210"), _otherTenant);
        widget.Rename("Priya Sharma 9876543211");

        var exception = Should.Throw<TenantIsolationException>(() => Guard().Apply(context));

        exception.Message.ShouldContain($"Widget (Id {widget.Id})");
        exception.Message.ShouldNotContain("Priya");
        exception.Message.ShouldNotContain("9876543210");
    }

    [Fact]
    public void SaveChanges_runs_the_guard_before_anything_is_sent_to_the_database()
    {
        // No interceptor is handed to the context here: it attaches the guard to itself.
        using var context = TestContexts.Create(_identity);
        var widget = context.TrackExisting(new Widget("theirs"), _otherTenant);
        widget.Rename("mine now");

        // The connection string points at a closed port: a save that got past the guard would fail with a
        // connection error instead.
        Should.Throw<TenantIsolationException>(() => context.SaveChanges());
    }

    [Fact]
    public async Task SaveChangesAsync_runs_the_guard_before_anything_is_sent_to_the_database()
    {
        await using var context = TestContexts.Create(_identity);
        var widget = context.TrackExisting(new Widget("theirs"), _otherTenant);
        widget.Rename("mine now");

        await Should.ThrowAsync<TenantIsolationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public void A_context_built_by_hand_for_system_work_refuses_to_save_a_tenant_scoped_entity()
    {
        // As seeding code or a background job would build it: no dependency injection, no tenant, no user.
        using var context = TestContexts.Create(SystemIdentity.Instance);
        context.Add(new Widget("orphan"));

        var exception = Should.Throw<TenantIsolationException>(() => context.SaveChanges());

        exception.Message.ShouldContain("the current scope has no tenant");
    }

    [Fact]
    public void A_tenant_may_update_its_own_tenant_row()
    {
        using var context = TestContexts.CreateProduction(_identity);
        var tenant = ExistingTenant(context, CurrentTenant);
        context.Entry(tenant).State = EntityState.Modified;

        Should.NotThrow(() => Guard().Apply(context));
    }

    [Fact]
    public void A_tenant_may_not_update_another_tenants_row()
    {
        using var context = TestContexts.CreateProduction(_identity);
        var tenant = ExistingTenant(context, _otherTenant);
        context.Entry(tenant).State = EntityState.Modified;

        var exception = Should.Throw<TenantIsolationException>(() => Guard().Apply(context));

        exception.Message.ShouldContain("Cannot update Tenant");
        exception.Message.ShouldContain($"it is tenant {_otherTenant}, but the current tenant is {CurrentTenant}");
    }

    [Fact]
    public void A_tenant_may_not_delete_another_tenants_row()
    {
        using var context = TestContexts.CreateProduction(_identity);
        var tenant = ExistingTenant(context, _otherTenant);
        context.Remove(tenant);

        var exception = Should.Throw<TenantIsolationException>(() => Guard().Apply(context));

        exception.Message.ShouldContain("Cannot delete Tenant");
    }

    [Fact]
    public void System_work_may_update_any_tenant_row()
    {
        // Onboarding and platform operations run without a tenant; the tenant row is not tenant-scoped for them.
        using var context = TestContexts.CreateProduction();
        var tenant = ExistingTenant(context, _otherTenant);
        context.Entry(tenant).State = EntityState.Modified;

        Should.NotThrow(() => new TenantWriteGuardInterceptor(SystemIdentity.Instance).Apply(context));
    }

    [Fact]
    public void Adding_a_tenant_is_not_a_question_of_isolation()
    {
        // Who may onboard a tenant is authorisation, not isolation; the guard leaves an added tenant alone.
        using var context = TestContexts.CreateProduction(_identity);
        context.Add(Tenant.Create("other-clinic", "Other Clinic", Now).Value);

        Should.NotThrow(() => Guard().Apply(context));
    }

    [Fact]
    public void SaveChanges_refuses_another_tenants_row_before_anything_is_sent_to_the_database()
    {
        using var context = TestContexts.CreateProduction(_identity);
        var tenant = ExistingTenant(context, _otherTenant);
        context.Entry(tenant).State = EntityState.Modified;

        Should.Throw<TenantIsolationException>(() => context.SaveChanges());
    }

    [Fact]
    public void An_isolation_violation_is_not_an_invalid_operation_that_a_general_handler_would_catch()
    {
        typeof(TenantIsolationException).BaseType.ShouldBe(typeof(Exception));
    }

    private TenantWriteGuardInterceptor Guard() => new(_identity);

    private static IdempotencyKey NewIdempotencyKey() => IdempotencyKey.Reserve(Guid.NewGuid(), userId: null, "POST", "/api/v1/payments", new byte[IdempotencyKey.RequestHashLength], Now);

    // A tenant row that already exists in the database with the given id, tracked as unchanged.
    private static Tenant ExistingTenant(DbContext context, TenantId id)
    {
        var tenant = Tenant.Create("city-hospital", "City Hospital", Now).Value;
        var entry = context.Entry(tenant);
        entry.Property(entity => entity.Id).CurrentValue = id;
        entry.State = EntityState.Unchanged;

        return tenant;
    }
}
