namespace BillingRecon.Domain.Common;

/// <summary>
/// What the unit of work needs of an aggregate root, whatever its id type: the events its methods raised, and a way
/// to forget them once they are dispatched. <see cref="AggregateRoot{TId}"/> implements it; nothing else should.
/// </summary>
public interface IHasDomainEvents
{
    /// <summary>The events raised since the last <see cref="ClearDomainEvents"/>, in the order they were raised.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    /// <summary>Forgets the collected events.</summary>
    public void ClearDomainEvents();
}
