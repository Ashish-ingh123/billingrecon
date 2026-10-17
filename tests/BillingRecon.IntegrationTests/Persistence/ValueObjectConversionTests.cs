using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillingRecon.IntegrationTests.Persistence;

/// <summary>
/// The value objects of ADR 0004 are stored as decimals with the scale of docs/database.md section 1, through
/// converters the context registers by convention: no configuration names a converter or a precision. No database is
/// needed; the round trip on SQL Server is in the Database collection.
/// </summary>
public sealed class ValueObjectConversionTests
{
    [Theory]
    [InlineData(nameof(Priced.Amount), "decimal(18,2)", false, typeof(MoneyConverter))]
    [InlineData(nameof(Priced.Optional), "decimal(18,2)", true, typeof(MoneyConverter))]
    [InlineData(nameof(Priced.Price), "decimal(19,4)", false, typeof(UnitPriceConverter))]
    [InlineData(nameof(Priced.Count), "decimal(9,3)", false, typeof(QuantityConverter))]
    [InlineData(nameof(Priced.Rate), "decimal(5,2)", false, typeof(PercentConverter))]
    public void A_value_object_column_gets_its_scale_and_its_converter_by_convention(string property, string columnType, bool nullable, Type converter)
    {
        using var context = TestContexts.CreateProbe<ValueObjectProbe>();

        var column = context.Model.FindEntityType(typeof(Priced))!.FindProperty(property)!;

        column.GetColumnType().ShouldBe(columnType);
        column.IsNullable.ShouldBe(nullable);
        column.GetTypeMapping().Converter.ShouldNotBeNull().GetType().ShouldBe(converter);
    }

    [Fact]
    public void The_converters_round_trip_through_the_decimal()
    {
        var money = new MoneyConverter();
        money.ConvertToProvider(Money.Of(12.34m)).ShouldBe(12.34m);
        money.ConvertFromProvider(12.34m).ShouldBe(Money.Of(12.34m));

        var price = new UnitPriceConverter();
        price.ConvertToProvider(UnitPrice.Of(333.3333m)).ShouldBe(333.3333m);
        price.ConvertFromProvider(333.3333m).ShouldBe(UnitPrice.Of(333.3333m));

        var quantity = new QuantityConverter();
        quantity.ConvertToProvider(Quantity.Of(2.5m)).ShouldBe(2.5m);
        quantity.ConvertFromProvider(2.5m).ShouldBe(Quantity.Of(2.5m));

        var percent = new PercentConverter();
        percent.ConvertToProvider(Percent.Of(18m)).ShouldBe(18m);
        percent.ConvertFromProvider(18m).ShouldBe(Percent.Of(18m));
    }

    [Fact]
    public void A_stored_value_the_value_object_forbids_is_a_bug_that_throws_when_read()
    {
        // The CHECK constraints keep such a value out of the table; the converter does not quietly accept one either.
        Should.Throw<ArgumentOutOfRangeException>(() => new QuantityConverter().ConvertFromProvider(0m));
        Should.Throw<ArgumentOutOfRangeException>(() => new PercentConverter().ConvertFromProvider(101m));
        Should.Throw<ArgumentException>(() => new MoneyConverter().ConvertFromProvider(0.005m));
    }

    [Fact]
    public void The_tenant_approval_threshold_is_money_stored_as_an_optional_decimal_18_2()
    {
        using var context = TestContexts.CreateProduction();

        var threshold = context.Model.FindEntityType(typeof(Tenant))!.FindProperty(nameof(Tenant.PostingApprovalThreshold))!;

        threshold.ClrType.ShouldBe(typeof(Money?));
        threshold.GetColumnType().ShouldBe("decimal(18,2)");
        threshold.IsNullable.ShouldBeTrue();
        threshold.GetTypeMapping().Converter.ShouldNotBeNull().ShouldBeOfType<MoneyConverter>();
    }

    [Fact]
    public void A_money_property_in_a_query_predicate_becomes_a_decimal_parameter()
    {
        // No tenant in scope: the Tenants row filter stays out of the SQL and the predicate is the only WHERE.
        using var context = TestContexts.CreateProduction();
        var threshold = Money.Of(5000.50m);

        var sql = context.Set<Tenant>().Where(tenant => tenant.PostingApprovalThreshold == threshold).ToQueryString();
        var unset = context.Set<Tenant>().Where(tenant => tenant.PostingApprovalThreshold == null).ToQueryString();

        sql.ShouldMatch(@"WHERE \[t\]\.\[PostingApprovalThreshold\] = @\w+");
        sql.ShouldContain("decimal(18,2) = 5000.5");
        unset.ShouldContain("WHERE [t].[PostingApprovalThreshold] IS NULL");
    }

    public sealed class Priced
    {
        public int Id { get; set; }

        public Money Amount { get; set; }

        public Money? Optional { get; set; }

        public UnitPrice Price { get; set; }

        public Quantity Count { get; set; }

        public Percent Rate { get; set; }
    }

    public sealed class ValueObjectProbe : IModelProbe
    {
        public void Configure(ModelBuilder modelBuilder) => modelBuilder.Entity<Priced>();
    }
}
