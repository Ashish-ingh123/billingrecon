using BillingRecon.Domain.Reconciliation;

namespace BillingRecon.Application.Reconciliation;

/// <summary>A row of a statement that was read: the line of the file it started on, what it says, and the row as the file held it.</summary>
public sealed record ReadStatementRow(int RowNumber, StatementLineValues Values, string RawText);
