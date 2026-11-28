using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Reconciliation;

/// <summary>What a row of a bank's statement says, once read with the channel's mapping (BR-1002, BR-1005).</summary>
/// <param name="TransactionDate">The date of the transaction.</param>
/// <param name="ValueDate">The value date, when the bank gave one.</param>
/// <param name="Direction">Money in or out.</param>
/// <param name="Amount">The amount, above zero, rounded to the paisa.</param>
/// <param name="Reference">The reference, when the file has a column for it.</param>
/// <param name="Description">The bank's description of the line.</param>
/// <param name="Balance">The running balance after the line, when the bank gave one.</param>
public sealed record StatementLineValues(
    DateOnly TransactionDate,
    DateOnly? ValueDate,
    StatementDirection Direction,
    Money Amount,
    string? Reference,
    string Description,
    Money? Balance);
