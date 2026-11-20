using System.Text.Json;
using BillingRecon.Domain.Audit;

namespace BillingRecon.Application.Audit;

/// <summary>Explicit mapping from the audit record to its DTO.</summary>
public static class AuditMapping
{
    /// <summary>The record as the API returns it.</summary>
    public static AuditRecordDto ToDto(this AuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new AuditRecordDto
        {
            Id = record.Id,
            OccurredAtUtc = record.OccurredAtUtc,
            UserId = record.UserId?.Value,
            CorrelationId = record.CorrelationId,
            EntityType = record.EntityType,
            EntityId = record.EntityId,
            DocumentNumber = record.DocumentNumber,
            Action = record.Action,
            Before = record.BeforeJson is null ? null : Parse(record.BeforeJson),
            After = Parse(record.AfterJson),
            Reason = record.Reason,
        };
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }
}
