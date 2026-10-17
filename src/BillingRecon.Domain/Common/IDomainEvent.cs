namespace BillingRecon.Domain.Common;

/// <summary>
/// Something that happened to an aggregate. Events are immutable records in the module that raises them; the unit
/// of work dispatches them in-process before it saves (docs/domain-model.md, section 8).
/// </summary>
public interface IDomainEvent
{
}
