using BillingRecon.Domain.Receivables;

namespace BillingRecon.Application.Receivables;

/// <summary>An opening batch as the API answers it (ADR 0053): what it brings in and where it stands, never its files.</summary>
public sealed record OpeningBatchDto(
    Guid Id,
    DateOnly CutOverDate,
    OpeningBatchStatus Status,
    int Receivables,
    decimal ReceivableTotal,
    int Advances,
    decimal AdvanceTotal,
    DateTimeOffset SubmittedAtUtc,
    Guid? SubmittedByUserId,
    DateTimeOffset? DecidedAtUtc,
    string Version);
