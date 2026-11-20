namespace BillingRecon.Application.Common;

/// <summary>
/// Something that reacts to every domain event, in-process and inside the transaction of the use case that raised it
/// (docs/domain-model.md, section 8). The unit of work calls each subscriber once per event, after it has stamped the
/// tenant and the audit columns and before it saves; what a subscriber adds to the unit of work is saved, or rolled
/// back, with the change itself. A subscriber that throws fails the use case.
/// <para>
/// A subscriber handles <see cref="Domain.Common.IDomainEvent"/> generically and never names an event of a business
/// module (ADR 0003): the audit trail is the one subscriber. An effect in another module is a call through that
/// module's port in the use case, where it can be seen, not a subscription.
/// </para>
/// </summary>
public interface IDomainEventSubscriber
{
    /// <summary>Handles one event.</summary>
    public Task HandleAsync(DomainEventOccurrence occurrence, CancellationToken ct);
}
