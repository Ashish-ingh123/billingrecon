using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Reconciliation;

/// <summary>An unmatched statement line as the matcher reads it: its id, date, amount, reference and description.</summary>
public sealed record LineToMatch(StatementLineId Id, DateOnly Date, Money Amount, string? Reference, string Description);
