using BillingRecon.Application.Reconciliation;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Reconciliation;

namespace BillingRecon.Application.Tests.Reconciliation;

/// <summary>
/// Reading a bank's statement with a channel's mapping (BR-1002, BR-1003, BR-1005): the two layouts of the money, the date
/// formats, a header below a few lines about the account, every row refused with its number and reason, and the files
/// that cannot be read at all. And the plan of an import (BR-1004): identical lines, and lines held from a file before.
/// </summary>
public sealed class StatementReaderTests
{
    private static readonly DateOnly Today = new(2026, 11, 28);

    // The layout of a bank that writes a debit and a credit column, three lines about the account above its header.
    private static readonly ColumnMapping TwoColumns = new()
    {
        HeaderRow = 4,
        DateColumn = "Txn Date",
        DateFormat = "dd/MM/yyyy",
        ValueDateColumn = "Value Date",
        DescriptionColumn = "Narration",
        ReferenceColumn = "Chq./Ref.No.",
        AmountLayout = AmountLayout.TwoColumns,
        DebitColumn = "Withdrawal Amt.",
        CreditColumn = "Deposit Amt.",
        BalanceColumn = "Closing Balance",
        DecimalStyle = DecimalStyle.Point,
    };

    // The layout of a bank that writes one signed amount and a month name.
    private static readonly ColumnMapping OneColumn = new()
    {
        HeaderRow = 1,
        DateColumn = "Date",
        DateFormat = "dd-MMM-yy",
        DescriptionColumn = "Description",
        AmountLayout = AmountLayout.OneColumn,
        AmountColumn = "Amount",
        DecimalStyle = DecimalStyle.Point,
    };

    private const string TwoColumnFile =
        "Statement of account\r\n" +
        "Account: XXXXXXXX4521\r\n" +
        "Period: 01/11/2026 to 27/11/2026\r\n" +
        "Txn Date,Narration,Chq./Ref.No.,Value Date,Withdrawal Amt.,Deposit Amt.,Closing Balance\r\n" +
        "26/11/2026,\"NEFT CR-HDFC0001-RAVI KUMAR\",N3341,26/11/2026,,\"12,000.00\",\"1,52,300.50\"\r\n" +
        "27/11/2026,UPI/ASHA RAO/PAY,UPI88123,27/11/2026,0.00,500.00,152800.50\r\n" +
        "27/11/2026,BANK CHARGES,,27/11/2026,59.00,,152741.50\r\n";

    [Fact]
    public void BR1002_a_debit_and_credit_file_below_three_lines_about_the_account_is_read_line_by_line()
    {
        var reading = StatementReader.Read(TwoColumnFile, TwoColumns, Today);

        reading.UnreadableReason.ShouldBeNull();
        reading.RowsRead.ShouldBe(3);
        reading.Refusals.ShouldBeEmpty();
        reading.Lines.Select(line => (line.RowNumber, line.Values.Direction, line.Values.Amount, line.Values.Reference)).ShouldBe(
        [
            (5, StatementDirection.In, Money.Of(12000m), "N3341"),
            (6, StatementDirection.In, Money.Of(500m), "UPI88123"),
            (7, StatementDirection.Out, Money.Of(59m), null),
        ]);
        reading.Lines[0].Values.Balance.ShouldBe(Money.Of(152300.50m), "the Indian grouping of 1,52,300.50 is read");
        reading.Lines[0].Values.Description.ShouldBe("NEFT CR-HDFC0001-RAVI KUMAR");
        reading.Lines[0].Values.ValueDate.ShouldBe(new DateOnly(2026, 11, 26));
        reading.Lines[0].RawText.ShouldBe("26/11/2026,NEFT CR-HDFC0001-RAVI KUMAR,N3341,26/11/2026,,\"12,000.00\",\"1,52,300.50\"");
    }

    [Fact]
    public void BR1002_a_signed_amount_and_a_month_name_are_read_and_Cr_and_Dr_give_the_direction()
    {
        const string file =
            "Date,Description,Amount\n" +
            "25-Nov-26,IMPS IN 7781,2500.00\n" +
            "26-NOV-26,CHQ RETURN 004512,-1800.00\n" +
            "27-nov-26,RTGS IN,\"10,000.00 Cr\"\n" +
            "27-Nov-26,SWEEP OUT,750.00 Dr\n" +
            "27-Nov-26,REVERSAL,(25.00)\n";

        var reading = StatementReader.Read(file, OneColumn, Today);

        reading.Refusals.ShouldBeEmpty();
        reading.Lines.Select(line => (line.Values.TransactionDate.Day, line.Values.Direction, line.Values.Amount.Amount)).ShouldBe(
        [
            (25, StatementDirection.In, 2500m),
            (26, StatementDirection.Out, 1800m),
            (27, StatementDirection.In, 10000m),
            (27, StatementDirection.Out, 750m),
            (27, StatementDirection.Out, 25m),
        ]);
        reading.Lines.ShouldAllBe(line => line.Values.Reference == null, "this mapping has no reference column");
    }

