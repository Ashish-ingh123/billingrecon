using BillingRecon.Application.Common;
using BillingRecon.Domain.Audit;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Audit;

/// <summary>
/// Writes the audit trail (BR-701): one record for every domain event, in the transaction of the change. It knows no
/// event by name (ADR 0003): the kind and id of the record come from the aggregate that raised the event, the action
/// from the name of the event (<see cref="AuditAction"/>), the before and after images from the unit of work, the
/// reason and the document number from an event that carries one (<see cref="IHasReason"/>,
/// <see cref="IHasDocumentNumber"/>), and who and in which request from the scope.
/// So an aggregate is audited by raising an event; a state change that raises none leaves no trace, which is why the
/// tests count one record per use case.
/// </summary>
public sealed class AuditSubscriber(IAuditWriter writer, ICurrentUser currentUser, ICorrelationContext correlation) : IDomainEventSubscriber
{
    /// <inheritdoc />
    public Task HandleAsync(DomainEventOccurrence occurrence, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(occurrence);

        writer.Append(AuditRecord.Create(
            occurrence.OccurredAtUtc,
            currentUser.UserId,
            correlation.CorrelationId,
            occurrence.EntityType,
            occurrence.EntityId,
            AuditAction.For(occurrence.Event.GetType(), occurrence.EntityType),
            occurrence.BeforeJson,
            occurrence.AfterJson,
            documentNumber: (occurrence.Event as IHasDocumentNumber)?.DocumentNumber,
            reason: (occurrence.Event as IHasReason)?.Reason));

        return Task.CompletedTask;
    }
}
