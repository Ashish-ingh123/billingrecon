using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Tests.Billing;

/// <summary>
/// The Bill aggregate as a draft (BR-201 to BR-205; docs/domain-model.md, sections 4.4 and 5.1): its lines and totals,
/// its dates, and the two states it can be in, Draft and Voided.
/// </summary>
public sealed class BillTests
{
    private const int Seed = 20261120;

    private static readonly DateOnly Today = new(2026, 11, 20);
    private static readonly BusinessDateWindow Window = new(Today, maxBackdateDays: 7);
    private static readonly PatientId Patient = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301"));
    private static readonly PayerId Payer = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3302"));
    private static readonly ServiceItemId Consultation = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3303"));
    private static readonly DateTimeOffset VoidedAt = new(2026, 11, 20, 9, 15, 0, TimeSpan.Zero);
    private static readonly UserId Clerk = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3304"));

    [Fact]
    public void US20_a_new_bill_is_a_draft_for_its_patient_and_payer_with_its_header_and_lines()
    {
        var bill = Bill.Create(Patient, Payer, Header() with { EncounterReference = " OPD-7781 ", Notes = "  " }, [Line(), FreeText()], Window).Value;

        bill.Status.ShouldBe(BillStatus.Draft);
        bill.PatientId.ShouldBe(Patient);
        bill.PayerId.ShouldBe(Payer);
        bill.BillDate.ShouldBe(Today);
        bill.DueDate.ShouldBe(Today.AddDays(15));
        bill.EncounterReference.ShouldBe("OPD-7781");
        bill.Notes.ShouldBeNull("a blank text is stored as null");
        bill.Lines.Select(line => line.LineNo).ShouldBe([1, 2]);
        bill.Lines[0].ServiceItemId.ShouldBe(Consultation);
        bill.Lines[1].ServiceItemId.ShouldBeNull();
        bill.VoidedAtUtc.ShouldBeNull();
        bill.VoidReason.ShouldBeNull();
    }

    [Fact]
    public void A_new_bill_has_no_id_no_tenant_and_no_row_version_until_it_is_saved()
    {
        var bill = Draft();

        bill.Id.ShouldBe(default);
        bill.TenantId.ShouldBe(default);
        bill.RowVersion.ShouldBeEmpty();
    }

    [Fact]
    public void Creating_raises_BillDrafted_with_the_parties_the_date_and_the_total()
    {
        var bill = Draft();

        bill.DomainEvents.ShouldHaveSingleItem().ShouldBe(new BillDrafted(Patient, Payer, Today, bill.Total));
    }

    [Fact]
    public void BR202_each_line_carries_the_amounts_of_the_line_arithmetic()
    {
        var bill = Bill.Create(
            Patient,
            Payer,
            Header(),
            [new BillLineDetails { Description = "Dressing", Quantity = Quantity.Of(3m), UnitPrice = UnitPrice.Of(333.3333m), DiscountAmount = Money.Of(0.50m), TaxRate = Percent.Of(5m) }],
            Window).Value;

        var line = bill.Lines.ShouldHaveSingleItem();
        line.LineAmount.ShouldBe(Money.Of(1000.00m));
        line.DiscountAmount.ShouldBe(Money.Of(0.50m));
        line.TaxAmount.ShouldBe(Money.Of(49.98m));
        line.LineTotal.ShouldBe(Money.Of(1049.48m));
    }

    [Fact]
    public void BR203_the_totals_are_the_sums_of_the_lines_and_are_not_rounded_again()
    {
        // Three lines of one unit at 0.005 with 100% tax on top: each line is 0.01 + 0.01. Rounding the sums of the
        // raw products instead (0.015 and 0.015) would give 0.02 and 0.02.
        var halfPaisa = new BillLineDetails { Description = "Half a paisa", Quantity = Quantity.Of(1m), UnitPrice = UnitPrice.Of(0.005m), TaxRate = Percent.Of(100m) };

        var bill = Bill.Create(Patient, Payer, Header(), [halfPaisa, halfPaisa, halfPaisa], Window).Value;

        bill.SubTotal.ShouldBe(Money.Of(0.03m));
        bill.DiscountTotal.ShouldBe(Money.Zero);
        bill.TaxTotal.ShouldBe(Money.Of(0.03m));
        bill.Total.ShouldBe(Money.Of(0.06m));
        bill.BalanceDue.ShouldBe(bill.Total, "a draft carries its total as the balance");
    }

