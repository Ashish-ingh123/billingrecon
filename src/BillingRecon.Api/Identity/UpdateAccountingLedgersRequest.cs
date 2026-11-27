using System.Text.Json.Serialization;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Api.Identity;

/// <summary>
/// The body of <c>PUT /api/v1/settings/accounting-ledgers</c> (ADR 0062): the name of each ledger of the accountant's
/// books. A member left out, null or empty leaves that ledger without a name.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateAccountingLedgersRequest
{
    /// <summary>What payers owe on bills.</summary>
    public string? Receivable { get; init; }

    /// <summary>Money received and not yet applied to a bill.</summary>
    public string? UnappliedCash { get; init; }

    /// <summary>Credit of credit notes not yet applied or refunded.</summary>
    public string? CreditAvailable { get; init; }

    /// <summary>The taxable value billed.</summary>
    public string? Revenue { get; init; }

    /// <summary>The tax billed.</summary>
    public string? TaxOutput { get; init; }

    /// <summary>Discounts, write-offs and rounding.</summary>
    public string? Adjustments { get; init; }

    /// <summary>The other side of the opening balances.</summary>
    public string? OpeningBalances { get; init; }

    /// <summary>Money received in cash.</summary>
    public string? Cash { get; init; }

    /// <summary>Money received by card.</summary>
    public string? Card { get; init; }

    /// <summary>Money received by UPI.</summary>
    public string? Upi { get; init; }

    /// <summary>Money received by bank transfer.</summary>
    public string? BankTransfer { get; init; }

    /// <summary>Money received by cheque.</summary>
    public string? Cheque { get; init; }

    /// <summary>The command of the use case.</summary>
    public UpdateAccountingLedgersCommand ToCommand(string expectedVersion) => new()
    {
        Names = new AccountingLedgerNames
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
        },
        ExpectedVersion = expectedVersion,
    };
}
