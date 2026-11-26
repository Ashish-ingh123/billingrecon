using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Domain.Tests.Identity;

/// <summary>
/// The print settings in the domain (ADR 0059): the default is the sheet as it was before there were settings; a layout
/// keeps within its limits; a bill is never printed on a roll and never without its SAC codes and its tax in its
/// halves; and settings are an event only when something changes.
/// </summary>
public sealed class PrintSettingsTests
{
    [Fact]
    public void ADR0059_the_default_layout_is_the_sheet_as_it_was_before_there_were_settings()
    {
        var layout = PrintLayout.Default;

        // A4 with the margins of the print styles (14 mm above and below, 12 mm at the sides), the logo at the left in
        // its box of 10.4c-1, and what the three sheets showed: the payer, the SAC column, the tax split, the amount
        // in words and the signature block; not the patient's contact, not the cashier's name, no footer, no terms.
        (layout.Paper, layout.MarginTopMm, layout.MarginRightMm, layout.MarginBottomMm, layout.MarginLeftMm).ShouldBe((PaperSize.A4, 14, 12, 14, 12));
        (layout.LogoPosition, layout.LogoSize, layout.PrePrintedStationery, layout.StationeryTopMm).ShouldBe((LogoPosition.Left, LogoSize.Medium, false, 0));
        (layout.ShowPayer, layout.ShowSacColumn, layout.ShowTaxSplit, layout.ShowAmountInWords, layout.ShowSignatureBlock).ShouldBe((true, true, true, true, true));
        (layout.ShowPatientContact, layout.ShowCashierName, layout.FooterLine, layout.Terms).ShouldBe((false, false, null, null));

        foreach (var documentType in Enum.GetValues<PrintedDocumentType>())
        {
            layout.Problems(documentType).ShouldBeEmpty();
        }
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(25, true)]
    [InlineData(26, false)]
    public void ADR0059_a_margin_is_between_5_and_25_mm(int margin, bool allowed)
    {
        var layouts = new[]
        {
            (Part: nameof(PrintLayout.MarginTopMm), Layout: PrintLayout.Default with { MarginTopMm = margin }),
            (Part: nameof(PrintLayout.MarginRightMm), Layout: PrintLayout.Default with { MarginRightMm = margin }),
            (Part: nameof(PrintLayout.MarginBottomMm), Layout: PrintLayout.Default with { MarginBottomMm = margin }),
            (Part: nameof(PrintLayout.MarginLeftMm), Layout: PrintLayout.Default with { MarginLeftMm = margin }),
        };

        foreach (var (part, layout) in layouts)
        {
            layout.Problems(PrintedDocumentType.Payment).Keys.ShouldBe(allowed ? [] : [part]);
        }
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(80, true)]
    [InlineData(81, false)]
    public void ADR0059_the_space_for_pre_printed_stationery_is_between_0_and_80_mm(int space, bool allowed)
        => (PrintLayout.Default with { PrePrintedStationery = true, StationeryTopMm = space })
            .Problems(PrintedDocumentType.Bill).Keys.ShouldBe(allowed ? [] : [nameof(PrintLayout.StationeryTopMm)]);

    [Fact]
    public void ADR0059_a_roll_is_for_a_receipt_or_a_refund_voucher_and_never_for_a_bill()
    {
        var roll = PrintLayout.Default with { Paper = PaperSize.Roll80 };

        roll.Problems(PrintedDocumentType.Payment).ShouldBeEmpty();
        roll.Problems(PrintedDocumentType.Refund).ShouldBeEmpty();
        roll.Problems(PrintedDocumentType.Bill).Keys.ShouldBe([nameof(PrintLayout.Paper)]);
        (PrintLayout.Default with { Paper = PaperSize.A5 }).Problems(PrintedDocumentType.Bill).ShouldBeEmpty();
    }

