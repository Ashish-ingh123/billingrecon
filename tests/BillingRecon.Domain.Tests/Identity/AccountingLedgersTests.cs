using BillingRecon.Domain.Identity;

namespace BillingRecon.Domain.Tests.Identity;

/// <summary>
/// A hospital's names for its accountant's ledgers (ADR 0062): trimmed, an empty one as none, at most 100 characters
/// on one line, two may be the same, and only a change is an event.
/// </summary>
public sealed class AccountingLedgersTests
{
    [Fact]
    public void ADR0062_the_first_names_are_kept_trimmed_and_an_empty_one_is_none()
    {
        var ledgers = AccountingLedgers.Create(new AccountingLedgerNames { Receivable = "  Patient Receivables ", Revenue = "   ", Card = "HDFC Current A/c", Upi = "HDFC Current A/c" });

        ledgers.Names.ShouldBe(new AccountingLedgerNames { Receivable = "Patient Receivables", Card = "HDFC Current A/c", Upi = "HDFC Current A/c" });
        ledgers.DomainEvents.ShouldHaveSingleItem().ShouldBe(new AccountingLedgersChanged());
    }

    [Fact]
    public void ADR0062_only_a_change_of_a_name_is_an_event()
    {
        var ledgers = AccountingLedgers.Create(new AccountingLedgerNames { Cash = "Cash" });
        ledgers.ClearDomainEvents();

        ledgers.Change(new AccountingLedgerNames { Cash = " Cash " }).ShouldBeFalse();
        ledgers.DomainEvents.ShouldBeEmpty();

        ledgers.Change(new AccountingLedgerNames { Cash = "Cash in Hand", TaxOutput = "GST Output" }).ShouldBeTrue();
        (ledgers.Cash, ledgers.TaxOutput).ShouldBe(("Cash in Hand", "GST Output"));
        ledgers.DomainEvents.ShouldHaveSingleItem();

        // A name left out is taken away.
        ledgers.Change(AccountingLedgerNames.None).ShouldBeTrue();
        ledgers.Names.ShouldBe(AccountingLedgerNames.None);
    }

    [Fact]
    public void ADR0062_every_ledger_has_its_name_and_no_other()
    {
        var names = new AccountingLedgerNames
        {
            Receivable = "0",
            UnappliedCash = "1",
            CreditAvailable = "2",
            Revenue = "3",
            TaxOutput = "4",
            Adjustments = "5",
            OpeningBalances = "6",
            Cash = "7",
            Card = "8",
            Upi = "9",
            BankTransfer = "10",
            Cheque = "11",
        };

        Enum.GetValues<AccountingLedger>().Select(names.Of).ShouldBe(Enum.GetValues<AccountingLedger>().Select(ledger => ((int)ledger).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Should.Throw<ArgumentOutOfRangeException>(() => names.Of((AccountingLedger)42));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("Discounts & Write-offs <2026>", true)]
    [InlineData("Two\nlines", false)]
    [InlineData("A tab\there", false)]
    public void ADR0062_a_name_is_on_one_line(string? name, bool accepted)
        => AccountingLedgerNames.IsName(name).ShouldBe(accepted);

    [Fact]
    public void ADR0062_a_name_has_at_most_100_characters_and_one_the_validator_let_through_is_a_bug_in_the_caller()
    {
        AccountingLedgerNames.IsName(new string('a', 100)).ShouldBeTrue();
        AccountingLedgerNames.IsName($"  {new string('a', 100)}  ").ShouldBeTrue();
        AccountingLedgerNames.IsName(new string('a', 101)).ShouldBeFalse();

        Should.Throw<ArgumentException>(() => AccountingLedgers.Create(new AccountingLedgerNames { Cash = new string('a', 101) }));
    }
}
