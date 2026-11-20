using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace BillingRecon.Infrastructure.Common.Persistence;

/// <summary>
/// The unit of work of a request: one <c>SaveChanges</c> of the request's context, which SQL Server runs as one
/// transaction (BR-007). When the use case took a document number first, that transaction is already open and holds
/// the lock of the series; the save joins it and the commit here ends it (ADR 0008). When the request carries an
/// idempotency key, the transaction belongs to the idempotency store, which commits it with the stored answer
/// (ADR 0006): the unit of work then saves and leaves the ending to the store. Two ways of losing a race to another request are expected and come back as failed results:
/// a row that changed since it was read, <c>concurrency_conflict</c> (BR-804; a forged write to another tenant's row
/// ends here too, because the tenant is part of the WHERE clause, ADR 0015), and a row a unique index refused,
/// <c>duplicate_key</c>. The SQL Server message of the second names the duplicate key, which can be patient data, so
/// it goes nowhere. Every other failure throws.
/// <para>
/// An aggregate is versioned as a whole (ADR 0007): the row version is on its root, and its children have none. So
/// before the save, a root whose children changed while its own row did not is made part of the save: its
/// <c>UpdatedAtUtc</c> is written, the UPDATE checks the row version the root was read at, and the version advances.
/// Without that, two clerks replacing the lines of one bill at the same time would both succeed.
/// </para>
/// <para>
/// Before the save, the domain events the aggregates raised are dispatched, in-process, to every
/// <see cref="IDomainEventSubscriber"/> (ADR 0020): the tenant and the audit columns are stamped first, with one
/// clock reading that the save reuses, so a subscriber sees complete rows and its own rows carry the same instant.
/// What a subscriber adds, the audit record of the change, is saved by the same save and rolled back with it. An
/// aggregate's events are cleared as they are collected, so a commit dispatches each event once.
/// </para>
/// </summary>
public sealed class UnitOfWork(
    BillingReconDbContext context,
    RequestTransaction? requestTransaction = null,
    IEnumerable<IDomainEventSubscriber>? subscribers = null) : IUnitOfWork
{
    // SQL Server: 2601 is a duplicate in a unique index, 2627 in a unique constraint or primary key.
    private const int DuplicateInUniqueIndex = 2601;
    private const int DuplicateInUniqueConstraint = 2627;

    private readonly IDomainEventSubscriber[] _subscribers = [.. subscribers ?? []];

    /// <inheritdoc />
    public async Task<Result> CommitAsync(CancellationToken ct)
    {
        // Open only when the use case took a document number (INumberSeries opens it, ADR 0008): the save then joins
        // it, and the commit below makes the number and the document permanent together. Whatever goes wrong before
        // that rolls both back, so a failed posting leaves no gap in the series (BR-801).
        //
        // Not ours to end while the idempotency store holds it (ADR 0006): the save below then joins the transaction
        // and stays uncommitted, and the store commits it together with the stored answer, or rolls all of it back
        // when the use case reports a failure. A save that fails inside it undoes itself (Entity Framework sets a
        // savepoint), and the store undoes the rest.
        var transaction = requestTransaction is { IsHeld: true } ? null : context.Database.CurrentTransaction;

        try
        {
            TouchRootsOfChangedChildren();
            await DispatchDomainEventsAsync(ct);

            await context.SaveChangesAsync(ct);

            if (transaction is not null)
            {
                await transaction.CommitAsync(ct);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            await RollBackAsync(transaction);

            return CommonErrors.ConcurrencyConflict();
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException { Number: DuplicateInUniqueIndex or DuplicateInUniqueConstraint })
        {
            await RollBackAsync(transaction);

            return CommonErrors.DuplicateKey();
        }
        catch
        {
            await RollBackAsync(transaction);

            throw;
        }
        finally
        {
            context.EndCommit();

            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }

        return Result.Success();
    }

    // Not cancellable: once the use case has failed, the locks it holds are released whatever the caller does next.
    private static async Task RollBackAsync(IDbContextTransaction? transaction)
    {
        if (transaction is not null)
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
    }

    // Stamps first, so that a subscriber reads rows that have their tenant, their ids and their audit columns; then
    // takes every event off its aggregate, with the aggregate as it was read and as it will be saved; then hands each
    // to every subscriber. A subscriber that throws fails the commit, and the catch above rolls everything back.
    private async Task DispatchDomainEventsAsync(CancellationToken ct)
    {
        var now = context.BeginCommit();

        var occurrences = new List<DomainEventOccurrence>();
        foreach (var root in context.ChangeTracker.Entries<IHasDomainEvents>().ToList())
        {
            if (root.Entity.DomainEvents.Count == 0)
            {
                continue;
            }

            if (_subscribers.Length > 0)
            {
                var entityType = root.Metadata.ClrType.Name;
                var entityId = EntitySnapshot.IdOf(root);
                var before = EntitySnapshot.Before(context, root);
                var after = EntitySnapshot.After(context, root);

                occurrences.AddRange(root.Entity.DomainEvents.Select(raised => new DomainEventOccurrence(raised, entityType, entityId, now, before, after)));
            }

            root.Entity.ClearDomainEvents();
        }

        foreach (var occurrence in occurrences)
        {
            foreach (var subscriber in _subscribers)
            {
                await subscriber.HandleAsync(occurrence, ct);
            }
        }
    }

    // A child is an entity reached through a collection of a versioned entity (Bill.Lines). Its root is found among
    // the tracked entities by the child's foreign key; for a removed child that is the key it had when it was read.
    private void TouchRootsOfChangedChildren()
    {
        context.ChangeTracker.DetectChanges();

        foreach (var child in context.ChangeTracker.Entries().ToList())
        {
            if (child.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            foreach (var foreignKey in child.Metadata.GetForeignKeys())
            {
                var rootType = foreignKey.PrincipalEntityType.ClrType;
                if (foreignKey.PrincipalToDependent is not { IsCollection: true }
                    || !typeof(IHasRowVersion).IsAssignableFrom(rootType)
                    || !typeof(IModificationAudited).IsAssignableFrom(rootType))
                {
                    continue;
                }

                if (FindRoot(child, foreignKey) is { State: EntityState.Unchanged } root)
                {
                    // The audit stamp of the save sets the value; marking the column is what makes the row part of it.
                    root.Property(nameof(IModificationAudited.UpdatedAtUtc)).IsModified = true;
                }
            }
        }
    }

    private EntityEntry? FindRoot(EntityEntry child, IForeignKey foreignKey)
    {
        var keyValues = foreignKey.Properties
            .Select(property => child.State == EntityState.Deleted ? child.Property(property.Name).OriginalValue : child.Property(property.Name).CurrentValue)
            .ToArray();

        return context.ChangeTracker.Entries()
            .FirstOrDefault(candidate =>
                candidate.Metadata == foreignKey.PrincipalEntityType
                && foreignKey.PrincipalKey.Properties
                    .Select(property => candidate.Property(property.Name).CurrentValue)
                    .SequenceEqual(keyValues));
    }
}
