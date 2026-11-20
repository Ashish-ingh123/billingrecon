using BillingRecon.Domain.Audit;

namespace BillingRecon.Application.Audit;

/// <summary>
/// The action an audit record shows for a domain event: the name of the event without the name of the aggregate that
/// raised it. <c>PatientRegistered</c> on a <c>Patient</c> is <c>Registered</c>, <c>BillLinesReplaced</c> on a
/// <c>Bill</c> is <c>LinesReplaced</c>. An event that is not named after its aggregate keeps its whole name.
/// </summary>
public static class AuditAction
{
    /// <summary>The action for an event of type <paramref name="eventType"/> raised by an aggregate of <paramref name="entityType"/>.</summary>
    /// <exception cref="InvalidOperationException">The action is longer than its column; rename the event.</exception>
    public static string For(Type eventType, string entityType)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        ArgumentException.ThrowIfNullOrEmpty(entityType);

        var name = eventType.Name;
        var action = name.Length > entityType.Length && name.StartsWith(entityType, StringComparison.Ordinal)
            ? name[entityType.Length..]
            : name;

        return action.Length <= AuditRecord.ActionMaxLength
            ? action
            : throw new InvalidOperationException(
                $"The audit action of {name} has {action.Length} characters; the column holds {AuditRecord.ActionMaxLength}. Give the event a shorter name.");
    }
}
