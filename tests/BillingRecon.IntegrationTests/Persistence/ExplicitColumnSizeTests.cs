using BillingRecon.Infrastructure.Common.Persistence;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BillingRecon.IntegrationTests.Persistence;

/// <summary>
/// No decimal and no text column gets a size by default (CLAUDE.md section 4, rule 1; docs/database.md, section 1):
/// a model that leaves one unconfigured cannot be built. Each test builds the production model plus one probe entity.
/// </summary>
public sealed class ExplicitColumnSizeTests
{
    [Fact]
    public void A_decimal_without_precision_and_scale_fails_the_model()
    {
        var exception = BuildModelOf<UnsizedDecimal>().ShouldBeOfType<InvalidOperationException>();

        exception.Message.ShouldContain("Priced.Amount: decimal without an explicit precision and scale");
    }

    [Fact]
    public void A_decimal_with_a_precision_but_no_scale_fails_the_model()
    {
        var exception = BuildModelOf<DecimalWithoutScale>().ShouldBeOfType<InvalidOperationException>();

        exception.Message.ShouldContain("Priced.Amount: decimal without an explicit precision and scale");
    }

    [Fact]
    public void A_value_object_stored_as_a_decimal_needs_precision_and_scale_too()
    {
        var exception = BuildModelOf<UnsizedConvertedDecimal>().ShouldBeOfType<InvalidOperationException>();

        exception.Message.ShouldContain("Weighed.Weight: decimal without an explicit precision and scale");
    }

    [Fact]
    public void A_string_without_a_maximum_length_fails_the_model()
    {
        var exception = BuildModelOf<UnsizedString>().ShouldBeOfType<InvalidOperationException>();

        exception.Message.ShouldContain("Titled.Title: text without a maximum length");
    }

    [Fact]
    public void An_enum_is_stored_as_text_and_needs_a_maximum_length_as_well()
    {
        var exception = BuildModelOf<EnumWithoutLength>().ShouldBeOfType<InvalidOperationException>();

        exception.Message.ShouldContain("Kinded.Kind: text without a maximum length");
    }

    [Fact]
    public void Every_unsized_column_is_reported_in_one_message()
    {
        var exception = BuildModelOf<SeveralUnsized>().ShouldBeOfType<InvalidOperationException>();

        exception.Message.ShouldContain("Priced.Amount");
        exception.Message.ShouldContain("Titled.Title");
    }

    [Fact]
    public void A_string_marked_as_unbounded_is_accepted_and_becomes_a_max_column()
    {
        using var context = Context<UnboundedString>();

        var title = context.Model.FindEntityType(typeof(Titled))!.FindProperty(nameof(Titled.Title))!;

        title.GetColumnType().ShouldBe("nvarchar(max)");
    }

    [Fact]
    public void A_string_that_is_both_unbounded_and_limited_fails_the_model()
    {
        var exception = BuildModelOf<UnboundedAndLimited>().ShouldBeOfType<InvalidOperationException>();

        exception.Message.ShouldContain("Titled.Title: text marked as unbounded that also has a maximum length");
    }

    [Fact]
    public void The_opt_out_does_not_exist_for_decimals()
    {
        var exception = BuildModelOf<UnboundedDecimal>().ShouldBeOfType<InvalidOperationException>();

        exception.Message.ShouldContain("Priced.Amount: decimal without an explicit precision and scale");
    }

    [Fact]
    public void Fully_sized_columns_are_accepted()
    {
        using var context = Context<Sized>();

        var model = context.Model;

        model.FindEntityType(typeof(Priced))!.FindProperty(nameof(Priced.Amount))!.GetColumnType().ShouldBe("decimal(19,4)");
        model.FindEntityType(typeof(Titled))!.FindProperty(nameof(Titled.Title))!.GetColumnType().ShouldBe("nvarchar(80)");
        model.FindEntityType(typeof(Kinded))!.FindProperty(nameof(Kinded.Kind))!.GetColumnType().ShouldBe("varchar(10)");
        model.FindEntityType(typeof(Weighed))!.FindProperty(nameof(Weighed.Weight))!.GetColumnType().ShouldBe("decimal(9,3)");
    }

    [Fact]
    public void The_production_model_and_the_test_model_pass_the_check()
    {
        using var production = TestContexts.CreateProduction();
        using var test = TestContexts.Create(FixedIdentity.Random());

        production.Model.GetEntityTypes().ShouldNotBeEmpty();
        test.Model.GetEntityTypes().ShouldNotBeEmpty();
    }

    [Fact]
    public void An_enum_column_that_is_too_short_for_the_longest_name_fails_the_model()
    {
        var exception = BuildModelOf<EnumColumnTooShort>().ShouldBeOfType<InvalidOperationException>();

        exception.Message.ShouldContain("Kinded.Kind: the column holds 4 characters, but the name 'Plain' has 5");
    }

    [Fact]
    public void A_collection_stored_as_json_text_needs_a_maximum_length_as_well()
    {
        var exception = BuildModelOf<TagsWithoutLength>().ShouldBeOfType<InvalidOperationException>();

        exception.Message.ShouldContain("Tagged.Tags: text without a maximum length");
    }

