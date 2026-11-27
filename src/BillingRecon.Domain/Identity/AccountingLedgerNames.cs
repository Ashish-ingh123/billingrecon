namespace BillingRecon.Domain.Identity;

/// <summary>
/// The names a hospital gives its accountant's ledgers, one for each <see cref="AccountingLedger"/> (ADR 0062). A
/// name that is null is not set yet; the export refuses a period that needs it. Two may share a name (card and UPI
/// into one bank ledger).
/// </summary>
public sealed record AccountingLedgerNames
{
    /// <summary>Longest name of a ledger.</summary>
    public const int NameMaxLength = 100;

    /// <summary>The names with none set.</summary>
    public static readonly AccountingLedgerNames None = new();

    /// <summary>The name for <see cref="AccountingLedger.Receivable"/>.</summary>
    public string? Receivable { get; init; }

    /// <summary>The name for <see cref="AccountingLedger.UnappliedCash"/>.</summary>
    public string? UnappliedCash { get; init; }

    /// <summary>The name for <see cref="AccountingLedger.CreditAvailable"/>.</summary>
    public string? CreditAvailable { get; init; }

    /// <summary>The name for <see cref="AccountingLedger.Revenue"/>.</summary>
    public string? Revenue { get; init; }

    /// <summary>The name for <see cref="AccountingLedger.TaxOutput"/>.</summary>
    public string? TaxOutput { get; init; }

    /// <summary>The name for <see cref="AccountingLedger.Adjustments"/>.</summary>
    public string? Adjustments { get; init; }

    /// <summary>The name for <see cref="AccountingLedger.OpeningBalances"/>.</summary>
    public string? OpeningBalances { get; init; }

    /// <summary>The name for <see cref="AccountingLedger.Cash"/>.</summary>
    public string? Cash { get; init; }

    /// <summary>The name for <see cref="AccountingLedger.Card"/>.</summary>
    public string? Card { get; init; }

    /// <summary>The name for <see cref="AccountingLedger.Upi"/>.</summary>
    public string? Upi { get; init; }

    /// <summary>The name for <see cref="AccountingLedger.BankTransfer"/>.</summary>
    public string? BankTransfer { get; init; }

    /// <summary>The name for <see cref="AccountingLedger.Cheque"/>.</summary>
    public string? Cheque { get; init; }

    /// <summary>The name given to <paramref name="ledger"/>, or null when none is set.</summary>
    public string? Of(AccountingLedger ledger) => ledger switch
    {
        AccountingLedger.Receivable => Receivable,
        AccountingLedger.UnappliedCash => UnappliedCash,
        AccountingLedger.CreditAvailable => CreditAvailable,
        AccountingLedger.Revenue => Revenue,
        AccountingLedger.TaxOutput => TaxOutput,
        AccountingLedger.Adjustments => Adjustments,
        AccountingLedger.OpeningBalances => OpeningBalances,
        AccountingLedger.Cash => Cash,
        AccountingLedger.Card => Card,
        AccountingLedger.Upi => Upi,
        AccountingLedger.BankTransfer => BankTransfer,
        AccountingLedger.Cheque => Cheque,
        _ => throw new ArgumentOutOfRangeException(nameof(ledger), ledger, "The ledger is not one the enum defines."),
    };

    /// <summary>
    /// True when <paramref name="name"/> can be the name of a ledger: empty, or at most
    /// <see cref="NameMaxLength"/> characters after trimming with no control character in it (a line break would
    /// break the accountant's file).
    /// </summary>
    public static bool IsName(string? name)
    {
        var trimmed = name?.Trim();

        return string.IsNullOrEmpty(trimmed) || (trimmed.Length <= NameMaxLength && !trimmed.Any(char.IsControl));
    }

    /// <summary>The same names trimmed, an empty one as null. A name <see cref="IsName"/> refuses is a bug in the caller.</summary>
    public AccountingLedgerNames Normalized() => new()
    {
        Receivable = Clean(Receivable),
        UnappliedCash = Clean(UnappliedCash),
        CreditAvailable = Clean(CreditAvailable),
        Revenue = Clean(Revenue),
        TaxOutput = Clean(TaxOutput),
        Adjustments = Clean(Adjustments),
        OpeningBalances = Clean(OpeningBalances),
        Cash = Clean(Cash),
        Card = Clean(Card),
        Upi = Clean(Upi),
        BankTransfer = Clean(BankTransfer),
        Cheque = Clean(Cheque),
    };

    private static string? Clean(string? name)
    {
        if (!IsName(name))
        {
            throw new ArgumentException($"A ledger's name has at most {NameMaxLength} characters and no control character; the validator of the use case should have refused it.", nameof(name));
        }

        var trimmed = name?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
