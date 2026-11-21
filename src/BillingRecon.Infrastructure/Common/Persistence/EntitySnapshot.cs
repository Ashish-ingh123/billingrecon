using System.Text.Encodings.Web;
using System.Text.Json;
using BillingRecon.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BillingRecon.Infrastructure.Common.Persistence;

/// <summary>
/// The image of an aggregate for its audit record (BR-701): a JSON object with one member per stored column of the
/// root, named like the property and holding the value as the column stores it (a typed id as its Guid, money as its
/// decimal, an enum as its name), and one array per collection of children, such as the lines of a bill. The "before"
/// image is built from the values the aggregate was read with, the "after" image from the values about to be saved,
/// so the two differ exactly where the save changes the database.
/// <para>
/// Left out: the row version, which the database assigns after the save, and the tenant, which the audit record
/// carries in a column of its own. A child leaves out the key of its parent as well. Children are in the order of
/// their ids, the same in both images; that is not their order on the document, which their own columns give. A
/// column whose configuration carries <see cref="NotAuditedAnnotation"/> is left out too: the password hash of a user
/// (ADR 0030), which an audit trail must never be able to hand out.
/// </para>
/// <para>
/// The image holds what the row holds, patient data included. It goes to the audit table, in the same database and
/// behind the same tenant filter as the row, and never to a log (ADR 0012).
/// </para>
/// </summary>
public static class EntitySnapshot
{
    /// <summary>The annotation of a property that no audit image holds; its value is <see langword="true"/>.</summary>
    public const string NotAuditedAnnotation = "BillingRecon:NotAudited";

    // Readable in the database: a name in Devanagari stays itself instead of becoming \u escapes. The text is stored
    // and returned as JSON, never written into HTML.
    private static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The id of the aggregate behind <paramref name="root"/>, as the Guid its key column stores.</summary>
    /// <exception cref="InvalidOperationException">The entity has no key of exactly one Guid column.</exception>
    public static Guid IdOf(EntityEntry root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var key = root.Metadata.FindPrimaryKey();
        if (key is { Properties.Count: 1 } && Stored(key.Properties[0], root.Property(key.Properties[0]).CurrentValue) is Guid id)
        {
            return id;
        }

        throw new InvalidOperationException(
            $"{root.Metadata.DisplayName()} raised a domain event, but its key is not one Guid column, so its audit record cannot name it.");
    }

    /// <summary>The aggregate as it was read, or null when the save about to happen creates it.</summary>
    public static string? Before(DbContext context, EntityEntry root)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(root);

        return root.State == EntityState.Added ? null : JsonSerializer.Serialize(Image(context, root, original: true), Options);
    }

    /// <summary>The aggregate as the save about to happen writes it.</summary>
    public static string After(DbContext context, EntityEntry root)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(root);

        return JsonSerializer.Serialize(Image(context, root, original: false), Options);
    }

    private static Dictionary<string, object?> Image(DbContext context, EntityEntry root, bool original)
    {
        var image = Columns(root, original, without: []);

        foreach (var navigation in root.Metadata.GetNavigations().Where(navigation => navigation.IsCollection && !navigation.IsOnDependent))
        {
            var foreignKey = navigation.ForeignKey;
            var rootKey = foreignKey.PrincipalKey.Properties.Select(property => root.Property(property).CurrentValue).ToArray();

            image[navigation.Name] = context.ChangeTracker.Entries()
                .Where(child => child.Metadata == navigation.TargetEntityType)
                .Where(child => child.State is not EntityState.Detached && child.State != (original ? EntityState.Added : EntityState.Deleted))
                .Where(child => foreignKey.Properties.Select(property => Value(child, property, original)).SequenceEqual(rootKey))
                .OrderBy(KeyText, StringComparer.Ordinal)
                .Select(child => Columns(child, original, without: foreignKey.Properties))
                .ToList();
        }

        return image;
    }

    private static Dictionary<string, object?> Columns(EntityEntry entry, bool original, IReadOnlyList<IProperty> without)
    {
        var columns = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var property in entry.Metadata.GetProperties())
        {
            var isRowVersion = property.IsConcurrencyToken && property.ClrType == typeof(byte[]);
            var isNotAudited = property.FindAnnotation(NotAuditedAnnotation)?.Value is true;
            if (isRowVersion || isNotAudited || property.Name == nameof(ITenantScoped.TenantId) || without.Contains(property))
            {
                continue;
            }

            columns[property.Name] = Stored(property, Value(entry, property, original));
        }

        return columns;
    }

    private static object? Value(EntityEntry entry, IProperty property, bool original)
        => original ? entry.Property(property).OriginalValue : entry.Property(property).CurrentValue;

    // The value as its column stores it: what the converter of the property makes of it, or the value itself.
    private static object? Stored(IProperty property, object? value)
        => value is null ? null : property.GetTypeMapping().Converter?.ConvertToProvider(value) ?? value;

    private static string KeyText(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();

        return key is null ? string.Empty : string.Join("/", key.Properties.Select(property => Stored(property, entry.Property(property).CurrentValue)));
    }
}
