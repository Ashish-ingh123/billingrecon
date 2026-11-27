using System.Text;
using System.Xml.Linq;
using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using BillingRecon.Application.Reporting;
using BillingRecon.Domain.Common;
using NSubstitute;

namespace BillingRecon.Application.Tests.Reporting;

/// <summary>
/// The accountant's export with its ports replaced (ADR 0062): the control totals, the refusal that names every
/// ledger without a name, the refusal to write vouchers that do not add up to the ledger, and the file itself: the
/// envelope Tally documents, a debit as "Yes" with a negative amount, names and narrations escaped.
/// </summary>
public sealed class AccountingExportTests
{
    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 10, 31);
    private static readonly Guid Bill = Guid.Parse("0193a000-0000-7000-8000-000000000001");
    private static readonly Guid Receipt = Guid.Parse("0193a000-0000-7000-8000-000000000002");

    private static readonly ExportVoucher[] Vouchers =
    [
        new(Bill, new DateOnly(2026, 10, 17), ExportVoucherType.Journal, "BIL/26-27/000001", "Bill BIL/26-27/000001 posted. Payer: Rao & Sons <Pune>.",
            [new(ExportLedger.Receivable, 590.00m, 0m), new(ExportLedger.Revenue, 0m, 500.00m), new(ExportLedger.TaxOutput, 0m, 90.00m)]),
        new(Receipt, new DateOnly(2026, 10, 18), ExportVoucherType.Receipt, "RCT/26-27/000001", "Payment RCT/26-27/000001 recorded. Payer: Asha\u0007 Verma.",
            [new(ExportLedger.Upi, 590.00m, 0m), new(ExportLedger.UnappliedCash, 0m, 590.00m)]),
    ];

    private static readonly AccountingLedgersDto Named = new("Patient Receivables", "Advances", null, "Hospital Income", "GST Output", null, null, null, null, "HDFC A/c", null, null, "AAAAAAAAB9E=");

    private readonly IAccountingExportReports _reports = Substitute.For<IAccountingExportReports>();
    private readonly IQueryHandler<GetAccountingLedgersQuery, AccountingLedgersDto> _names = Substitute.For<IQueryHandler<GetAccountingLedgersQuery, AccountingLedgersDto>>();

    public AccountingExportTests()
    {
        _reports.VouchersAsync(From, To, Arg.Any<CancellationToken>()).Returns(new ExportVouchers(Vouchers, 1180.00m, 1180.00m));
        _names.HandleAsync(Arg.Any<GetAccountingLedgersQuery>(), Arg.Any<CancellationToken>()).Returns(Result<AccountingLedgersDto>.Success(Named));
    }

    private AccountingExportBuilder Builder => new(_reports, _names);

    [Fact]
    public async Task ADR0062_the_summary_counts_the_vouchers_and_says_what_each_ledger_receives()
    {
        var result = await new AccountingExportSummaryHandler(Builder).HandleAsync(new AccountingExportSummaryQuery { From = From, To = To }, CancellationToken.None);

        var summary = result.Value;
        (summary.Vouchers, summary.Receipts, summary.Payments, summary.Journals).ShouldBe((2, 1, 0, 1));
        (summary.ControlTotal, summary.LedgerTotal).ShouldBe((1180.00m, 1180.00m));
        summary.Ledgers.ShouldBe(
        [
            new(ExportLedger.Receivable, "Patient Receivables", 590.00m, 0m),
            new(ExportLedger.UnappliedCash, "Advances", 0m, 590.00m),
            new(ExportLedger.Revenue, "Hospital Income", 0m, 500.00m),
            new(ExportLedger.TaxOutput, "GST Output", 0m, 90.00m),
            new(ExportLedger.Upi, "HDFC A/c", 590.00m, 0m),
        ]);
        summary.Missing.ShouldBeEmpty();
    }

    [Fact]
    public async Task ADR0062_a_ledger_without_a_name_is_in_the_summary_and_refuses_the_file_by_name()
    {
        _names.HandleAsync(Arg.Any<GetAccountingLedgersQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<AccountingLedgersDto>.Success(Named with { TaxOutput = null, Upi = null }));

        var summary = await new AccountingExportSummaryHandler(Builder).HandleAsync(new AccountingExportSummaryQuery { From = From, To = To }, CancellationToken.None);
        var file = await new AccountingExportHandler(Builder).HandleAsync(new AccountingExportQuery { From = From, To = To }, CancellationToken.None);

        summary.Value.Missing.ShouldBe([ExportLedger.TaxOutput, ExportLedger.Upi]);
        file.Error.Code.ShouldBe(CommonErrors.AccountingLedgerNotMappedCode);
        file.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
        file.Error.Message.ShouldContain("TaxOutput, Upi");

        // A ledger the period does not use needs no name: CreditAvailable has none above, and nothing refused for it.
        file.Error.Message.ShouldNotContain("CreditAvailable");
    }

    [Fact]
    public async Task ADR0062_vouchers_that_do_not_add_up_to_the_ledger_or_do_not_balance_are_never_written()
    {
        var handler = new AccountingExportHandler(Builder);
        _reports.VouchersAsync(From, To, Arg.Any<CancellationToken>()).Returns(new ExportVouchers(Vouchers, 1180.01m, 1180.01m));
        await Should.ThrowAsync<InvalidOperationException>(() => handler.HandleAsync(new AccountingExportQuery { From = From, To = To }, CancellationToken.None));

        // The totals agree and one voucher does not balance, another being off the other way.
        ExportVoucher[] crooked =
        [
            Vouchers[0] with { Lines = [new(ExportLedger.Receivable, 590.00m, 0m), new(ExportLedger.Revenue, 0m, 589.00m)] },
            Vouchers[1] with { Lines = [new(ExportLedger.Upi, 590.00m, 0m), new(ExportLedger.UnappliedCash, 0m, 591.00m)] },
        ];
        _reports.VouchersAsync(From, To, Arg.Any<CancellationToken>()).Returns(new ExportVouchers(crooked, 1180.00m, 1180.00m));
        await Should.ThrowAsync<InvalidOperationException>(() => handler.HandleAsync(new AccountingExportQuery { From = From, To = To }, CancellationToken.None));
    }

    [Theory]
    [InlineData(null, "2026-10-31", "from")]
    [InlineData("2026-10-01", null, "to")]
    [InlineData("2026-10-31", "2026-10-01", "to")]
    [InlineData("2026-10-01", "2027-01-01", "to")]
    public async Task ADR0062_a_period_is_two_dates_in_order_and_at_most_92_days_and_nothing_is_read_for_another(string? from, string? to, string field)
    {
        var result = await new AccountingExportHandler(Builder).HandleAsync(
            new AccountingExportQuery { From = from is null ? null : DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture), To = to is null ? null : DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture) },
            CancellationToken.None);

        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.FieldErrors.Keys.ShouldContain(field);
        await _reports.DidNotReceiveWithAnyArgs().VouchersAsync(default, default, default);
    }

    [Fact]
    public async Task ADR0062_the_file_is_the_envelope_tally_documents_with_a_debit_as_yes_and_negative()
    {
        var export = (await new AccountingExportHandler(Builder).HandleAsync(new AccountingExportQuery { From = From, To = To }, CancellationToken.None)).Value;
        using var stream = new MemoryStream();

        await TallyVoucherWriter.WriteAsync(stream, export, CancellationToken.None);

        var bytes = stream.ToArray();
        bytes[..3].ShouldNotBe(Encoding.UTF8.GetPreamble(), "no byte order mark");
        var text = Encoding.UTF8.GetString(bytes);
        text.ShouldStartWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<ENVELOPE>");

        // A tag to a line: Tally's own advice for a file it imports.
        text.Split("\r\n").ShouldAllBe(line => line.Length < 200);
        text.ShouldContain("<NARRATION>Bill BIL/26-27/000001 posted. Payer: Rao &amp; Sons &lt;Pune&gt;.</NARRATION>");
        text.ShouldNotContain("CDATA");

        // A character XML cannot carry at all became a space, so the file is one a parser reads.
        var envelope = XDocument.Parse(text).Root.ShouldNotBeNull();
        envelope.Element("HEADER")!.Element("TALLYREQUEST")!.Value.ShouldBe("Import Data");
        envelope.Element("BODY")!.Element("IMPORTDATA")!.Element("REQUESTDESC")!.Element("REPORTNAME")!.Value.ShouldBe("Vouchers");
        var vouchers = envelope.Descendants("VOUCHER").ToList();
        vouchers.Count.ShouldBe(2);
        vouchers[1].Element("NARRATION")!.Value.ShouldBe("Payment RCT/26-27/000001 recorded. Payer: Asha  Verma.");

        var bill = vouchers[0];
        (bill.Attribute("REMOTEID")!.Value, bill.Attribute("VCHTYPE")!.Value, bill.Attribute("ACTION")!.Value)
            .ShouldBe(("billingrecon-0193a00000007000800000000000000" + "1", "Journal", "Create"));
        (bill.Element("DATE")!.Value, bill.Element("VOUCHERTYPENAME")!.Value, bill.Element("VOUCHERNUMBER")!.Value).ShouldBe(("20261017", "Journal", "BIL/26-27/000001"));
        bill.Elements("ALLLEDGERENTRIES.LIST").Select(line => (line.Element("LEDGERNAME")!.Value, line.Element("ISDEEMEDPOSITIVE")!.Value, line.Element("AMOUNT")!.Value)).ShouldBe(
        [
            ("Patient Receivables", "Yes", "-590.00"),
            ("Hospital Income", "No", "500.00"),
            ("GST Output", "No", "90.00"),
        ]);
        vouchers[1].Attribute("VCHTYPE")!.Value.ShouldBe("Receipt");

        TallyVoucherWriter.FileName(From, To).ShouldBe("billingrecon-tally-20261001-20261031.xml");
    }

    [Fact]
    public async Task ADR0062_a_period_with_no_posting_is_an_envelope_with_no_voucher()
    {
        _reports.VouchersAsync(From, To, Arg.Any<CancellationToken>()).Returns(new ExportVouchers([], 0m, 0m));
        var export = (await new AccountingExportHandler(Builder).HandleAsync(new AccountingExportQuery { From = From, To = To }, CancellationToken.None)).Value;
        using var stream = new MemoryStream();

        await TallyVoucherWriter.WriteAsync(stream, export, CancellationToken.None);

        var envelope = XDocument.Parse(Encoding.UTF8.GetString(stream.ToArray())).Root.ShouldNotBeNull();
        envelope.Descendants("REQUESTDATA").ShouldHaveSingleItem().HasElements.ShouldBeFalse();
    }
}
