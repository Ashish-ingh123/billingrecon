using BillingRecon.Application.Billing;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payers;
using BillingRecon.Application.Payments;
using BillingRecon.Application.Receivables;
using BillingRecon.Domain.Common;
using NSubstitute;

namespace BillingRecon.Application.Tests.Receivables;

/// <summary>
/// The check of an opening batch's files with the ports replaced (ADR 0053): what can be brought in, in the order of the
/// files, and for every row that cannot, its file, its line and a reason; the three totals held against the trial
/// balance to the paisa; and the CSV reader under it.
/// </summary>
public sealed class OpeningBatchCheckerTests
{
    private static readonly DateOnly CutOver = new(2027, 4, 1);
    private static readonly KnownPatient Asha = new(new PatientId(Guid.NewGuid()), "Asha Rao", Active: true, new PayerId(Guid.NewGuid()), PayerActive: true);
    private static readonly KnownPatient Ravi = new(new PatientId(Guid.NewGuid()), "Ravi Iyer", Active: true, new PayerId(Guid.NewGuid()), PayerActive: true);
    private static readonly KnownGuarantor Uncle = new(new PayerId(Guid.NewGuid()), "Mohan Rao", Active: true);

    private const string Bills =
        """
        legacy_number,mrn,guarantor_ref,bill_date,due_date,outstanding_amount
        OLD-1,NF-1,,2027-01-10,2027-01-25,1000.00
        OLD-2,nf-2,G-1,2027-02-01,,250.50
        """;

    private const string Advances =
        """
        legacy_reference,mrn,guarantor_ref,received_date,amount,method,kind
        ADV-1,NF-1,,2027-03-01,300.00,upi,
        CR-1,,G-1,2027-03-20,75.25,Cash,Credit
        """;

    private const string TrialBalance =
        """
        account,amount
        Receivable,1250.50
        Advance,300.00
        Credit,75.25
        """;

    private readonly IImportParties _parties = Substitute.For<IImportParties>();
    private readonly IOpeningReceivables _receivables = Substitute.For<IOpeningReceivables>();
    private readonly IOpeningAdvances _advances = Substitute.For<IOpeningAdvances>();

