namespace BillingRecon.Application.Reconciliation;

/// <summary>A row of a statement that was refused (BR-1003): the line of the file it started on and the reason in words.</summary>
public sealed record StatementRefusal(int RowNumber, string Reason);
