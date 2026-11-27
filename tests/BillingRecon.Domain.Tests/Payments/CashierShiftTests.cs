using BillingRecon.Domain.Common;
using BillingRecon.Domain.Payments;

namespace BillingRecon.Domain.Tests.Payments;

/// <summary>
/// The shift of the cash counter (BR-310 to BR-315; ADR 0060): opened with a float, it takes cash documents of its
/// own business date while it is open, is counted by denomination against the cash the ledger has for it, closes at
/// once without a difference and waits with one, and never changes once it is closed. And the count itself.
/// </summary>
public sealed class CashierShiftTests
{
    private static readonly UserId Cashier = new(Guid.NewGuid());
    private static readonly DateOnly Day = new(2026, 10, 17);
    private static readonly DateTimeOffset OpenedAt = new(2026, 10, 17, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CountedAt = new(2026, 10, 17, 11, 30, 0, TimeSpan.Zero);

    [Fact]
    public void BR310_a_shift_opens_with_its_float_on_its_business_date()
    {
        var shift = CashierShift.Open(Cashier, Day, Money.Of(500m), OpenedAt);

        (shift.CashierUserId, shift.BusinessDate, shift.Status, shift.OpeningFloat, shift.OpenedAtUtc, shift.CashDocuments)
            .ShouldBe((Cashier, Day, CashierShiftStatus.Open, Money.Of(500m), OpenedAt, 0));
        (shift.Denominations, shift.CountedCash, shift.ExpectedCash, shift.Difference, shift.ClosedAtUtc).ShouldBe((null, null, null, null, null));
        shift.DomainEvents.ShouldHaveSingleItem().ShouldBe(new CashierShiftOpened(Cashier, Day, Money.Of(500m)));
    }

    [Fact]
    public void A_shift_of_no_user_or_with_a_float_below_zero_is_a_bug_in_the_caller()
    {
        Should.Throw<ArgumentException>(() => CashierShift.Open(new UserId(Guid.Empty), Day, Money.Zero, OpenedAt));
        Should.Throw<ArgumentException>(() => CashierShift.Open(Cashier, Day, Money.Of(-0.01m), OpenedAt));
        CashierShift.Open(Cashier, Day, Money.Zero, OpenedAt).OpeningFloat.ShouldBe(Money.Zero);
    }

    [Fact]
    public void BR311_an_open_shift_takes_a_cash_document_of_its_own_date_and_counts_it_without_an_event()
    {
        var shift = Opened(0m);

        shift.CheckTakes(Day).IsSuccess.ShouldBeTrue();
        shift.Takes(Day).ShouldBeTrue();
        shift.CheckTakes(Day.AddDays(1)).Error.Code.ShouldBe(PaymentsErrors.ShiftDateMismatchCode);
        shift.CheckTakes(Day.AddDays(-1)).Error.Code.ShouldBe(PaymentsErrors.ShiftDateMismatchCode);
        shift.Takes(Day.AddDays(1)).ShouldBeFalse();

        shift.TakeCashDocument();
        shift.TakeCashDocument();

        shift.CashDocuments.ShouldBe(2);
        shift.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void BR313_a_count_that_is_the_expected_cash_closes_the_shift_at_once()
    {
        var shift = Opened(500m);

        // 500.00 of float and 200.50 taken: 700.50 expected, counted as one 500, one 200 and 50 paise.
        var closed = shift.Close(CashCount.Of([(500, 1), (200, 1)]), Money.Of(0.50m), Money.Of(200.50m), "It was all there", CountedAt);

        closed.IsSuccess.ShouldBeTrue();
        (shift.Status, shift.ExpectedCash, shift.CountedCash, shift.Difference, shift.LooseChange, shift.CountedAtUtc, shift.ClosedAtUtc)
            .ShouldBe((CashierShiftStatus.Closed, Money.Of(700.50m), Money.Of(700.50m), Money.Zero, Money.Of(0.50m), CountedAt, CountedAt));

        // A reason for no difference explains nothing and is not kept.
        shift.DifferenceReason.ShouldBeNull();
        shift.DomainEvents.ShouldHaveSingleItem().ShouldBe(new CashierShiftClosed(shift.Id, Money.Of(700.50m), Money.Of(700.50m)));
    }

    [Theory]
    [InlineData(650, -50)]
    [InlineData(705, 5)]
    public void BR313_BR314_a_count_that_differs_needs_its_reason_and_then_waits_for_a_checker(int counted, int difference)
    {
        var shift = Opened(500m);
        var count = CashCount.Of([(1, counted)]);

        var silent = shift.Close(count, Money.Zero, Money.Of(200m), "  ", CountedAt);

        silent.Error.Code.ShouldBe(PaymentsErrors.ShiftDifferenceNeedsReasonCode);
        silent.Error.Kind.ShouldBe(ErrorKind.BusinessRule);
        (shift.Status, shift.CountedCash, shift.Difference).ShouldBe((CashierShiftStatus.Open, null, null));
        shift.DomainEvents.ShouldBeEmpty();

        shift.Close(count, Money.Zero, Money.Of(200m), " A note is missing ", CountedAt).IsSuccess.ShouldBeTrue();

        (shift.Status, shift.ExpectedCash, shift.CountedCash, shift.Difference, shift.DifferenceReason, shift.ClosedAtUtc)
            .ShouldBe((CashierShiftStatus.PendingApproval, Money.Of(700m), Money.Of(counted), Money.Of(difference), "A note is missing", null));
        shift.DomainEvents.ShouldHaveSingleItem().ShouldBe(
            new CashierShiftSubmittedForApproval(shift.Id, Money.Of(700m), Money.Of(counted), Money.Of(difference), "A note is missing"));
    }

    [Fact]
    public void BR312_the_expected_cash_is_the_float_and_what_the_ledger_moved_also_when_more_was_paid_out_than_taken()
    {
        var shift = Opened(100m);

        // 100.00 of float, 30.00 more paid out than taken: 70.00 expected.
        shift.Close(CashCount.Of([(50, 1), (20, 1)]), Money.Zero, Money.Of(-30m), null, CountedAt).IsSuccess.ShouldBeTrue();

        (shift.Status, shift.ExpectedCash, shift.Difference).ShouldBe((CashierShiftStatus.Closed, Money.Of(70m), Money.Zero));
    }

    [Fact]
    public void BR314_BR315_an_approval_closes_the_shift_with_its_difference_and_a_closed_shift_does_nothing_more()
    {
        var shift = Waiting();
        var decidedAt = CountedAt.AddHours(1);

        shift.ApproveClosing(decidedAt).IsSuccess.ShouldBeTrue();

        (shift.Status, shift.Difference, shift.DifferenceReason, shift.ClosedAtUtc, shift.CountedAtUtc)
            .ShouldBe((CashierShiftStatus.Closed, Money.Of(-50m), "A note is missing", decidedAt, CountedAt));
        shift.DomainEvents.ShouldHaveSingleItem().ShouldBe(new CashierShiftClosingApproved(shift.Id, Money.Of(-50m)));

        // Closed for good: nothing joins it, counts it or decides it.
        shift.CheckTakes(Day).Error.Code.ShouldBe(PaymentsErrors.ShiftNotOpenCode);
        Should.Throw<InvalidOperationException>(shift.TakeCashDocument);
        shift.Close(CashCount.Of([]), Money.Zero, Money.Zero, null, decidedAt).Error.Code.ShouldBe(PaymentsErrors.ShiftNotOpenCode);
        shift.ApproveClosing(decidedAt).Error.Code.ShouldBe(PaymentsErrors.ShiftNotPendingApprovalCode);
        shift.RejectClosing("No").Error.Code.ShouldBe(PaymentsErrors.ShiftNotPendingApprovalCode);
        shift.Status.ShouldBe(CashierShiftStatus.Closed);
    }

    [Fact]
    public void BR605_a_rejection_says_why_and_opens_the_shift_again_without_its_count()
    {
        var shift = Waiting();

        // While it waits, no cash joins it.
        shift.CheckTakes(Day).Error.Code.ShouldBe(PaymentsErrors.ShiftNotOpenCode);
        Should.Throw<ArgumentException>(() => shift.RejectClosing(" "));

        shift.RejectClosing(" Count it again ").IsSuccess.ShouldBeTrue();

        (shift.Status, shift.Denominations, shift.LooseChange, shift.CountedCash, shift.ExpectedCash, shift.Difference, shift.DifferenceReason, shift.CountedAtUtc, shift.ClosedAtUtc)
            .ShouldBe((CashierShiftStatus.Open, null, null, null, null, null, null, null, null));
        shift.DomainEvents.ShouldHaveSingleItem().ShouldBe(new CashierShiftClosingRejected(shift.Id, "Count it again"));

        // Open again: it takes cash, and is counted again.
        shift.CheckTakes(Day).IsSuccess.ShouldBeTrue();
        shift.Close(CashCount.Of([(500, 1), (200, 1)]), Money.Zero, Money.Of(200m), null, CountedAt).IsSuccess.ShouldBeTrue();
        shift.Status.ShouldBe(CashierShiftStatus.Closed);
    }

    [Fact]
    public void An_approval_or_a_rejection_of_a_shift_that_does_not_wait_is_refused()
    {
        var shift = Opened(0m);

        shift.ApproveClosing(CountedAt).Error.Code.ShouldBe(PaymentsErrors.ShiftNotPendingApprovalCode);
        shift.RejectClosing("No").Error.Code.ShouldBe(PaymentsErrors.ShiftNotPendingApprovalCode);
        shift.Status.ShouldBe(CashierShiftStatus.Open);
    }

    [Fact]
    public void Loose_change_is_what_is_below_one_rupee()
    {
        Should.Throw<ArgumentException>(() => Opened(0m).Close(CashCount.Of([]), Money.Of(1m), Money.Zero, null, CountedAt));
        Should.Throw<ArgumentException>(() => Opened(0m).Close(CashCount.Of([]), Money.Of(-0.01m), Money.Zero, null, CountedAt));
        Opened(0m).Close(CashCount.Of([]), Money.Of(0.99m), Money.Of(0.99m), null, CountedAt).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_count_names_every_denomination_once_and_comes_to_the_sum_of_its_pieces()
    {
        var count = CashCount.Of([(100, 3), (2000, 1), (1, 7)]);

        count.Text.ShouldBe("2000x1 500x0 200x0 100x3 50x0 20x0 10x0 5x0 2x0 1x7");
        count.Total().ShouldBe(Money.Of(2307m));
        count.Pieces().ShouldBe([(2000, 1), (500, 0), (200, 0), (100, 3), (50, 0), (20, 0), (10, 0), (5, 0), (2, 0), (1, 7)]);
        CashCount.Parse(count.Text).ShouldBe(count);
        count.ShouldBe(CashCount.Of([(1, 7), (100, 3), (2000, 1), (50, 0)]));
        count.Text.Length.ShouldBeLessThanOrEqualTo(CashCount.TextMaxLength);

        // The fullest count still fits its column.
        CashCount.Of(CashCount.Denominations.Select(denomination => (denomination, CashCount.MaxPieces))).Text.Length.ShouldBeLessThanOrEqualTo(CashCount.TextMaxLength);
        CashCount.Of([]).Total().ShouldBe(Money.Zero);
        default(CashCount).Pieces().Sum(piece => piece.Pieces).ShouldBe(0);
    }

    [Fact]
    public void What_is_not_a_count_is_refused()
    {
        CashCount.TryOf([(25, 1)], out _).ShouldBeFalse();
        CashCount.TryOf([(100, -1)], out _).ShouldBeFalse();
        CashCount.TryOf([(100, CashCount.MaxPieces + 1)], out _).ShouldBeFalse();
        CashCount.TryOf([(100, 1), (100, 2)], out _).ShouldBeFalse();
        Should.Throw<ArgumentException>(() => CashCount.Of([(3, 1)]));
        Should.Throw<FormatException>(() => CashCount.Parse("100x1"));
        Should.Throw<FormatException>(() => CashCount.Parse("2000x1 500x0 200x0 100x3 50x0 20x0 10x0 5x0 2x0 1xseven"));
        Should.Throw<FormatException>(() => CashCount.Parse("1x7 2x0 5x0 10x0 20x0 50x0 100x3 200x0 500x0 2000x1"));
    }

    private static CashierShift Opened(decimal openingFloat)
    {
        var shift = CashierShift.Open(Cashier, Day, Money.Of(openingFloat), OpenedAt);
        shift.ClearDomainEvents();

        return shift;
    }

    // 500.00 of float and 200.00 taken, 650.00 counted: 50.00 short, waiting.
    private static CashierShift Waiting()
    {
        var shift = Opened(500m);
        shift.Close(CashCount.Of([(500, 1), (100, 1), (50, 1)]), Money.Zero, Money.Of(200m), "A note is missing", CountedAt).IsSuccess.ShouldBeTrue();
        shift.ClearDomainEvents();

        return shift;
    }
}
