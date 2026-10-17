using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BillingRecon.Infrastructure.Common.Persistence;

/// <summary>
/// Fills the audit columns of the entities a save is about to write (ADR 0015):
/// <list type="bullet">
/// <item>added: <see cref="ICreationAudited"/> and <see cref="IModificationAudited"/> both get the current instant
/// and the current user;</item>
/// <item>modified: <see cref="IModificationAudited"/> gets the current instant and user. The creation columns are
/// never part of an UPDATE; the model says so (<see cref="BillingReconDbContext"/>), not this class.</item>
/// </list>
/// The user is null for system work (seeding, migrations, background jobs). The clock is read once per save, so all
/// rows of one save carry the same instant. Set-based statements (<c>ExecuteUpdate</c>, <c>ExecuteDelete</c>, raw
/// SQL) do not pass through here, which is why the first two are banned (<c>src/BannedSymbols.txt</c>). These
/// columns say who touched the row first and last; the history is in the audit records (BR-701).
/// <para>
/// <see cref="BillingReconDbContext"/> attaches this interceptor to itself. <see cref="Apply"/> is public so that the
/// unit of work can stamp before it dispatches domain events, whose handlers read these columns.
/// </para>
/// </summary>
public sealed class AuditStampInterceptor(ICurrentUser currentUser, IClock clock) : SaveChangesInterceptor
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

    /// <summary>Stamps every audited entity that <paramref name="context"/> is about to insert or update.</summary>
    public void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        // Read once, and only when something is stamped.
        (DateTimeOffset Now, UserId? User)? stamp = null;

        // A list, because the changes below must not disturb the enumeration of the change tracker.
        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            var created = entry.State == EntityState.Added && entry.Entity is ICreationAudited;
            var modified = entry.Entity is IModificationAudited;
            if (!created && !modified)
            {
                continue;
            }

            stamp ??= (clock.UtcNow, currentUser.UserId);

            if (created)
            {
                entry.Property(nameof(ICreationAudited.CreatedAtUtc)).CurrentValue = stamp.Value.Now;
                entry.Property(nameof(ICreationAudited.CreatedByUserId)).CurrentValue = stamp.Value.User;
            }

            if (modified)
            {
                entry.Property(nameof(IModificationAudited.UpdatedAtUtc)).CurrentValue = stamp.Value.Now;
                entry.Property(nameof(IModificationAudited.UpdatedByUserId)).CurrentValue = stamp.Value.User;
            }
        }
    }
}
