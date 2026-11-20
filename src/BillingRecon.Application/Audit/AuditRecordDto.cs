using System.Text.Json;

namespace BillingRecon.Application.Audit;

/// <summary>
/// One entry of an audit trail, as the API returns it. <see cref="Before"/> and <see cref="After"/> are the record
/// that changed, as JSON objects whose members are the stored columns; a bill carries its lines.
/// </summary>
public sealed record AuditRecordDto
{
    /// <summary>The number of the entry; entries of one trail are in the order of their numbers.</summary>
    public required long Id { get; init; }

    /// <summary>When the change happened, in UTC.</summary>
    public required DateTimeOffset OccurredAtUtc { get; init; }

    /// <summary>Who made the change; null for system work.</summary>
    public required Guid? UserId { get; init; }

    /// <summary>The correlation id of the request that made the change (BR-703).</summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>The kind of record that changed.</summary>
    public required string EntityType { get; init; }

    /// <summary>The id of the record that changed.</summary>
    public required Guid EntityId { get; init; }

    /// <summary>The number of the document, when it has one.</summary>
    public required string? DocumentNumber { get; init; }

    /// <summary>What was done.</summary>
    public required string Action { get; init; }

    /// <summary>The record before the change; null when the change created it.</summary>
    public required JsonElement? Before { get; init; }

    /// <summary>The record after the change.</summary>
    public required JsonElement After { get; init; }

    /// <summary>The reason a person gave, when the change needed one.</summary>
    public required string? Reason { get; init; }
}