    [Fact]
    public void ADR0059_a_bill_cannot_switch_off_its_sac_column_or_its_tax_split_and_is_told_why()
    {
        var without = PrintLayout.Default with { ShowSacColumn = false, ShowTaxSplit = false };

        var problems = without.Problems(PrintedDocumentType.Bill);
        problems.Keys.ShouldBe([nameof(PrintLayout.ShowSacColumn), nameof(PrintLayout.ShowTaxSplit)], ignoreOrder: true);
        problems[nameof(PrintLayout.ShowSacColumn)].ShouldContain("cannot be switched off for a bill");
        problems[nameof(PrintLayout.ShowTaxSplit)].ShouldContain("cannot be switched off for a bill");

        // A receipt and a voucher have neither: the switches mean nothing there and are not refused.
        without.Problems(PrintedDocumentType.Payment).ShouldBeEmpty();
        without.Problems(PrintedDocumentType.Refund).ShouldBeEmpty();

        // Every other switch is the hospital's to turn, on a bill too.
        (PrintLayout.Default with { ShowPayer = false, ShowAmountInWords = false, ShowSignatureBlock = false, ShowPatientContact = true, ShowCashierName = true })
            .Problems(PrintedDocumentType.Bill).ShouldBeEmpty();
    }

    [Fact]
    public void ADR0059_enums_out_of_range_and_texts_too_long_are_named()
    {
        var wrong = PrintLayout.Default with
        {
            Paper = (PaperSize)9,
            LogoPosition = (LogoPosition)9,
            LogoSize = (LogoSize)9,
            FooterLine = new string('x', PrintLayout.FooterLineMaxLength + 1),
            Terms = new string('x', PrintLayout.TermsMaxLength + 1),
        };

        wrong.Problems(PrintedDocumentType.Refund).Keys.ShouldBe(
            [nameof(PrintLayout.Paper), nameof(PrintLayout.LogoPosition), nameof(PrintLayout.LogoSize), nameof(PrintLayout.FooterLine), nameof(PrintLayout.Terms)],
            ignoreOrder: true);
    }

    [Fact]
    public void ADR0059_the_first_settings_of_a_kind_are_an_event_and_only_a_change_is_another()
    {
        var layout = PrintLayout.Default with { Paper = PaperSize.A5, FooterLine = "  Thank you  ", Terms = " " };

        var settings = PrintSettings.Create(PrintedDocumentType.Payment, layout);

        settings.DocumentType.ShouldBe(PrintedDocumentType.Payment);
        settings.Layout.ShouldBe(layout with { FooterLine = "Thank you", Terms = null }, "texts are trimmed and a blank one is none");
        settings.DomainEvents.ShouldHaveSingleItem().ShouldBe(new PrintSettingsChanged(PrintedDocumentType.Payment));
        settings.ClearDomainEvents();

        settings.Change(layout).ShouldBeFalse("the same layout again changes nothing");
        settings.DomainEvents.ShouldBeEmpty();

        settings.Change(layout with { ShowCashierName = true, LogoPosition = LogoPosition.Centre }).ShouldBeTrue();
        (settings.ShowCashierName, settings.LogoPosition, settings.Paper).ShouldBe((true, LogoPosition.Centre, PaperSize.A5));
        settings.DomainEvents.ShouldHaveSingleItem().ShouldBe(new PrintSettingsChanged(PrintedDocumentType.Payment));
    }

    [Fact]
    public void ADR0059_a_layout_its_kind_may_not_have_is_never_taken()
    {
        Should.Throw<ArgumentException>(() => PrintSettings.Create(PrintedDocumentType.Bill, PrintLayout.Default with { Paper = PaperSize.Roll80 }));
        Should.Throw<ArgumentException>(() => PrintSettings.Create(PrintedDocumentType.Bill, PrintLayout.Default with { ShowTaxSplit = false }));
        Should.Throw<ArgumentException>(() => PrintSettings.Create((PrintedDocumentType)9, PrintLayout.Default));

        var bill = PrintSettings.Create(PrintedDocumentType.Bill, PrintLayout.Default);
        Should.Throw<ArgumentException>(() => bill.Change(PrintLayout.Default with { ShowSacColumn = false }));
        Should.Throw<ArgumentException>(() => bill.Change(PrintLayout.Default with { MarginLeftMm = 2 }));
        bill.Layout.ShouldBe(PrintLayout.Default);
    }
}
