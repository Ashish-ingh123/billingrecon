using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Identity;

/// <summary>
/// A hospital's names for its accountant's ledgers (ADR 0062): at most one row for a hospital, made the first time an
/// administrator saves the names. Until then no name is set, and the accountant's export refuses.
/// </summary>
public sealed class AccountingLedgers : AggregateRoot<AccountingLedgersId>, ITenantScoped, ICreationAudited, IModificationAudited, IHasRowVersion
{
    // Entity Framework creates the row with this constructor and then sets the properties.
    private AccountingLedgers()
    {
        RowVersion = [];
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>The name for <see cref="AccountingLedger.Receivable"/>.</summary>
    public string? Receivable { get; private set; }

    /// <summary>The name for <see cref="AccountingLedger.UnappliedCash"/>.</summary>
    public string? UnappliedCash { get; private set; }

    /// <summary>The name for <see cref="AccountingLedger.CreditAvailable"/>.</summary>
    public string? CreditAvailable { get; private set; }

    /// <summary>The name for <see cref="AccountingLedger.Revenue"/>.</summary>
    public string? Revenue { get; private set; }

    /// <summary>The name for <see cref="AccountingLedger.TaxOutput"/>.</summary>
    public string? TaxOutput { get; private set; }

    /// <summary>The name for <see cref="AccountingLedger.Adjustments"/>.</summary>
    public string? Adjustments { get; private set; }

    /// <summary>The name for <see cref="AccountingLedger.OpeningBalances"/>.</summary>
    public string? OpeningBalances { get; private set; }

    /// <summary>The name for <see cref="AccountingLedger.Cash"/>.</summary>
    public string? Cash { get; private set; }

    /// <summary>The name for <see cref="AccountingLedger.Card"/>.</summary>
    public string? Card { get; private set; }

    /// <summary>The name for <see cref="AccountingLedger.Upi"/>.</summary>
    public string? Upi { get; private set; }

    /// <summary>The name for <see cref="AccountingLedger.BankTransfer"/>.</summary>
    public string? BankTransfer { get; private set; }

    /// <summary>The name for <see cref="AccountingLedger.Cheque"/>.</summary>
    public string? Cheque { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? CreatedByUserId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? UpdatedByUserId { get; private set; }

    /// <inheritdoc />
    public byte[] RowVersion { get; private set; }

    /// <summary>The names as one value.</summary>
    public AccountingLedgerNames Names => new()
    {
        Receivable = Receivable,
        UnappliedCash = UnappliedCash,
        CreditAvailable = CreditAvailable,
        Revenue = Revenue,
        TaxOutput = TaxOutput,
        Adjustments = Adjustments,
        OpeningBalances = OpeningBalances,
        Cash = Cash,
        Card = Card,
        Upi = Upi,
        BankTransfer = BankTransfer,
        Cheque = Cheque,
    };

    /// <summary>The first names a hospital saves.</summary>
    public static AccountingLedgers Create(AccountingLedgerNames names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var ledgers = new AccountingLedgers();
        ledgers.Take(names.Normalized());

        // The id is still empty here: it is assigned when the row is added to the unit of work.
        ledgers.AddDomainEvent(new AccountingLedgersChanged());

        return ledgers;
    }

    /// <summary>Replaces the names. False, and no event, when nothing changes.</summary>
    public bool Change(AccountingLedgerNames names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var next = names.Normalized();
        if (next == Names)
        {
            return false;
        }

        Take(next);
        AddDomainEvent(new AccountingLedgersChanged());

        return true;
    }

    private void Take(AccountingLedgerNames names)
    {
        Receivable = names.Receivable;
        UnappliedCash = names.UnappliedCash;
        CreditAvailable = names.CreditAvailable;
        Revenue = names.Revenue;
        TaxOutput = names.TaxOutput;
        Adjustments = names.Adjustments;
        OpeningBalances = names.OpeningBalances;
        Cash = names.Cash;
        Card = names.Card;
        Upi = names.Upi;
        BankTransfer = names.BankTransfer;
        Cheque = names.Cheque;
    }
}
