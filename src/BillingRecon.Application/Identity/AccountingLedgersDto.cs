using BillingRecon.Domain.Identity;

namespace BillingRecon.Application.Identity;

/// <summary>
/// The hospital's names for its accountant's ledgers, as the API answers them (ADR 0062). A name that is null is not
/// set. <paramref name="Version"/> is the row version to send back as <c>If-Match</c>; eight zero bytes in base64 for
/// a hospital that never saved any.
/// </summary>
/// <param name="Receivable">What payers owe on bills.</param>
/// <param name="UnappliedCash">Money received and not yet applied to a bill.</param>
/// <param name="CreditAvailable">Credit of credit notes not yet applied or refunded.</param>
/// <param name="Revenue">The taxable value billed.</param>
/// <param name="TaxOutput">The tax billed.</param>
/// <param name="Adjustments">Discounts, write-offs and rounding.</param>
/// <param name="OpeningBalances">The other side of the opening balances.</param>
/// <param name="Cash">Money received in cash.</param>
/// <param name="Card">Money received by card.</param>
/// <param name="Upi">Money received by UPI.</param>
/// <param name="BankTransfer">Money received by bank transfer.</param>
/// <param name="Cheque">Money received by cheque.</param>
/// <param name="Version">The row version.</param>
public sealed record AccountingLedgersDto(
    string? Receivable,
    string? UnappliedCash,
    string? CreditAvailable,
    string? Revenue,
    string? TaxOutput,
    string? Adjustments,
    string? OpeningBalances,
    string? Cash,
    string? Card,
    string? Upi,
    string? BankTransfer,
    string? Cheque,
    string Version)
{
    /// <summary>The names as the value of the Domain.</summary>
    public AccountingLedgerNames ToNames() => new()
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
}
