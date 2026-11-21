using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace BillingRecon.IntegrationTests.Persistence;

/// <summary>
/// The relational conventions that arrived with the first tenant-scoped tables (ADR 0015, Prompt 2.2): every
/// tenant-scoped row refers to its tenant, no foreign key deletes anything, every index of a tenant-scoped table leads
/// with TenantId, and Entity Framework adds no index of its own for a foreign key. Checked on the models, without a
/// database; the database tests show the same constraints at work.
/// </summary>
public sealed class RelationalConventionTests
{
    [Fact]
    public void Every_tenant_scoped_entity_refers_to_the_tenant_row_through_a_foreign_key_on_TenantId()
    {
        using var context = TestContexts.Create(FixedIdentity.Random());
        var tenantScoped = context.Model.GetEntityTypes().Where(entityType => typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType)).ToArray();

        tenantScoped.Length.ShouldBeGreaterThanOrEqualTo(4, "the test model has Patient, Payer, Widget and LogLine");
        foreach (var entityType in tenantScoped)
        {
            var name = entityType.ClrType.Name;
            var foreignKey = entityType.GetForeignKeys()
                .Where(candidate => candidate.Properties.Select(property => property.Name).SequenceEqual([nameof(ITenantScoped.TenantId)]))
                .ShouldHaveSingleItem($"{name} has no single foreign key on TenantId");

            foreignKey.PrincipalEntityType.ClrType.ShouldBe(typeof(Tenant), $"{name}.TenantId does not refer to the tenant row");
            foreignKey.GetConstraintName().ShouldBe($"FK_{entityType.GetTableName()}_TenantId");
            foreignKey.IsRequired.ShouldBeTrue($"{name}.TenantId is optional");
            foreignKey.DependentToPrincipal.ShouldBeNull($"{name} has a navigation to its tenant; loading a row would load the tenant");
        }
    }

    [Fact]
    public void No_foreign_key_of_the_production_or_the_test_model_deletes_anything()
    {
        using var production = TestContexts.CreateProduction();
        using var test = TestContexts.Create(FixedIdentity.Random());

        var foreignKeys = production.Model.GetEntityTypes().Concat(test.Model.GetEntityTypes()).SelectMany(entityType => entityType.GetForeignKeys()).ToArray();

        foreignKeys.ShouldNotBeEmpty();

        // Restrict is ON DELETE NO ACTION. So is ClientCascade in the database: it only lets Entity Framework delete a
        // child that its aggregate removed. Exactly three foreign keys have it: the one from a bill line to its bill
        // (Prompt 2.4), the one from a credit note line to its credit note (Prompt 4.3), both lines of drafts, and the one from a
        // role of a user to the user (Prompt 5.1); which rows may be deleted at all is the business of HardDeleteGuardInterceptor.
        var clientCascades = foreignKeys.Where(foreignKey => foreignKey.DeleteBehavior != DeleteBehavior.Restrict).ToArray();
        clientCascades.ShouldAllBe(foreignKey => foreignKey.DeleteBehavior == DeleteBehavior.ClientCascade);
        clientCascades.Select(foreignKey => foreignKey.GetConstraintName()).Distinct().ShouldBe(["FK_BillLines_BillId", "FK_CreditNoteLines_CreditNoteId", "FK_UserRoles_UserId"], ignoreOrder: true);
        production.Database.GenerateCreateScript().ShouldNotContain("CASCADE", Case.Insensitive);
        test.Database.GenerateCreateScript().ShouldNotContain("CASCADE", Case.Insensitive);
    }

    [Fact]
    public void A_foreign_key_a_configuration_asks_to_cascade_still_deletes_nothing()
    {
        using var context = TestContexts.CreateProbe<CascadingChild>();

        var foreignKey = context.Model.FindEntityType(typeof(Child))!.GetForeignKeys().ShouldHaveSingleItem();

        foreignKey.DeleteBehavior.ShouldBe(DeleteBehavior.Restrict);
        context.Database.GenerateCreateScript().ShouldNotContain("CASCADE", Case.Insensitive);
    }

    [Fact]
    public void A_foreign_key_gets_no_index_of_its_own()
    {
        // Entity Framework would add IX_Child_ParentId; a table declares the indexes it needs, tenant first.
        using var context = TestContexts.CreateProbe<CascadingChild>();

        context.Model.FindEntityType(typeof(Child))!.GetIndexes().ShouldBeEmpty();
    }

    [Fact]
    public void An_index_of_a_tenant_scoped_table_that_does_not_lead_with_TenantId_fails_the_model()
    {
        using var context = TestContexts.CreateProbe<IndexWithoutTenant>();

        var exception = Should.Throw<InvalidOperationException>(() => _ = context.Model);

        exception.Message.ShouldContain("Every index of a tenant-scoped table leads with TenantId");
        exception.Message.ShouldContain("Indexed: IX_Indexed_Name on (Name)");
    }

    [Fact]
    public void An_index_that_leads_with_TenantId_is_accepted()
    {
        using var context = TestContexts.CreateProbe<IndexLedByTenant>();

        Should.NotThrow(() => _ = context.Model);
    }

    [Fact]
    public void Entity_Framework_logs_a_failed_command_save_or_query_at_debug_because_the_exception_is_reported_once_elsewhere()
    {
        // ADR 0011: the exception handler logs a fault once, and a lost race is an expected outcome (ADR 0015). Logged by
        // Entity Framework as well, a lost race would be an error with the duplicate key in its message (ADR 0012).
        using var context = TestContexts.CreateProduction();
        var warnings = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!.WarningsConfiguration;

        warnings.GetLevel(RelationalEventId.CommandError).ShouldBe(LogLevel.Debug);
        warnings.GetLevel(CoreEventId.SaveChangesFailed).ShouldBe(LogLevel.Debug);
        warnings.GetLevel(CoreEventId.QueryIterationFailed).ShouldBe(LogLevel.Debug);
    }

    public sealed class Indexed : ITenantScoped
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public TenantId TenantId { get; private set; }
    }

    public sealed class Parent
    {
        public int Id { get; set; }
    }

    public sealed class Child
    {
        public int Id { get; set; }

        public int ParentId { get; set; }
    }

    public sealed class IndexWithoutTenant : IModelProbe
    {
        public void Configure(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Indexed>(entity =>
            {
                entity.Property(indexed => indexed.Name).HasMaxLength(50);
                entity.HasIndex(indexed => indexed.Name).HasDatabaseName("IX_Indexed_Name");
            });
    }

    public sealed class IndexLedByTenant : IModelProbe
    {
        public void Configure(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Indexed>(entity =>
            {
                entity.Property(indexed => indexed.Name).HasMaxLength(50);
                entity.HasIndex(indexed => new { indexed.TenantId, indexed.Name }).IsUnique().HasDatabaseName("UX_Indexed_TenantId_Name");
            });
    }

    public sealed class CascadingChild : IModelProbe
    {
        public void Configure(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Parent>();
            modelBuilder.Entity<Child>().HasOne<Parent>().WithMany().HasForeignKey(child => child.ParentId).OnDelete(DeleteBehavior.Cascade);
        }
    }
}
