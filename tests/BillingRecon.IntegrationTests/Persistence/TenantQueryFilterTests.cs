using BillingRecon.Domain.Audit;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Domain.Payers;
using BillingRecon.Domain.Payments;
using BillingRecon.Domain.Receivables;
using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.Infrastructure.Identity;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BillingRecon.IntegrationTests.Persistence;

/// <summary>
/// The read side of tenant isolation (BR-002, ADR 0009): every query on a tenant-scoped entity is filtered by the
/// tenant of the context that runs it, and a tenant sees its own row of the Tenants table and no other. The tests
/// look at the SQL Entity Framework generates, which needs no database.
/// </summary>
public sealed class TenantQueryFilterTests
{
    [Fact]
    public void Two_contexts_with_different_tenants_filter_the_same_query_by_their_own_tenant()
    {
        var first = FixedIdentity.Random();
        var second = FixedIdentity.Random();

        // The model is built once, by whichever context is used first, and shared by both. If the tenant had been
        // captured while the model was built, the second context would show the first tenant.
        using var firstContext = TestContexts.Create(first);
        using var secondContext = TestContexts.Create(second);

        var firstSql = firstContext.Set<Widget>().Where(widget => widget.Name == "probe").ToQueryString();
        var secondSql = secondContext.Set<Widget>().Where(widget => widget.Name == "probe").ToQueryString();

        firstSql.ShouldContain(first.TenantId.ToString()!);
        firstSql.ShouldNotContain(second.TenantId.ToString()!);
        secondSql.ShouldContain(second.TenantId.ToString()!);
        secondSql.ShouldNotContain(first.TenantId.ToString()!);
    }

    [Fact]
    public void The_tenant_is_passed_as_a_parameter_and_compared_with_the_TenantId_column()
    {
        using var context = TestContexts.Create(FixedIdentity.Random());

        var sql = context.Set<Widget>().ToQueryString();

        sql.ShouldContain("DECLARE @");
        sql.ShouldContain("uniqueidentifier = '");
        sql.ShouldMatch(@"WHERE \[w\]\.\[TenantId\] = @\w+");
    }

    [Fact]
    public void The_tenant_is_read_again_for_every_query()
    {
        var identity = FixedIdentity.Random();
        using var context = TestContexts.Create(identity);
        context.Set<Widget>().ToQueryString().ShouldContain(identity.TenantId.ToString()!);

        var next = new TenantId(Guid.NewGuid());
        identity.TenantId = next;

        context.Set<Widget>().ToQueryString().ShouldContain(next.ToString());
    }

    [Fact]
    public void Every_tenant_scoped_entity_of_the_test_model_is_filtered_and_guarded()
    {
        using var context = TestContexts.Create(FixedIdentity.Random());

        var protectedEntities = AssertEveryTenantColumnIsProtected(context);

        // The test model is the production model plus the probe entities.
        protectedEntities.ShouldBe([typeof(Widget), typeof(LogLine), typeof(NumberedDocument), typeof(Patient), typeof(Payer), typeof(ServiceItem), typeof(Bill), typeof(BillLine), typeof(NumberSeries), typeof(IdempotencyKey), typeof(AuditRecord), typeof(LedgerPosting), typeof(LedgerEntry), typeof(PayerBalance), typeof(ApprovalRequest), typeof(Payment), typeof(PaymentAllocation), typeof(CreditNote), typeof(CreditNoteLine), typeof(CreditNoteAllocation)], ignoreOrder: true);
    }

    [Fact]
    public void Every_tenant_scoped_entity_of_the_production_model_is_filtered_and_guarded()
    {
        // Tenants is the tenant itself; Patients and Payers (Prompt 2.2) are the first tenant-scoped tables, ServiceItems
        // (Prompt 2.3), Bills and BillLines (Prompt 2.4) NumberSeries (Prompt 3.1), IdempotencyKeys (Prompt 3.2) AuditRecords (Prompt 3.4) and the three Receivables tables (Prompt 3.3) the next. Every one that follows is checked here as well: a TenantId column that is not protected
        // fails.
        using var context = TestContexts.CreateProduction(FixedIdentity.Random());

        var protectedEntities = AssertEveryTenantColumnIsProtected(context);

        protectedEntities.ShouldBe([typeof(Patient), typeof(Payer), typeof(ServiceItem), typeof(Bill), typeof(BillLine), typeof(NumberSeries), typeof(IdempotencyKey), typeof(AuditRecord), typeof(LedgerPosting), typeof(LedgerEntry), typeof(PayerBalance), typeof(ApprovalRequest), typeof(Payment), typeof(PaymentAllocation), typeof(CreditNote), typeof(CreditNoteLine), typeof(CreditNoteAllocation)], ignoreOrder: true);
    }

    [Fact]
    public void An_entity_without_a_tenant_is_not_filtered_and_needs_no_tenant()
    {
        using var context = TestContexts.Create(SystemIdentity.Instance);

        var sql = context.Set<Setting>().ToQueryString();

        sql.ShouldNotContain("WHERE");
        sql.ShouldNotContain("TenantId");
    }

