using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace BillingRecon.Infrastructure.Common.Persistence;

/// <summary>
/// Refuses a model in which the size of a decimal or text column was left to a default (CLAUDE.md section 4, rule 1;
/// docs/database.md, section 1). A decimal needs an explicit precision and scale. Text needs a maximum length,
/// unless it is marked as deliberately unbounded with <c>IsUnbounded()</c> (<see cref="UnboundedColumnExtensions"/>).
/// What counts is what is stored in the database, so an enum stored by name, a value object stored as a decimal and
/// a collection stored as JSON text are checked as well, and the column of an enum must be long enough for its
/// longest name. The check runs whenever the model is built: at startup, in tests and in the EF Core tools.
/// </summary>
internal sealed class ExplicitColumnSizeConvention : IModelFinalizingConvention
{
    /// <inheritdoc />
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        var problems = new List<string>();
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            Inspect(entityType, problems);
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "The model leaves the size of these columns to a default (CLAUDE.md section 4, rule 1; docs/database.md, section 1):"
                + Environment.NewLine
                + string.Join(Environment.NewLine, problems.Order(StringComparer.Ordinal).Select(problem => "  - " + problem)));
        }
    }

    private static void Inspect(IReadOnlyTypeBase type, List<string> problems)
    {
        foreach (var property in type.GetDeclaredProperties())
        {
            Inspect(property, problems);
        }

        foreach (var complexProperty in type.GetDeclaredComplexProperties())
        {
            Inspect(complexProperty.ComplexType, problems);
        }
    }

    private static void Inspect(IReadOnlyProperty property, List<string> problems)
    {
        var storedType = StoredType(property);
        var name = $"{property.DeclaringType.DisplayName()}.{property.Name}";

        if (storedType == typeof(decimal))
        {
            if (property.GetPrecision() is null || property.GetScale() is null)
            {
                problems.Add($"{name}: decimal without an explicit precision and scale. Configure it with HasPrecision(precision, scale).");
            }

            return;
        }

        // A collection of simple values (a list of strings, say) is stored as JSON text in one column.
        if (storedType != typeof(string) && !property.IsPrimitiveCollection)
        {
            return;
        }

        var maxLength = property.GetMaxLength();
        var isUnbounded = UnboundedColumnExtensions.IsMarkedUnbounded(property);

        if (maxLength is not null && isUnbounded)
        {
            problems.Add($"{name}: text marked as unbounded that also has a maximum length. Keep one of HasMaxLength(n) and IsUnbounded().");
        }
        else if (maxLength is null && !isUnbounded && !property.IsForeignKey())
        {
            // A foreign key column takes its size from the key it points at, which is checked in its own right.
            problems.Add($"{name}: text without a maximum length. Configure it with HasMaxLength(n), or with IsUnbounded() if an unbounded column is intended.");
        }
        else if (maxLength is { } length && LongestEnumName(property) is { } longest && longest.Length > length)
        {
            problems.Add($"{name}: the column holds {length} characters, but the name '{longest}' has {longest.Length}. Raise HasMaxLength.");
        }
    }

    // The CLR type that reaches the database: the provider type of a converter when there is one.
    private static Type StoredType(IReadOnlyProperty property)
    {
        var type = property.GetValueConverter()?.ProviderClrType ?? property.GetProviderClrType() ?? property.ClrType;

        return Nullable.GetUnderlyingType(type) ?? type;
    }

    // An enum is stored by name; the longest name is the longest value the column will ever have to hold.
    private static string? LongestEnumName(IReadOnlyProperty property)
    {
        var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

        return type.IsEnum ? Enum.GetNames(type).MaxBy(name => name.Length) : null;
    }
}
