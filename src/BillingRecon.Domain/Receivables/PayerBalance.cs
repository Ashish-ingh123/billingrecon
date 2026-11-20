using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Receivables;

/// <summary>
/// The cached balances of one payer (BR-404; docs/database.md, section 3.7): what they owe on bills, what money is
/// held for them unapplied, and what credit they have left. It is a cache of the ledger, never a source: the row is
/// opened at zero when the payer is created, and afterwards changed only by the ledger, with one atomic statement
/// per posting, in the transaction of that posting. So it has no row version, and nothing in the application
/// assigns its amounts; the reconciliation query proves that it equals the ledger.
/// </summary>
public sealed class PayerBalance : ITenantScoped
{
    // Entity Framework creates a balance with this constructor and then sets the properties.
    private PayerBalance()
    {
    }

    /// <summary>The payer the balances belong to; also the key of the row.</summary>
    public PayerId PayerId { get; private set; }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>The sum of the balances due of the payer's posted bills.</summary>
    public Money Receivable { get; private set; }

    /// <summary>The sum of the unapplied amounts of the payer's payments.</summary>
    public Money UnappliedCash { get; private set; }

    /// <summary>The sum of the available amounts of the payer's credit notes.</summary>
    public Money CreditAvailable { get; private set; }

    /// <summary>What the payer owes net of what is held for them. Computed by the database.</summary>
    public Money NetOutstanding { get; private set; }

    /// <summary>When the row was opened or last changed by a posting.</summary>
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>The zero balances of a payer that has just been created.</summary>
    /// <exception cref="ArgumentException"><paramref name="payerId"/> is empty.</exception>
    public static PayerBalance Open(PayerId payerId, DateTimeOffset openedAtUtc)
    {
        if (payerId.Value == Guid.Empty)
        {
            throw new ArgumentException("Balances belong to a payer; the empty id is not one.", nameof(payerId));
        }

        return new PayerBalance { PayerId = payerId, UpdatedAtUtc = openedAtUtc };
    }
}