    [Fact]
    public void A_collection_with_a_maximum_length_or_marked_as_unbounded_is_accepted()
    {
        using var sized = Context<TagsWithLength>();
        using var unbounded = Context<TagsUnbounded>();

        sized.Model.FindEntityType(typeof(Tagged))!.FindProperty(nameof(Tagged.Tags))!.GetColumnType().ShouldBe("nvarchar(400)");
        unbounded.Model.FindEntityType(typeof(Tagged))!.FindProperty(nameof(Tagged.Tags))!.GetColumnType().ShouldBe("nvarchar(max)");
    }

    private static ModelProbeContext<TProbe> Context<TProbe>()
        where TProbe : IModelProbe, new()
        => TestContexts.CreateProbe<TProbe>();

    // Building the model is what startup does (DatabaseStartup reads context.Model); here without a host.
    private static Exception BuildModelOf<TProbe>()
        where TProbe : IModelProbe, new()
    {
        using var context = Context<TProbe>();

        return Should.Throw<Exception>(() => _ = context.Model);
    }

    public sealed class Priced
    {
        public int Id { get; set; }

        public decimal Amount { get; set; }
    }

    public sealed class Titled
    {
        public int Id { get; set; }

        public string? Title { get; set; }
    }

    public sealed class Kinded
    {
        public int Id { get; set; }

        public WidgetKind Kind { get; set; }
    }

    public sealed class Weighed
    {
        public int Id { get; set; }

        public Kilograms Weight { get; set; }
    }

    public sealed class Tagged
    {
        public int Id { get; set; }

        public List<string> Tags { get; set; } = [];
    }

    public readonly record struct Kilograms(decimal Value);

    public sealed class UnsizedDecimal : IModelProbe
    {
        public void Configure(ModelBuilder modelBuilder) => modelBuilder.Entity<Priced>();
    }

    public sealed class DecimalWithoutScale : IModelProbe
    {
        public void Configure(ModelBuilder modelBuilder) => modelBuilder.Entity<Priced>().Property(entity => entity.Amount).HasPrecision(18);
    }

    public sealed class UnboundedDecimal : IModelProbe
    {
        public void Configure(ModelBuilder modelBuilder) => modelBuilder.Entity<Priced>().Property(entity => entity.Amount).IsUnbounded();
    }

    public sealed class UnsizedConvertedDecimal : IModelProbe
    {
        public void Configure(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Weighed>().Property(entity => entity.Weight).HasConversion(KilogramsConverter);
    }

    public sealed class UnsizedString : IModelProbe
    {
        public void Configure(ModelBuilder modelBuilder) => modelBuilder.Entity<Titled>();
    }

    public sealed class EnumWithoutLength : IModelProbe
    {
        public void Configure(ModelBuilder modelBuilder) => modelBuilder.Entity<Kinded>();
    }

    public sealed class EnumColumnTooShort : IModelProbe
    {
        public void Configure(ModelBuilder modelBuilder) => modelBuilder.Entity<Kinded>().Property(entity => entity.Kind).HasMaxLength(4);
    }

    public sealed class TagsWithoutLength : IModelProbe
    {
        public void Configure(ModelBuilder modelBuilder) => modelBuilder.Entity<Tagged>();
    }

    public sealed class TagsWithLength : IModelProbe
    {
        public void Configure(ModelBuilder modelBuilder) => modelBuilder.Entity<Tagged>().PrimitiveCollection(entity => entity.Tags).HasMaxLength(400);
    }

    public sealed class TagsUnbounded : IModelProbe
    {
        public void Configure(ModelBuilder modelBuilder) => modelBuilder.Entity<Tagged>().PrimitiveCollection(entity => entity.Tags).IsUnbounded();
    }

    public sealed class SeveralUnsized : IModelProbe
    {
        public void Configure(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Priced>();
            modelBuilder.Entity<Titled>();
        }
    }

    public sealed class UnboundedString : IModelProbe
    {
        public void Configure(ModelBuilder modelBuilder) => modelBuilder.Entity<Titled>().Property(entity => entity.Title).IsUnbounded();
    }

    public sealed class UnboundedAndLimited : IModelProbe
    {
        public void Configure(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Titled>().Property(entity => entity.Title).HasMaxLength(80).IsUnbounded();
    }

    public sealed class Sized : IModelProbe
    {
        public void Configure(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Priced>().Property(entity => entity.Amount).HasPrecision(19, 4);
            modelBuilder.Entity<Titled>().Property(entity => entity.Title).HasMaxLength(80);
            modelBuilder.Entity<Kinded>().Property(entity => entity.Kind).HasMaxLength(10);
            modelBuilder.Entity<Weighed>().Property(entity => entity.Weight).HasConversion(KilogramsConverter).HasPrecision(9, 3);
        }
    }

    private static readonly ValueConverter<Kilograms, decimal> KilogramsConverter = new(weight => weight.Value, value => new Kilograms(value));
}
