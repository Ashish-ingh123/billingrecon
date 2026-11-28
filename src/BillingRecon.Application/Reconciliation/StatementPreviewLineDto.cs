using BillingRecon.Domain.Reconciliation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>A line the dry run read, as the import would store it, and whether the channel already holds it.</summary>
public sealed record StatementPreviewLineDto(
    int RowNumber,
    DateOnly TransactionDate,
    DateOnly? ValueDate,
    StatementDirection Direction,
    decimal Amount,
    string? Reference,
    string Description,
    decimal? Balance,
    bool AlreadyHeld);
