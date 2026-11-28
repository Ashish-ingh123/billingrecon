using BillingRecon.Domain.Reconciliation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>
/// A line of a bank's statement as the API returns it (BR-1004, BR-1005); amounts in rupees with two decimals. Since 13.2:
/// the reason of its active match, or the reason and score of its best open proposal and how many it has (ADR 0066).
/// </summary>
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
    string Version,
    string? MatchReason = null,
    string? ProposalReason = null,
    int? ProposalScore = null,
    int ProposalCount = 0);
