using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillingRecon.Infrastructure.Common.Persistence;

/// <summary>The explicit opt-out from the rule that every text column has a maximum length.</summary>
public static class UnboundedColumnExtensions
{
    private const string AnnotationName = "BillingRecon:Unbounded";

    /// <summary>
    /// Declares that a text column is unbounded on purpose (<c>nvarchar(max)</c> or <c>varchar(max)</c>): JSON
    /// documents and stored response bodies (docs/database.md, section 1). Without this call, a string property
    /// that has no maximum length fails the model.
    /// </summary>
    public static PropertyBuilder<TProperty> IsUnbounded<TProperty>(this PropertyBuilder<TProperty> builder)
    {
        builder.HasAnnotation(AnnotationName, true);

        return builder;
    }

    /// <summary>
    /// The same declaration for a collection of simple values, which is stored as JSON text in one column.
    /// </summary>
    public static PrimitiveCollectionBuilder<TProperty> IsUnbounded<TProperty>(this PrimitiveCollectionBuilder<TProperty> builder)
    {
        builder.HasAnnotation(AnnotationName, true);

        return builder;
    }

    internal static bool IsMarkedUnbounded(IReadOnlyProperty property)
        => property.FindAnnotation(AnnotationName)?.Value is true;
}
