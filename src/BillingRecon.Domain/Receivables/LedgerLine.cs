using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Receivables;

/// <summary>
/// One line of a posting before it is written: an account, an amount on one side, and for a subject account the
/// document it is about (BR-403). Built with <see cref="Dr"/> or <see cref="Cr"/>; <see cref="LedgerPosting"/> checks
/// the lines together.
/// </summary>
/// <param name="Account">The account.</param>
/// <param name="Debit">The debit amount; zero on a credit line.</param>
/// <param name="Credit">The credit amount; zero on a debit line.</param>
/// <param name="Subject">The bill, payment or credit note of a subject account; null on a control account.</param>
public sealed record LedgerLine(LedgerAccount Account, Money Debit, Money Credit, LedgerSubject? Subject)
{
    /// <summary>A debit of <paramref name="amount"/> to <paramref name="account"/>.</summary>
    public static LedgerLine Dr(LedgerAccount account, Money amount, LedgerSubject? subject = null) => new(account, amount, Money.Zero, subject);

    /// <summary>A credit of <paramref name="amount"/> to <paramref name="account"/>.</summary>
    public static LedgerLine Cr(LedgerAccount account, Money amount, LedgerSubject? subject = null) => new(account, Money.Zero, amount, subject);
}
