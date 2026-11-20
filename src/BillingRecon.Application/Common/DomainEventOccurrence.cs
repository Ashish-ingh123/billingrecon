using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Common;

/// <summary>
/// A domain event on its way to its subscribers, with what the unit of work knows about the aggregate that raised it
/// and the event itself does not carry: the kind and the id of the aggregate (an event raised by a factory method
/// cannot know the id), the instant of the commit, and the aggregate as it was read and as it is about to be saved.
/// </summary>
/// <param name="Event">The event, as the aggregate raised it.</param>
/// <param name="EntityType">The kind of aggregate, by the name of its class: <c>Patient</c>, <c>Bill</c>.</param>
/// <param name="EntityId">The id of the aggregate.</param>
/// <param name="OccurredAtUtc">The instant of the commit: the one its audit columns carry too (BR-003).</param>
/// <param name="BeforeJson">The aggregate as it was read, as JSON; null when this commit creates it.</param>
/// <param name="AfterJson">The aggregate as this commit saves it, as JSON.</param>
public sealed record DomainEventOccurrence(
    IDomainEvent Event,
    string EntityType,
    Guid EntityId,
    DateTimeOffset OccurredAtUtc,
    string? BeforeJson,
    string AfterJson);