    [Fact]
    public void BR203_for_every_generated_bill_the_totals_equal_the_sums_of_its_lines()
    {
        var random = new Random(Seed);

        for (var run = 0; run < 300; run++)
        {
            var lines = Enumerable.Range(0, random.Next(0, 25)).Select(_ => GeneratedLine(random)).ToArray();

            var bill = Bill.Create(Patient, Payer, Header(), lines, Window).Value;

            bill.Lines.Count.ShouldBe(lines.Length);
            bill.SubTotal.ShouldBe(bill.Lines.Select(line => line.LineAmount).Sum());
            bill.DiscountTotal.ShouldBe(bill.Lines.Select(line => line.DiscountAmount).Sum());
            bill.TaxTotal.ShouldBe(bill.Lines.Select(line => line.TaxAmount).Sum());
            bill.Total.ShouldBe(bill.Lines.Select(line => line.LineTotal).Sum());
            bill.Total.ShouldBe(bill.SubTotal - bill.DiscountTotal + bill.TaxTotal);
            bill.Total.IsNegative.ShouldBeFalse();
            bill.BalanceDue.ShouldBe(bill.Total);
        }
    }

    [Fact]
    public void A_draft_may_have_no_lines_and_then_comes_to_nothing()
    {
        var bill = Bill.Create(Patient, Payer, Header(), [], Window).Value;

        bill.Lines.ShouldBeEmpty();
        bill.Total.ShouldBe(Money.Zero);
    }

    [Fact]
    public void BR201_a_discount_of_the_whole_line_is_allowed_and_one_paisa_more_is_refused_naming_the_line()
    {
        var whole = Line() with { DiscountAmount = Money.Of(500m) };
        var tooMuch = Line() with { DiscountAmount = Money.Of(500.01m) };

        Bill.Create(Patient, Payer, Header(), [whole], Window).Value.Total.ShouldBe(Money.Zero);

        var refused = Bill.Create(Patient, Payer, Header(), [Line(), tooMuch], Window);
        refused.Error.Code.ShouldBe(BillingErrors.BillLineDiscountExceedsAmountCode);
        refused.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
        refused.Error.Message.ShouldContain("line 2");
    }

    [Fact]
    public void A_line_whose_amount_is_larger_than_a_line_can_hold_is_refused()
    {
        var huge = Line() with { Quantity = Quantity.Of(999_999.999m), UnitPrice = UnitPrice.Of(1_000_000m) };

        Bill.Create(Patient, Payer, Header(), [huge], Window).Error.Code.ShouldBe(BillingErrors.BillLineAmountTooLargeCode);
    }

    [Fact]
    public void A_line_built_from_the_default_quantity_a_negative_discount_or_a_blank_description_is_a_bug_and_throws()
    {
        Should.Throw<ArgumentException>(() => Bill.Create(Patient, Payer, Header(), [Line() with { Quantity = default }], Window));
        Should.Throw<ArgumentException>(() => Bill.Create(Patient, Payer, Header(), [Line() with { DiscountAmount = Money.Of(-0.01m) }], Window));
        Should.Throw<ArgumentException>(() => Bill.Create(Patient, Payer, Header(), [Line() with { Description = "  " }], Window));
        Should.Throw<ArgumentException>(() => Bill.Create(Patient, Payer, Header(), [Line() with { ServiceItemId = new ServiceItemId(Guid.Empty) }], Window));
    }

