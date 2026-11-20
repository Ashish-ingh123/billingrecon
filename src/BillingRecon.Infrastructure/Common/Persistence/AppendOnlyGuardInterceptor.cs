using BillingRecon.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BillingRecon.Infrastructure.Common.Persistence;

/// <summary>
/// Audit records are written once and never again (BR-702; CLAUDE.md section 4, rule 11), and so will ledger rows be
/// (rule 2): a save that would update or delete a row of an entity that carries <see cref="IAppendOnly"/> is refused.
/// A correction is a new row. A refused write is a bug in the code that asked for it, so it throws.
/// <para>
/// This is the guard at the Entity Framework layer. The database grants that will refuse an <c>UPDATE</c> or a
/// <c>DELETE</c> from the application login come with the logins (docs/database.md, section 7); statements that go
/// around the change tracker (<c>ExecuteUpdate</c>, <c>ExecuteDelete</c>, raw SQL) are banned outright (ADR 0015).
/// </para>
/// </summary>
public sealed class AppendOnlyGuardInterceptor : SaveChangesInterceptor
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

    /// <summary>Refuses the save when it would update or delete a row of an <see cref="IAppendOnly"/> entity.</summary>
    /// <exception cref="InvalidOperationException">A row that is written once is marked as modified or deleted.</exception>
    public static void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<IAppendOnly>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                var verb = entry.State == EntityState.Deleted ? "delete" : "update";

                throw new InvalidOperationException(
                    $"Cannot {verb} {Describe(entry)}: its table is append-only (CLAUDE.md section 4, rules 2 and 11). "
                    + "Write a new row that corrects the old one.");
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
