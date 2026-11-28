using BillingRecon.Domain.Reconciliation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>A line of a bank's statement as the API returns it (BR-1004, BR-1005); amounts in rupees with two decimals.</summary>
public sealed record StatementLineDto(
    Guid Id,
    Guid ChannelId,
    Guid ImportId,
    int RowNumber,
    DateOnly TransactionDate,
    DateOnly? ValueDate,
    StatementDirection Direction,
    decimal Amount,
    string? Reference,
    string Description,
    decimal? Balance,
    string RawText,
    StatementLineStatus Status,
    string Version);