    [Fact]
    public void A_tenant_sees_its_own_row_of_the_tenants_table_and_no_other()
    {
        var identity = FixedIdentity.Random();
        using var context = TestContexts.CreateProduction(identity);

        var sql = context.Set<Tenant>().ToQueryString();

        sql.ShouldContain("FROM [Tenants]");
        sql.ShouldMatch(@"WHERE \[t\]\.\[Id\] = @\w+");
        sql.ShouldContain(identity.TenantId.ToString()!);
    }

    [Fact]
    public void Two_tenants_each_see_their_own_tenant_row()
    {
        var first = FixedIdentity.Random();
        var second = FixedIdentity.Random();
        using var firstContext = TestContexts.CreateProduction(first);
        using var secondContext = TestContexts.CreateProduction(second);

        firstContext.Set<Tenant>().ToQueryString().ShouldContain(first.TenantId.ToString()!);
        secondContext.Set<Tenant>().ToQueryString().ShouldContain(second.TenantId.ToString()!);
        secondContext.Set<Tenant>().ToQueryString().ShouldNotContain(first.TenantId.ToString()!);
    }

    [Fact]
    public void System_work_sees_every_tenant()
    {
        // No tenant in scope: the predicate is resolved away at query compile time, so the SQL has no WHERE at all.
        using var context = TestContexts.CreateProduction();

        var sql = context.Set<Tenant>().ToQueryString();

        sql.ShouldContain("FROM [Tenants]");
        sql.ShouldNotContain("WHERE");
    }

    [Fact]
    public void A_query_on_a_tenant_scoped_entity_in_a_scope_without_a_tenant_fails()
    {
        using var context = TestContexts.Create(SystemIdentity.Instance);

        var exception = Should.Throw<Exception>(() => context.Set<Widget>().ToQueryString());

        // Entity Framework may wrap what the filter throws; the cause is what matters.
        exception.WithInnerExceptions().ShouldContain(inner => inner is TenantIsolationException);
    }

    [Fact]
    public void An_unnamed_filter_a_module_adds_to_a_tenant_scoped_entity_is_refused_when_the_model_is_built()
    {
        // The tenant filter is a named filter. Entity Framework does not mix named and unnamed filters on one entity,
        // so an unnamed one a module adds fails the model instead of silently replacing the tenant filter, which is
        // what happened before EF Core 10 (ADR 0015, section 3).
        using var context = TestContexts.CreateProbe<UnnamedModuleFilter>();

        var exception = Should.Throw<InvalidOperationException>(() => _ = context.Model);

        exception.Message.ShouldContain("filter", Case.Insensitive);
    }

    [Fact]
    public void A_named_filter_a_module_adds_applies_together_with_the_tenant_filter()
    {
        using var context = TestContexts.CreateProbe<NamedModuleFilter>();

        var sql = context.Set<Scoped>().ToQueryString();

        sql.ShouldContain("[TenantId] = @");
        sql.ShouldContain("[Hidden]");
    }

    // Every entity that has a TenantId column must get all three protections, which hang on ITenantScoped: the query
    // filter, the tenant in the WHERE clause of UPDATE and DELETE, and a column that an UPDATE can never set.
    private static Type[] AssertEveryTenantColumnIsProtected(DbContext context)
    {
        var protectedEntities = new List<Type>();

        foreach (var entityType in context.Model.GetEntityTypes())
        {
            var tenant = entityType.FindProperty(nameof(ITenantScoped.TenantId));
            if (tenant is null)
            {
                continue;
            }

            var name = entityType.ClrType.Name;
            typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType).ShouldBeTrue($"{name} has a TenantId but does not implement ITenantScoped");
            QueryAll(context, entityType.ClrType).ToQueryString().ShouldContain("[TenantId] = @", customMessage: $"{name} is not filtered by tenant");
            tenant.IsConcurrencyToken.ShouldBeTrue($"{name}.TenantId is not part of the WHERE clause of UPDATE and DELETE");
            tenant.GetAfterSaveBehavior().ShouldBe(PropertySaveBehavior.Throw, $"{name}.TenantId could be the target of an UPDATE");
            protectedEntities.Add(entityType.ClrType);
        }

        return [.. protectedEntities];
    }

    private static IQueryable QueryAll(DbContext context, Type entity)
        => (IQueryable)typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!.MakeGenericMethod(entity).Invoke(context, null)!;

    public sealed class Scoped : ITenantScoped
    {
        public int Id { get; set; }

        public TenantId TenantId { get; private set; }

        public bool Hidden { get; set; }
    }

    public sealed class UnnamedModuleFilter : IModelProbe
    {
        public void Configure(ModelBuilder modelBuilder) => modelBuilder.Entity<Scoped>().HasQueryFilter(scoped => !scoped.Hidden);
    }

    public sealed class NamedModuleFilter : IModelProbe
    {
        public void Configure(ModelBuilder modelBuilder) => modelBuilder.Entity<Scoped>().HasQueryFilter("Hidden", scoped => !scoped.Hidden);
    }
}