    [Fact]
    public void More_lines_than_a_bill_may_have_and_an_empty_patient_or_payer_are_bugs_and_throw()
    {
        var tooMany = Enumerable.Repeat(Line(), Bill.MaxLines + 1).ToArray();

        Should.Throw<ArgumentException>(() => Bill.Create(Patient, Payer, Header(), tooMany, Window));
        Should.Throw<ArgumentException>(() => Bill.Create(new PatientId(Guid.Empty), Payer, Header(), [], Window)).ParamName.ShouldBe("patientId");
        Should.Throw<ArgumentException>(() => Bill.Create(Patient, new PayerId(Guid.Empty), Header(), [], Window)).ParamName.ShouldBe("payerId");
        Bill.Create(Patient, Payer, Header(), Enumerable.Repeat(Line(), Bill.MaxLines).ToArray(), Window).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0, true)] // today
    [InlineData(-7, true)] // the last day the tenant allows
    [InlineData(-8, false)] // one day too old
    [InlineData(1, false)] // tomorrow
    public void BR205_the_bill_date_is_not_in_the_future_and_not_older_than_the_tenant_allows(int daysFromToday, bool allowed)
    {
        var billDate = Today.AddDays(daysFromToday);

        var result = Bill.Create(Patient, Payer, new BillHeader { BillDate = billDate, DueDate = billDate }, [], Window);

        result.IsSuccess.ShouldBe(allowed);
        if (!allowed)
        {
            result.Error.Code.ShouldBe(daysFromToday > 0 ? BillingErrors.BillDateInFutureCode : BillingErrors.BillDateTooOldCode);
            result.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
        }
    }

    [Fact]
    public void BR205_the_due_date_may_be_the_bill_date_or_later_and_never_before_it()
    {
        Bill.Create(Patient, Payer, new BillHeader { BillDate = Today, DueDate = Today }, [], Window).IsSuccess.ShouldBeTrue();
        Bill.Create(Patient, Payer, new BillHeader { BillDate = Today, DueDate = Today.AddYears(1) }, [], Window).IsSuccess.ShouldBeTrue();

        Bill.Create(Patient, Payer, new BillHeader { BillDate = Today, DueDate = Today.AddDays(-1) }, [], Window)
            .Error.Code.ShouldBe(BillingErrors.BillDueDateBeforeBillDateCode);
    }

    [Fact]
    public void BR204_replacing_the_lines_of_a_draft_renumbers_them_and_recomputes_the_totals()
    {
        var bill = Bill.Create(Patient, Payer, Header(), [Line(), FreeText(), Line()], Window).Value;
        var firstRow = bill.Lines[0];

        bill.ReplaceLines([FreeText(), Line() with { Quantity = Quantity.Of(2m) }]).IsSuccess.ShouldBeTrue();

        bill.Lines.Select(line => line.LineNo).ShouldBe([1, 2]);
        bill.Lines[0].ShouldBeSameAs(firstRow, "a line that keeps its position keeps its row");
        bill.Lines[0].Description.ShouldBe("Dressing");
        bill.Lines[0].ServiceItemId.ShouldBeNull();
        bill.Lines[1].Quantity.ShouldBe(Quantity.Of(2m));
        bill.SubTotal.ShouldBe(Money.Of(120m + 1000m));
        bill.Total.ShouldBe(bill.Lines.Select(line => line.LineTotal).Sum());
        bill.BalanceDue.ShouldBe(bill.Total);
    }

    [Fact]
    public void BR204_replacing_with_more_lines_adds_them_and_with_none_removes_them_all()
    {
        var bill = Bill.Create(Patient, Payer, Header(), [Line()], Window).Value;

        bill.ReplaceLines([Line(), FreeText(), FreeText()]).IsSuccess.ShouldBeTrue();
        bill.Lines.Select(line => line.LineNo).ShouldBe([1, 2, 3]);

        bill.ReplaceLines([]).IsSuccess.ShouldBeTrue();
        bill.Lines.ShouldBeEmpty();
        bill.Total.ShouldBe(Money.Zero);
    }

    [Fact]
    public void Lines_that_are_refused_leave_the_bill_as_it_was()
    {
        var bill = Bill.Create(Patient, Payer, Header(), [Line()], Window).Value;
        var total = bill.Total;

        var result = bill.ReplaceLines([FreeText(), Line() with { DiscountAmount = Money.Of(9999m) }]);

        result.Error.Code.ShouldBe(BillingErrors.BillLineDiscountExceedsAmountCode);
        bill.Lines.ShouldHaveSingleItem().Description.ShouldBe("OPD consultation");
        bill.Total.ShouldBe(total);
    }

    [Fact]
    public void BR204_updating_the_header_of_a_draft_replaces_its_dates_and_texts_and_keeps_everything_else()
    {
        var bill = Bill.Create(Patient, Payer, Header() with { EncounterReference = "OPD-1", Notes = "First visit" }, [Line()], Window).Value;
        var total = bill.Total;

        var result = bill.UpdateHeader(new BillHeader { BillDate = Today.AddDays(-2), DueDate = Today.AddDays(28), Notes = "Follow-up" }, Window);

        result.IsSuccess.ShouldBeTrue();
        bill.BillDate.ShouldBe(Today.AddDays(-2));
        bill.DueDate.ShouldBe(Today.AddDays(28));
        bill.EncounterReference.ShouldBeNull("a text the correction leaves out is cleared");
        bill.Notes.ShouldBe("Follow-up");
        bill.PatientId.ShouldBe(Patient);
        bill.Total.ShouldBe(total);
    }

    [Fact]
    public void BR205_a_changed_bill_date_must_be_allowed_today_and_a_refused_header_changes_nothing()
    {
        var bill = Draft();

        bill.UpdateHeader(new BillHeader { BillDate = Today.AddDays(1), DueDate = Today.AddDays(1), Notes = "Changed" }, Window)
            .Error.Code.ShouldBe(BillingErrors.BillDateInFutureCode);
        bill.UpdateHeader(new BillHeader { BillDate = Today.AddDays(-8), DueDate = Today, Notes = "Changed" }, Window)
            .Error.Code.ShouldBe(BillingErrors.BillDateTooOldCode);
        bill.UpdateHeader(new BillHeader { BillDate = Today, DueDate = Today.AddDays(-1), Notes = "Changed" }, Window)
            .Error.Code.ShouldBe(BillingErrors.BillDueDateBeforeBillDateCode);

        bill.BillDate.ShouldBe(Today);
        bill.Notes.ShouldBeNull();
    }

    [Fact]
    public void BR205_a_draft_that_has_grown_old_can_still_be_corrected_while_its_bill_date_stays()
    {
        var bill = Draft();
        var tenDaysLater = new BusinessDateWindow(Today.AddDays(10), maxBackdateDays: 7);

        bill.UpdateHeader(new BillHeader { BillDate = Today, DueDate = Today.AddDays(30), Notes = "Corrected late" }, tenDaysLater).IsSuccess.ShouldBeTrue();
        bill.Notes.ShouldBe("Corrected late");

        // Moving the date is a new date: it must be allowed on the day it is entered.
        bill.UpdateHeader(new BillHeader { BillDate = Today.AddDays(1), DueDate = Today.AddDays(30) }, tenDaysLater)
            .Error.Code.ShouldBe(BillingErrors.BillDateTooOldCode);
    }

    [Fact]
    public void Updating_the_header_raises_BillHeaderUpdated_when_a_value_changed_and_nothing_when_none_did()
    {
        var bill = Draft();
        bill.ClearDomainEvents();

        bill.UpdateHeader(Header(), Window).IsSuccess.ShouldBeTrue();
        bill.DomainEvents.ShouldBeEmpty("the same header changes nothing");

        bill.UpdateHeader(Header() with { Notes = "Second visit" }, Window).IsSuccess.ShouldBeTrue();
        bill.DomainEvents.ShouldHaveSingleItem().ShouldBe(new BillHeaderUpdated(bill.Id, Today, Today.AddDays(15)));
    }

    [Fact]
    public void Replacing_the_lines_raises_BillLinesReplaced_when_a_line_changed_and_nothing_when_none_did()
    {
        var bill = Draft();
        bill.ClearDomainEvents();

        bill.ReplaceLines([Line()]).IsSuccess.ShouldBeTrue();
        bill.DomainEvents.ShouldBeEmpty("the same lines change nothing");

        bill.ReplaceLines([Line(), FreeText()]).IsSuccess.ShouldBeTrue();
        bill.DomainEvents.ShouldHaveSingleItem().ShouldBe(new BillLinesReplaced(bill.Id, 2, bill.Total));

        bill.ClearDomainEvents();
        bill.ReplaceLines([Line(), FreeText() with { Description = "Dressing, large" }]).IsSuccess.ShouldBeTrue();
        bill.DomainEvents.ShouldHaveSingleItem("a description alone is a change, although no amount moved").ShouldBeOfType<BillLinesReplaced>();
    }

    [Fact]
    public void A_refused_header_or_refused_lines_raise_nothing()
    {
        var bill = Draft();
        bill.ClearDomainEvents();

        bill.UpdateHeader(Header() with { DueDate = Today.AddDays(-1) }, Window).IsFailure.ShouldBeTrue();
        bill.ReplaceLines([Line() with { DiscountAmount = Money.Of(500.01m) }]).IsFailure.ShouldBeTrue();

        bill.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void US23_voiding_a_draft_keeps_its_content_and_records_who_when_and_why()
    {
        var bill = Bill.Create(Patient, Payer, Header(), [Line(), FreeText()], Window).Value;
        var total = bill.Total;
        bill.ClearDomainEvents();

        bill.Void("  Entered for the wrong patient ", VoidedAt, Clerk).IsSuccess.ShouldBeTrue();

        bill.Status.ShouldBe(BillStatus.Voided);
        bill.VoidedAtUtc.ShouldBe(VoidedAt);
        bill.VoidedByUserId.ShouldBe(Clerk);
        bill.VoidReason.ShouldBe("Entered for the wrong patient");
        bill.Lines.Count.ShouldBe(2);
        bill.Total.ShouldBe(total);
        bill.DomainEvents.ShouldHaveSingleItem().ShouldBe(new BillVoided(bill.Id, Patient, Payer, total, "Entered for the wrong patient"));
    }

    [Fact]
    public void BR204_a_voided_bill_refuses_every_change_and_stays_exactly_as_it_was()
    {
        var bill = Bill.Create(Patient, Payer, Header(), [Line()], Window).Value;
        bill.Void("Duplicate", VoidedAt, Clerk);
        bill.ClearDomainEvents();
        var total = bill.Total;

        bill.ReplaceLines([FreeText()]).Error.Code.ShouldBe(BillingErrors.BillNotDraftCode);
        bill.UpdateHeader(new BillHeader { BillDate = Today, DueDate = Today, Notes = "Changed" }, Window).Error.Code.ShouldBe(BillingErrors.BillNotDraftCode);
        var again = bill.Void("Again", VoidedAt.AddHours(1), voidedBy: null);

        again.Error.Code.ShouldBe(BillingErrors.BillNotDraftCode);
        again.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
        bill.Lines.ShouldHaveSingleItem().Description.ShouldBe("OPD consultation");
        bill.Total.ShouldBe(total);
        bill.Notes.ShouldBeNull();
        bill.VoidReason.ShouldBe("Duplicate");
        bill.VoidedAtUtc.ShouldBe(VoidedAt);
        bill.DomainEvents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Voiding_without_a_reason_is_a_bug_and_throws_and_the_bill_stays_a_draft(string reason)
    {
        var bill = Draft();

        Should.Throw<ArgumentException>(() => bill.Void(reason, VoidedAt, Clerk));

        bill.Status.ShouldBe(BillStatus.Draft);
    }

    private static Bill Draft() => Bill.Create(Patient, Payer, Header(), [Line()], Window).Value;

    private static BillHeader Header() => new() { BillDate = Today, DueDate = Today.AddDays(15) };

    // 1 x 500.00 at 18%: 500.00 + 90.00.
    private static BillLineDetails Line() => new()
    {
        ServiceItemId = Consultation,
        Description = "OPD consultation",
        Quantity = Quantity.Of(1m),
        UnitPrice = UnitPrice.Of(500m),
        TaxRate = Percent.Of(18m),
    };

    // 2 x 60.00 at 5%: 120.00 + 6.00.
    private static BillLineDetails FreeText() => new()
    {
        Description = "Dressing",
        Quantity = Quantity.Of(2m),
        UnitPrice = UnitPrice.Of(60m),
        TaxRate = Percent.Of(5m),
    };

    private static BillLineDetails GeneratedLine(Random random)
    {
        var quantity = Quantity.Of(random.Next(1, 100_000) / 1000m);
        var unitPrice = UnitPrice.Of(random.Next(0, 50_000_000) / 10000m);
        var lineAmount = LineCalculator.LineAmount(quantity, unitPrice);

        return new BillLineDetails
        {
            Description = "Generated",
            Quantity = quantity,
            UnitPrice = unitPrice,
            DiscountAmount = Money.Round(lineAmount.Amount * random.Next(0, 101) / 100m),
            TaxRate = Percent.Of(random.Next(0, 10_001) / 100m),
        };
    }
}
