namespace BillingRecon.Domain.Payments;

/// <summary>
/// Where a payment is in its life (docs/domain-model.md, section 5.3). A payment is posted the moment it is recorded
/// (BR-302), so there is no draft. Members are added at the end; their names are stored as text, so renaming one is a
/// data migration.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Recorded: it has its number and its ledger entry.</summary>
    Posted = 0,

    /// <summary>
    /// A cashier asked for the payment to be reversed and a second person has not decided yet (BR-306). Nothing can be
    /// allocated from it meanwhile; an approval reverses it, a rejection makes it <see cref="Posted"/> again.
    /// </summary>
    PendingReversal = 1,

    /// <summary>
    /// Reversed (BR-306), for example a dishonoured cheque: every allocation is reversed, nothing is unapplied, and
    /// the payment keeps its number and its rows. It never changes again.
    /// </summary>
    Reversed = 2,
}
