namespace BillingRecon.Domain.Payers;

/// <summary>
/// Whether a payer can take on new business (BR-105). The member names are stored as text (docs/database.md, section
/// 3.2), so renaming one is a data migration.
/// </summary>
public enum PayerStatus
{
    /// <summary>The payer can receive new bills, payments and refunds.</summary>
    Active,

    /// <summary>No new bill, payment or refund for the payer; existing balances remain and are still allocated and reported.</summary>
    Inactive,
}
