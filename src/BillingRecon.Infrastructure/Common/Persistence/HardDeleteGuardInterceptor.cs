using BillingRecon.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BillingRecon.Infrastructure.Common.Persistence;

/// <summary>
/// Nothing is ever deleted (CLAUDE.md section 4, rule 10; BR-005): a save that would delete a row is refused, unless
/// the entity carries <see cref="IHardDeletable"/>, which only a line of a draft bill does. Documents are voided or
/// reversed, reference data is deactivated. A refused delete is a bug in the code that asked for it, so it throws.
/// <para>
/// This is the guard at the Entity Framework layer. The database grants that will refuse a <c>DELETE</c> from the
/// application login come with the logins (docs/database.md, section 7); statements that go around the change
/// tracker (<c>ExecuteDelete</c>) are banned outright (ADR 0015).
/// </para>
/// </summary>
public sealed class HardDeleteGuardInterceptor : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>Refuses the save when it would delete a row of an entity that is not <see cref="IHardDeletable"/>.</summary>
    /// <exception cref="InvalidOperationException">A row that must never be deleted is marked for deletion.</exception>
    public static void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Deleted && entry.Entity is not IHardDeletable)
            {
                throw new InvalidOperationException(
                    $"Cannot delete {Describe(entry)}: nothing is deleted in this system (CLAUDE.md section 4, rule 10). "
                    + "Void or reverse a document, deactivate reference data.");
            }
        }
    }

    // Entity type and key only: ids are not personal data, anything else on the entity might be.
    private static string Describe(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        var values = key is null ? "no key" : string.Join(", ", key.Properties.Select(property => $"{property.Name} {entry.Property(property.Name).CurrentValue}"));

        return $"{entry.Metadata.DisplayName()} ({values})";
    }
}
