namespace BillingRecon.Domain.Common;

/// <summary>
/// Base type of an aggregate root: the entity through which the whole aggregate is loaded, changed and saved. It
/// collects the domain events its methods raise until the unit of work dispatches them.
/// </summary>
/// <typeparam name="TId">The typed id of this kind of aggregate.</typeparam>
public abstract class AggregateRoot<TId> : Entity<TId>, IHasDomainEvents
    where TId : struct, ITypedId
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>The events raised since the last <see cref="ClearDomainEvents"/>, in the order they were raised.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>Forgets the collected events. The unit of work calls it once it has dispatched them.</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>
    /// Records that something happened to this aggregate. An event must not copy <see cref="Entity{TId}.Id"/> when it
    /// is created inside a factory method: the id is still empty until the aggregate is added to the unit of work.
    /// </summary>
    protected void AddDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        _domainEvents.Add(domainEvent);
    }
}
