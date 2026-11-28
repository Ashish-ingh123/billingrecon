namespace BillingRecon.Domain.Reconciliation;

/// <summary>
/// What a settlement channel is (BR-1001). The member names are stored as text (docs/database.md §5.16), so renaming
/// one is a data migration.
/// </summary>
public enum SettlementChannelKind
{
    /// <summary>One of the hospital's bank accounts: its statement is imported through a column mapping.</summary>
    Bank,

    /// <summary>
    /// One of the hospital's payment-gateway accounts: its settlement report is imported (Prompt 13.5), and it settles
    /// into a bank channel.
    /// </summary>
    Gateway,
}