    public OpeningBatchCheckerTests()
    {
        Known(new Dictionary<string, KnownPatient>(StringComparer.OrdinalIgnoreCase) { ["NF-1"] = Asha, ["NF-2"] = Ravi });
        _parties.FindGuarantorsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, KnownGuarantor>(StringComparer.Ordinal) { ["G-1"] = Uncle });
        _receivables.FindBroughtForwardAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(new HashSet<string>());
        _advances.FindBroughtForwardAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(new HashSet<string>());
        _advances.Methods.Returns(["Cash", "Card", "UPI", "BankTransfer", "Cheque"]);
    }

    private void Known(Dictionary<string, KnownPatient> patients)
        => _parties.FindPatientsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(patients);

    private Task<OpeningBatchCheck> CheckAsync(string? bills = Bills, string? advances = Advances, string? trialBalance = TrialBalance)
        => new OpeningBatchChecker(_parties, _receivables, _advances).CheckAsync(new OpeningBatchFiles(CutOver, bills, advances, trialBalance), CancellationToken.None);

    [Fact]
    public async Task ADR0053_files_that_are_right_are_accepted_whole_in_their_order()
    {
        var check = await CheckAsync();

        check.Findings.ShouldBeEmpty();
        check.Receivables.ShouldBe(
        [
            new OpeningReceivable("OLD-1", Asha.Id, Asha.PayerId!.Value, new DateOnly(2027, 1, 10), new DateOnly(2027, 1, 25), Money.Of(1000m)),

            // The guarantor owes it, not the patient; and a bill without a due date is due on its bill date.
            new OpeningReceivable("OLD-2", Ravi.Id, Uncle.Id, new DateOnly(2027, 2, 1), new DateOnly(2027, 2, 1), Money.Of(250.50m)),
        ]);
        check.Advances.ShouldBe(
        [
            // The method is found without regard to case and answered as the enum names it.
            new OpeningAdvance("ADV-1", Asha.PayerId!.Value, new DateOnly(2027, 3, 1), "UPI", Money.Of(300m), CreditBalance: false),
            new OpeningAdvance("CR-1", Uncle.Id, new DateOnly(2027, 3, 20), "Cash", Money.Of(75.25m), CreditBalance: true),
        ]);
        (check.ReceivableTotal, check.AdvanceTotal, check.CreditBalanceTotal).ShouldBe((Money.Of(1250.50m), Money.Of(375.25m), Money.Of(75.25m)));
    }

    [Fact]
    public async Task ADR0053_every_row_that_cannot_be_brought_in_is_named_with_its_file_line_and_reason()
    {
        const string bills =
            """
            legacy_number,mrn,guarantor_ref,bill_date,due_date,outstanding_amount
            OLD-1,NF-1,,2027-01-10,2027-01-25,1000.00
            OLD-1,NF-1,,2027-01-11,,10.00
            OLD-3,NF-9,,2027-01-11,,10.00
            OLD-4,NF-1,,2027-04-01,,10.00
            OLD-5,NF-1,,2027-01-11,2027-01-10,10.00
            OLD-6,NF-1,,11/01/2027,,10.00
            OLD-7,NF-1,,2027-01-11,,10.005
            OLD-8,NF-1,,2027-01-11,,"1,000.00"
            OLD-9,NF-1,,2027-01-11,,0
            ,NF-1,,2027-01-11,,10.00
            OLD-11,NF-1,G-9,2027-01-11,,10.00
            OLD-12,,,2027-01-11,,10.00
            """;
        const string advances =
            """
            legacy_reference,mrn,guarantor_ref,received_date,amount,method,kind
            ADV-1,NF-1,G-1,2027-03-01,300.00,UPI,
            ADV-2,,,2027-03-01,300.00,UPI,
            ADV-3,NF-1,,2027-03-01,300.00,Barter,
            ADV-4,NF-1,,2027-03-01,300.00,UPI,Deposit
            ADV-5,NF-1,,2027-04-02,300.00,UPI,
            """;

        var check = await CheckAsync(bills, advances);

        check.Receivables.Select(receivable => receivable.LegacyNumber).ShouldBe(["OLD-1"]);
        check.Advances.ShouldBeEmpty();
        var found = check.Findings.Select(finding => $"{finding.File}:{finding.Line}").ToArray();
        found.ShouldBe(
        [
            "bills.csv:3", "bills.csv:4", "bills.csv:5", "bills.csv:6", "bills.csv:7", "bills.csv:8", "bills.csv:9", "bills.csv:10", "bills.csv:11", "bills.csv:12", "bills.csv:13",
            "advances.csv:2", "advances.csv:3", "advances.csv:4", "advances.csv:5", "advances.csv:6",
        ]);

        string Reason(string file, int line) => check.Findings.Single(finding => finding.File == file && finding.Line == line).Reason;
        Reason("bills.csv", 3).ShouldContain("OLD-1 is on line 2 too");
        Reason("bills.csv", 4).ShouldContain("NF-9 is not a patient here");
        Reason("bills.csv", 5).ShouldContain("before the cut-over date 2027-04-01");
        Reason("bills.csv", 6).ShouldContain("due_date is on or after the bill_date");
        Reason("bills.csv", 7).ShouldContain("yyyy-MM-dd");
        Reason("bills.csv", 8).ShouldContain("at most two decimals");
        Reason("bills.csv", 9).ShouldContain("plain number");
        Reason("bills.csv", 10).ShouldContain("above zero");
        Reason("bills.csv", 11).ShouldContain("legacy_number is required");
        Reason("bills.csv", 12).ShouldContain("G-9 is not a guarantor here");
        Reason("bills.csv", 13).ShouldContain("mrn is required");
        Reason("advances.csv", 2).ShouldContain("and not both");
        Reason("advances.csv", 3).ShouldContain("and not both");
        Reason("advances.csv", 4).ShouldContain("method is one of Cash, Card, UPI, BankTransfer, Cheque");
        Reason("advances.csv", 5).ShouldContain("kind is Advance or Credit");
        Reason("advances.csv", 6).ShouldContain("before the cut-over date");
    }

    [Fact]
    public async Task ADR0053_a_finding_never_repeats_the_name_of_a_patient()
    {
        Known(new Dictionary<string, KnownPatient>(StringComparer.OrdinalIgnoreCase) { ["NF-1"] = Asha with { Active = false }, ["NF-2"] = Ravi with { PayerId = null } });

        var check = await CheckAsync();

        // The bill and the advance of the inactive patient. The other patient has no payer of its own, but its bill is
        // owed by a guarantor, so nothing is wrong with it.
        check.Findings.Select(finding => $"{finding.File}:{finding.Line}").ShouldBe(["bills.csv:2", "advances.csv:2"]);
        check.Findings.ShouldAllBe(finding => !finding.Reason.Contains("Asha", StringComparison.Ordinal) && !finding.Reason.Contains("Rao", StringComparison.Ordinal));
        check.Findings[0].Reason.ShouldContain("inactive (BR-102)");
    }

    [Fact]
    public async Task ADR0053_an_old_number_that_was_brought_in_before_is_rejected()
    {
        _receivables.FindBroughtForwardAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(new HashSet<string> { "OLD-2" });
        _advances.FindBroughtForwardAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(new HashSet<string> { "CR-1" });

        var check = await CheckAsync();

        check.Findings.Select(finding => $"{finding.File}:{finding.Line}").ShouldBe(["bills.csv:3", "advances.csv:3"]);
        check.Findings.ShouldAllBe(finding => finding.Reason.Contains("was brought in before", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("account,amount\nReceivable,1250.49\nAdvance,300.00\nCredit,75.25", 2, "says 1250.49 for Receivable; the rows of the files come to 1250.50")]
    [InlineData("account,amount\nReceivable,1250.50\nAdvance,375.25\nCredit,75.25", 3, "says 375.25 for Advance; the rows of the files come to 300.00")]
    [InlineData("account,amount\nReceivable,1250.50\nAdvance,300.00", 0, "no row for Credit; the rows of the files come to 75.25")]
    [InlineData("account,amount\nReceivable,1250.50\nAdvance,300.00\nCredit,75.25\nRevenue,10.00", 5, "The account is Receivable, Advance or Credit")]
    [InlineData("account,amount\nReceivable,1250.50\nReceivable,1250.50\nAdvance,300.00\nCredit,75.25", 3, "Receivable is on line 2 too")]
    [InlineData("account\nReceivable", 1, "The header lacks: amount")]
    [InlineData(" ", 0, "The file is required")]
    public async Task ADR0053_the_totals_must_be_those_of_the_trial_balance_to_the_paisa(string trialBalance, int line, string reason)
    {
        var check = await CheckAsync(trialBalance: trialBalance);

        var finding = check.Findings.ShouldHaveSingleItem();
        (finding.File, finding.Line).ShouldBe(("trial_balance.csv", line));
        finding.Reason.ShouldContain(reason);
    }

    [Fact]
    public async Task ADR0053_empty_files_a_missing_column_and_too_many_rows_are_findings_of_the_file()
    {
        var nothing = await CheckAsync(bills: null, advances: " ", trialBalance: "account,amount");
        nothing.Findings.ShouldContain(finding => finding.Line == 0 && finding.Reason.Contains("nothing to bring in", StringComparison.Ordinal));

        var noColumn = await CheckAsync(bills: "legacy_number,mrn,bill_date\nOLD-1,NF-1,2027-01-10");
        noColumn.Findings.ShouldContain(new ImportFinding("bills.csv", 1, "The header lacks: outstanding_amount."));

        var many = "legacy_number,mrn,bill_date,outstanding_amount\n" + string.Join('\n', Enumerable.Range(1, 2001).Select(row => $"OLD-{row},NF-1,2027-01-10,1.00"));
        var tooMany = await CheckAsync(bills: many, advances: null, trialBalance: "account,amount\nReceivable,2001.00");
        tooMany.Findings.ShouldContain(finding => finding.Line == 0 && finding.Reason.Contains("at most 2000", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ADR0053_a_file_larger_than_one_request_can_carry_is_refused_whole()
    {
        // The proxy in front of the API takes 1 MB a request, so three files of this size could never arrive together.
        var large = "legacy_number,mrn,bill_date,outstanding_amount\n" + new string('x', OpeningBatchFiles.MaxFileLength);

        var check = await CheckAsync(bills: large, advances: null);

        check.Findings.ShouldContain(finding => finding.File == "bills.csv" && finding.Line == 0 && finding.Reason.Contains("at most 250000 characters", StringComparison.Ordinal));
        check.Receivables.ShouldBeEmpty();
    }

    [Fact]
    public async Task ADR0053_the_files_of_patients_and_guarantors_are_held_to_what_one_request_can_carry()
    {
        var validator = new ImportPartiesCommandValidator();
        var patients = "mrn,full_name\n" + new string('x', ImportPartiesCommand.MaxFileLength);
        var guarantors = "guarantor_ref,name\n" + new string('x', ImportPartiesCommand.MaxGuarantorsFileLength);

        (await validator.ValidateAsync(new ImportPartiesCommand { PatientsCsv = "mrn,full_name\nNF-1,Asha Rao", GuarantorsCsv = null })).IsValid.ShouldBeTrue();
        (await validator.ValidateAsync(new ImportPartiesCommand { PatientsCsv = " " })).Errors.ShouldHaveSingleItem().ErrorMessage.ShouldBe("The patients' file is required.");
        (await validator.ValidateAsync(new ImportPartiesCommand { PatientsCsv = patients })).Errors.ShouldHaveSingleItem().ErrorMessage.ShouldContain("at most 600000 characters");
        (await validator.ValidateAsync(new ImportPartiesCommand { PatientsCsv = "mrn,full_name\nNF-1,Asha Rao", GuarantorsCsv = guarantors })).Errors.ShouldHaveSingleItem().ErrorMessage.ShouldContain("at most 200000 characters");
        (ImportPartiesCommand.MaxFileLength + ImportPartiesCommand.MaxGuarantorsFileLength).ShouldBeLessThan(1_000_000);
        (3 * OpeningBatchFiles.MaxFileLength).ShouldBeLessThan(1_000_000);
    }

    [Fact]
    public void ADR0053_the_same_files_for_the_same_date_have_the_same_hash_and_any_change_another()
    {
        var files = new OpeningBatchFiles(CutOver, Bills, Advances, TrialBalance);

        files.ContentHash().ShouldBe(new OpeningBatchFiles(CutOver, Bills, Advances, TrialBalance).ContentHash());
        files.ContentHash().Length.ShouldBe(64);
        files.ContentHash().ShouldNotBe((files with { CutOverDate = CutOver.AddDays(1) }).ContentHash());
        files.ContentHash().ShouldNotBe((files with { BillsCsv = Bills + " " }).ContentHash());

        // What is in which file matters, not only the text of the three together.
        new OpeningBatchFiles(CutOver, "a", "b", "c").ContentHash().ShouldNotBe(new OpeningBatchFiles(CutOver, "a", "", "bc").ContentHash());
    }

    [Fact]
    public void ADR0053_the_csv_reader_keeps_the_line_of_each_row_and_reads_quoted_fields()
    {
        var table = CsvTable.Parse("﻿ Name , Amount ,note\r\n\"Rao, Asha\",\"1,000.00\",\"said \"\"later\"\"\"\r\n\r\n\"two\nlines\",5,\nlast,7");

        table.Headers.ShouldBe(["Name", "Amount", "note"]);
        table.Rows.Select(row => row.Line).ShouldBe([2, 4, 6]);
        table.Field(table.Rows[0], "name").ShouldBe("Rao, Asha");
        table.Field(table.Rows[0], "AMOUNT").ShouldBe("1,000.00");
        table.Field(table.Rows[0], "note").ShouldBe("said \"later\"");
        table.Field(table.Rows[1], "name").ShouldBe("two\nlines");
        table.Field(table.Rows[1], "note").ShouldBeNull("a blank field is no value");
        table.Field(table.Rows[2], "note").ShouldBeNull("the row is shorter than the header");
        table.Field(table.Rows[2], "absent").ShouldBeNull();
        table.Missing("name", "mrn", "amount").ShouldBe(["mrn"]);

        CsvTable.Parse(null).Headers.ShouldBeEmpty();
        CsvTable.Parse(" \n\n").Rows.ShouldBeEmpty();
    }
}
