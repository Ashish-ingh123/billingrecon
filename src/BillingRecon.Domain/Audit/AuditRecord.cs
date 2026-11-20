using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Audit;

/// <summary>
/// One entry of the audit trail (BR-701): who changed what, when, in which request, and what the record looked like
/// before and after. Written in the transaction of the change it describes, and never changed or deleted afterwards
/// (BR-702, <see cref="IAppendOnly"/>). It is not an aggregate with behaviour: it is created complete and only read.
/// <para>
/// The id is a number the database assigns, in the order the records are written, so the trail of one entity reads
/// in the order things happened. It is the one key in the model that is not a typed id (docs/database.md, section 3.8).
/// </para>
/// </summary>
public sealed class AuditRecord : ITenantScoped, IAppendOnly
{
    /// <summary>Longest allowed <see cref="EntityType"/>.</summary>
    public const int EntityTypeMaxLength = 50;

    /// <summary>Longest allowed <see cref="Action"/>.</summary>
    public const int ActionMaxLength = 40;

    /// <summary>Longest allowed <see cref="DocumentNumber"/>.</summary>
    public const int DocumentNumberMaxLength = 30;

    /// <summary>Longest allowed <see cref="Reason"/>.</summary>
    public const int ReasonMaxLength = 500;

    // Entity Framework creates a record with this constructor and then sets the properties.
    private AuditRecord()
    {
        EntityType = string.Empty;
        Action = string.Empty;
        AfterJson = string.Empty;
    }

    /// <summary>The number of the record, assigned by the database when it is written. Zero until then.</summary>
    public long Id { get; private set; }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>When the change happened, in UTC: the instant of the commit that made it.</summary>
    public DateTimeOffset OccurredAtUtc { get; private set; }

    /// <summary>Who made the change; null for system work, which no user does.</summary>
    public UserId? UserId { get; private set; }

    /// <summary>The correlation id of the request that made the change (BR-703).</summary>
    public Guid CorrelationId { get; private set; }

    /// <summary>The kind of record that changed, such as <c>Patient</c> or <c>Bill</c>.</summary>
    public string EntityType { get; private set; }

    /// <summary>The id of the record that changed.</summary>
    public Guid EntityId { get; private set; }

    /// <summary>The number of the document, for a document that has one; for people who look a change up by it.</summary>
    public string? DocumentNumber { get; private set; }

    /// <summary>What was done, such as <c>Registered</c>, <c>Updated</c> or <c>Voided</c>.</summary>
    public string Action { get; private set; }

    /// <summary>The record before the change, as JSON; null when the change created it.</summary>
    public string? BeforeJson { get; private set; }

    /// <summary>The record after the change, as JSON.</summary>
    public string AfterJson { get; private set; }

    /// <summary>The reason a person gave, for a change that needs one (a void, a reversal, a rejection).</summary>
    public string? Reason { get; private set; }

    /// <summary>Creates a record. The tenant is the current one, set when the record is saved.</summary>
    /// <exception cref="ArgumentException">A value is missing, empty or longer than its column.</exception>
    public static AuditRecord Create(
        DateTimeOffset occurredAtUtc,
        UserId? userId,
        Guid correlationId,
        string entityType,
        Guid entityId,
        string action,
        string? beforeJson,
        string afterJson,
        string? documentNumber = null,
        string? reason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(afterJson);

        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("An audit record needs the correlation id of its request (BR-703).", nameof(correlationId));
        }

        if (entityId == Guid.Empty)
        {
            throw new ArgumentException("An audit record names the record that changed; the empty id is not one.", nameof(entityId));
        }

        return new AuditRecord
        {
            OccurredAtUtc = occurredAtUtc,
            UserId = userId,
            CorrelationId = correlationId,
            EntityType = Field.Required(entityType, EntityTypeMaxLength),
            EntityId = entityId,
            Action = Field.Required(action, ActionMaxLength),
            BeforeJson = beforeJson,
            AfterJson = afterJson,
            DocumentNumber = Field.Optional(documentNumber, DocumentNumberMaxLength),
            Reason = Field.Optional(reason, ReasonMaxLength),
        };
    }
}