    [Fact]
    public void BR1002_a_comma_before_the_paise_is_read_when_the_mapping_says_so()
    {
        var reading = StatementReader.Read("Date,Description,Amount\n27-Nov-26,IN,\"1.234,56\"\n", OneColumn with { DecimalStyle = DecimalStyle.Comma }, Today);

        reading.Lines.ShouldHaveSingleItem().Values.Amount.ShouldBe(Money.Of(1234.56m));
    }

    [Fact]
    public void BR1003_every_row_that_cannot_be_read_is_refused_with_its_line_and_its_reason_and_the_others_are_read()
    {
        const string file =
            "Date,Description,Amount\n" +
            "27-Nov-26,GOOD,100.00\n" +
            "27/11/2026,WRONG DATE FORM,100.00\n" +
            "29-Nov-26,TOMORROW,100.00\n" +
            "27-Nov-26,ZERO,0.00\n" +
            "27-Nov-26,THREE DECIMALS,10.005\n" +
            "27-Nov-26,NOT A NUMBER,abc\n" +
            "27-Nov-26,NO AMOUNT,\n" +
            "27-Nov-26,BOTH,-5 Cr\n" +
            ",,\n" +
            "Closing balance,,\n";

        var reading = StatementReader.Read(file, OneColumn, Today);

        reading.Lines.ShouldHaveSingleItem().Values.Description.ShouldBe("GOOD");
        reading.Refusals.Select(refusal => refusal.RowNumber).ShouldBe([3, 4, 5, 6, 7, 8, 9, 11], "a blank row is skipped, not refused");
        reading.Refusals[0].Reason.ShouldContain("dd-MMM-yy");
        reading.Refusals[1].Reason.ShouldContain("after today");
        reading.Refusals[2].Reason.ShouldContain("zero");
        reading.Refusals[3].Reason.ShouldContain("two decimals");
        reading.Refusals[4].Reason.ShouldContain("not a number");
        reading.Refusals[5].Reason.ShouldContain("missing");
        reading.Refusals[6].Reason.ShouldContain("both a minus sign and Cr or Dr");
        reading.RowsRead.ShouldBe(reading.Lines.Count + reading.Refusals.Count);
    }

    [Fact]
    public void BR1003_a_row_with_both_or_neither_a_debit_and_a_credit_is_refused()
    {
        var header = "a\nb\nc\nTxn Date,Narration,Chq./Ref.No.,Value Date,Withdrawal Amt.,Deposit Amt.,Closing Balance\n";
        var reading = StatementReader.Read(header + "27/11/2026,BOTH,,,10.00,20.00,\n27/11/2026,NEITHER,,,0.00,,\n", TwoColumns, Today);

        reading.Lines.ShouldBeEmpty();
        reading.Refusals.Select(refusal => refusal.Reason).ShouldBe(["the row has both a debit and a credit.", "the row has neither a debit nor a credit."]);
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("Date,Description\n27-Nov-26,X\n", "\"Amount\"")]
    [InlineData("Date,Description,Amount\n27-Nov-26,CAF�,10.00\n", "UTF-8")]
    public void BR1003_a_file_that_cannot_be_read_at_all_says_why(string file, string reason)
        => StatementReader.Read(file, OneColumn, Today).UnreadableReason.ShouldNotBeNull().ShouldContain(reason);

    [Fact]
    public void BR1003_a_file_with_fewer_lines_than_its_header_row_or_over_1_MB_cannot_be_read()
    {
        StatementReader.Read("only one line\n", TwoColumns, Today).UnreadableReason.ShouldNotBeNull().ShouldContain("no header on line 4");
        StatementReader.Read(new string('x', StatementReader.MaxFileLength + 1), OneColumn, Today).UnreadableReason.ShouldNotBeNull().ShouldContain("1 MB");
    }

    [Fact]
    public void BR1004_identical_lines_of_one_file_are_each_kept_and_lines_held_before_are_not_added_again()
    {
        var channel = new SettlementChannelId(Guid.NewGuid());
        const string file =
            "Date,Description,Amount\n" +
            "27-Nov-26,UPI CONSULT,500.00\n" +
            "27-Nov-26,UPI CONSULT,500.00\n" +
            "27-Nov-26,UPI CONSULT,500.00\n" +
            "27-Nov-26,UPI LAB,300.00\n";
        var lines = StatementReader.Read(file, OneColumn, Today).Lines;
        var consult = StatementLine.FingerprintOf(channel, lines[0].Values);

        // The first import holds nothing: all four are added, the three identical ones as occurrences 1, 2 and 3.
        var first = StatementPlan.Plan(channel, lines, new Dictionary<string, int>());
        first.Select(line => (line.Occurrence, line.AlreadyHeld)).ShouldBe([(1, false), (2, false), (3, false), (1, false)]);

        // A later file that overlaps it: the channel holds two of the consultations, so only the third is new.
        var later = StatementPlan.Plan(channel, lines, new Dictionary<string, int> { [consult] = 2 });
        later.Select(line => (line.Occurrence, line.AlreadyHeld)).ShouldBe([(1, true), (2, true), (3, false), (1, false)]);

        StatementPlan.Fingerprints(channel, lines).Count.ShouldBe(2);
        StatementPlan.FileHashOf(file).ShouldBe(StatementPlan.FileHashOf(file));
        StatementPlan.FileHashOf(file).ShouldNotBe(StatementPlan.FileHashOf(file + " "));
    }
}
