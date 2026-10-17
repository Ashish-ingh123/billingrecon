using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BillingRecon.Infrastructure.Common.Persistence;

/// <summary>
/// The write side of tenant isolation (BR-002, ADR 0009, ADR 0015). The query filter only shapes what is read;
/// this interceptor looks at everything a save is about to write:
/// <list type="bullet">
/// <item>an added tenant-scoped entity gets the current tenant, and is refused if it already carries another one;</item>
/// <item>a modified or deleted one must belong to the current tenant (an empty tenant is a violation too), and its
/// tenant must not have been changed;</item>
/// <item>the tenant row itself (an entity whose id is a <see cref="TenantId"/>) may be updated or deleted by its
/// own tenant only. System work, which has no tenant, may write any tenant row; that is how tenants are onboarded.</item>
/// </list>
/// A violation throws <see cref="TenantIsolationException"/> before anything is sent to the database. Set-based
/// statements (<c>ExecuteUpdate</c>, <c>ExecuteDelete</c>, raw SQL) do not pass through here, which is why the first
/// two are banned (<c>src/BannedSymbols.txt</c>).
/// <para>
/// This class decides about tenants only. Whether a row may be updated or deleted at all (CLAUDE.md section 4,
/// rules 10 and 11) is not its concern.
/// </para>
/// <para>
/// <see cref="BillingReconDbContext"/> attaches this interceptor to itself. <see cref="Apply"/> is public so that the
/// unit of work can stamp the tenant before it dispatches domain events, whose handlers read it.
/// </para>
/// </summary>
public sealed class TenantWriteGuardInterceptor(ITenantContext tenantContext) : SaveChangesInterceptor
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

    /// <summary>Stamps and checks every tenant-scoped entity, and every tenant row, that <paramref name="context"/> is about to write.</summary>
    public void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        GuardTenantScopedEntities(context);
        GuardTenantRows(context);
    }

    private void GuardTenantScopedEntities(DbContext context)
    {
        // Read once, and only when a tenant-scoped entity is written: system work that touches other tables has no tenant.
        TenantId? currentTenant = null;

        // A list, because assigning a value below must not disturb the enumeration of the change tracker.
        foreach (var entry in context.ChangeTracker.Entries<ITenantScoped>().ToList())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            currentTenant ??= tenantContext.TenantId
                ?? throw new TenantIsolationException(
                    $"Cannot save {Describe(entry)}: the current scope has no tenant. System work may write only tables that are not tenant-scoped.");

            var tenant = entry.Property(nameof(ITenantScoped.TenantId));

            if (entry.State == EntityState.Added)
            {
                var carried = (TenantId)tenant.CurrentValue!;
                if (carried == default)
                {
                    tenant.CurrentValue = currentTenant.Value;
                }
                else if (carried != currentTenant.Value)
                {
                    throw new TenantIsolationException(
                        $"Cannot add {Describe(entry)}: it carries tenant {carried}, but the current tenant is {currentTenant.Value}.");
                }

                continue;
            }

            var original = (TenantId)tenant.OriginalValue!;
            var current = (TenantId)tenant.CurrentValue!;

            if (current != original)
            {
                throw new TenantIsolationException(
                    $"Cannot save {Describe(entry)}: its tenant was changed from {original} to {current}. A row never moves to another tenant.");
            }

            if (original != currentTenant.Value)
            {
                throw new TenantIsolationException(
                    $"Cannot {Verb(entry)} {Describe(entry)}: it belongs to tenant {original}, but the current tenant is {currentTenant.Value}.");
            }
        }
    }

    // The tenant row: its id is the tenant, so the id itself is what is checked. An added tenant is not a question of
    // isolation (onboarding is system work, and who may onboard is authorisation), so only updates and deletes are.
    private void GuardTenantRows(DbContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries<Entity<TenantId>>().ToList())
        {
            if (entry.State is not (EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            // Read only when a tenant row is written; system work has no tenant and may write any tenant row.
            if (tenantContext.TenantId is not { } currentTenant || entry.Entity.Id == currentTenant)
            {
                continue;
            }

            throw new TenantIsolationException(
                $"Cannot {Verb(entry)} {Describe(entry)}: it is tenant {entry.Entity.Id}, but the current tenant is {currentTenant}. A tenant may change only its own row.");
        }
    }

    private static string Verb(EntityEntry entry) => entry.State == EntityState.Deleted ? "delete" : "update";

    // Entity type and key only: ids are not personal data, anything else on the entity might be.
    private static string Describe(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        var values = key is null ? "no key" : string.Join(", ", key.Properties.Select(property => $"{property.Name} {entry.Property(property.Name).CurrentValue}"));

        return $"{entry.Metadata.DisplayName()} ({values})";
    }
}
