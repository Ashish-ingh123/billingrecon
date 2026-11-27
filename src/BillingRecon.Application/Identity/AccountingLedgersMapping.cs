using BillingRecon.Application.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>Maps the names to their DTO.</summary>
public static class AccountingLedgersMapping
{
    /// <summary>The version of names that were never saved: eight zero bytes.</summary>
    public static string NoVersion { get; } = Convert.ToBase64String(new byte[8]);

    /// <summary>The DTO of a saved row.</summary>
    public static AccountingLedgersDto ToDto(this AccountingLedgers ledgers)
    {
        ArgumentNullException.ThrowIfNull(ledgers);

        return ToDto(ledgers.Names, RowVersionText.Of(ledgers));
    }

    /// <summary>The DTO of a hospital that saved no name.</summary>
    public static AccountingLedgersDto NoneDto() => ToDto(AccountingLedgerNames.None, NoVersion);

    private static AccountingLedgersDto ToDto(AccountingLedgerNames names, string version) => new(
        Receivable: names.Receivable,
        UnappliedCash: names.UnappliedCash,
        CreditAvailable: names.CreditAvailable,
        Revenue: names.Revenue,
        TaxOutput: names.TaxOutput,
        Adjustments: names.Adjustments,
        OpeningBalances: names.OpeningBalances,
        Cash: names.Cash,
        Card: names.Card,
        Upi: names.Upi,
        BankTransfer: names.BankTransfer,
        Cheque: names.Cheque,
        Version: version);
}
