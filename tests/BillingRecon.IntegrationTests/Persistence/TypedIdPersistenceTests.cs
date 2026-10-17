using System.Data.SqlTypes;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Persistence;

/// <summary>
/// Typed ids are stored as uniqueidentifier and generated in the application when an entity is added, both by
/// convention: no entity configuration mentions the converter or the generator. No database is needed.
/// </summary>
public sealed class TypedIdPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_added_entity_gets_a_non_empty_id_through_the_converter()
    {
        using var context = TestContexts.Create(FixedIdentity.Random());
        var widget = new Widget("first");
        widget.Id.ShouldBe(default);

        context.Add(widget);

        widget.Id.Value.ShouldNotBe(Guid.Empty);

        // The value is real, not a placeholder the database would replace, and what is sent to SQL Server is the Guid inside it.
        var id = context.Entry(widget).Property(entity => entity.Id);
        id.IsTemporary.ShouldBeFalse();
        var converter = id.Metadata.GetTypeMapping().Converter.ShouldBeOfType<TypedIdConverter<WidgetId>>();
        converter.ConvertToProvider(widget.Id).ShouldBe(widget.Id.Value);
        converter.ConvertFromProvider(widget.Id.Value).ShouldBe(widget.Id);
    }

    [Fact]
    public void Generated_ids_increase_in_the_order_sql_server_sorts_uniqueidentifier()
    {
        using var context = TestContexts.Create(FixedIdentity.Random());
        var widgets = Enumerable.Range(0, 200).Select(index => new Widget($"widget {index}")).ToArray();

        foreach (var widget in widgets)
        {
            context.Add(widget);
        }

        // SqlGuid compares the way SQL Server does (the last six bytes first), which is not the order of Guid.CompareTo.
        var ids = widgets.Select(widget => new SqlGuid(widget.Id.Value)).ToArray();
        ids.Distinct().Count().ShouldBe(ids.Length);
        for (var index = 1; index < ids.Length; index++)
        {
            ids[index].CompareTo(ids[index - 1]).ShouldBeGreaterThan(0, $"id {index} must sort after id {index - 1}");
        }
    }

    [Fact]
    public void An_entity_that_already_has_an_id_keeps_it()
    {
        using var context = TestContexts.Create(FixedIdentity.Random());
        var widget = new Widget("existing");
        var id = new WidgetId(Guid.NewGuid());
        context.Entry(widget).Property(entity => entity.Id).CurrentValue = id;

        context.Add(widget);

        widget.Id.ShouldBe(id);
    }

    [Fact]
    public void The_production_tenant_gets_its_id_the_same_way()
    {
        using var context = TestContexts.CreateProduction();
        var first = Tenant.Create("city-hospital", "City Hospital", Now).Value;
        var second = Tenant.Create("lake-clinic", "Lake Clinic", Now).Value;

        context.Add(first);
        context.Add(second);

        first.Id.Value.ShouldNotBe(Guid.Empty);
        new SqlGuid(second.Id.Value).CompareTo(new SqlGuid(first.Id.Value)).ShouldBeGreaterThan(0);
    }

    [Theory]
    [InlineData(typeof(Widget), nameof(Widget.Id), false)]
    [InlineData(typeof(Widget), nameof(Widget.TenantId), false)]
    [InlineData(typeof(Widget), nameof(Widget.CreatedByUserId), true)]
    [InlineData(typeof(Widget), nameof(Widget.UpdatedByUserId), true)]
    [InlineData(typeof(LogLine), nameof(LogLine.Id), false)]
    [InlineData(typeof(Setting), nameof(Setting.Id), false)]
    public void Every_typed_id_column_is_a_uniqueidentifier_including_optional_ones(Type entity, string property, bool nullable)
    {
        using var context = TestContexts.Create(FixedIdentity.Random());

        var column = context.Model.FindEntityType(entity)!.FindProperty(property)!;

        column.GetColumnType().ShouldBe("uniqueidentifier");
        column.IsNullable.ShouldBe(nullable);
        typeof(ITypedId).IsAssignableFrom(Nullable.GetUnderlyingType(column.ClrType) ?? column.ClrType).ShouldBeTrue();
    }

    [Fact]
    public void The_tenant_id_of_the_production_model_is_a_uniqueidentifier_key()
    {
        using var context = TestContexts.CreateProduction();

        var tenant = context.Model.FindEntityType(typeof(Tenant))!;

        var key = tenant.FindPrimaryKey()!.Properties.ShouldHaveSingleItem();
        key.Name.ShouldBe(nameof(Tenant.Id));
        key.ClrType.ShouldBe(typeof(TenantId));
        key.GetColumnType().ShouldBe("uniqueidentifier");
    }
}
