using System.Diagnostics;
using BillingRecon.Application.Reconciliation;
using BillingRecon.Domain.Common;

namespace BillingRecon.Application.Tests.Reconciliation;

/// <summary>
/// The rules of automatic matching, each on its own (BR-1008 to BR-1011; ADR 0066 as approved for Prompt 13.2): the
/// certain case confirmed, a tie never confirmed, the reference in the description, the window, the three rules of
/// proposals in their order with their scores, and 10,000 lines against 10,000 payments, timed.
/// </summary>
public sealed class StatementMatcherTests
{
    private static readonly DateOnly Day = new(2026, 10, 16);
    private static readonly Guid Asha = Guid.NewGuid();
    private static readonly Guid Ravi = Guid.NewGuid();

    private static LineToMatch Line(decimal amount, string? reference = null, string description = "NEFT CR", int daysAfter = 0)
        => new(new StatementLineId(Guid.NewGuid()), Day.AddDays(daysAfter), Money.Of(amount), reference, description);

    private static MatchCandidate Payment(decimal amount, string? reference, int daysBefore = 0, Guid? payer = null, string number = "PAY/26-27/000001")
        => new(Guid.NewGuid(), number, payer ?? Asha, Day.AddDays(-daysBefore), "BankTransfer", reference, Money.Of(amount));

    private static MatchDecision Only(LineToMatch line, params MatchCandidate[] candidates)
        => StatementMatcher.Decide([line], candidates, 3)[line.Id];

    [Fact]
    public void BR1010_the_reference_the_amount_and_the_date_agreeing_with_one_payment_is_confirmed_with_its_reason()
    {
        var payment = Payment(12000m, "N3341", daysBefore: 1, number: "PAY/26-27/000233");

        var decision = Only(Line(12000m, "N3341"), payment, Payment(12000m, "N9999"));

        decision.Confirmed.ShouldNotBeNull().ShouldHaveSingleItem().Id.ShouldBe(payment.Id);
        decision.Reason.ShouldBe("The reference N3341 and the amount ₹12,000.00 agree with PAY/26-27/000233 of 15 Oct 2026.");
        decision.Proposals.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("n 3341", "N3341")]
    [InlineData("004512", "4512")]
    public void BR1010_a_reference_agrees_without_case_spaces_and_leading_zeros(string onTheLine, string onThePayment)
        => Only(Line(500m, onTheLine), Payment(500m, onThePayment)).Confirmed.ShouldNotBeNull();

    [Fact]
    public void A_reference_of_eight_characters_or_more_written_in_the_description_agrees_and_a_shorter_one_does_not()
    {
        var utr = Payment(12000m, "HDFCR52026101612345");
        Only(Line(12000m, null, "NEFT CR-HDFCR52026101612345-RAVI KUMAR"), utr).Confirmed.ShouldNotBeNull();

        // A cheque number of six digits inside a narration could be anything: it is not a reference that agrees.
        var cheque = Payment(12000m, "004512");
        var decision = Only(Line(12000m, null, "CHQ DEP 004512 HOSPITAL"), cheque);
        decision.Confirmed.ShouldBeNull();
        decision.Proposals.ShouldHaveSingleItem().Rule.ShouldBe(2, "the amount and the date still propose it");
    }

    [Fact]
    public void BR1011_a_tie_is_never_confirmed_and_both_are_proposed_by_the_reference()
    {
        var first = Payment(500m, "UPI001", number: "PAY/26-27/000010");
        var second = Payment(500m, "UPI001", number: "PAY/26-27/000011");

        var decision = Only(Line(500m, "UPI001"), first, second);

        decision.Confirmed.ShouldBeNull();
        decision.Proposals.Select(proposal => proposal.Items.Single().Id).ShouldBe([first.Id, second.Id], ignoreOrder: true);
        decision.Proposals.ShouldAllBe(proposal => proposal.Rule == 1 && proposal.Score == 90);
    }

    [Fact]
    public void BR1010_two_lines_with_the_reference_of_one_payment_confirm_neither()
    {
        var payment = Payment(500m, "UPI001");
        var first = Line(500m, "UPI001");
        var second = Line(500m, "UPI001");

        var decisions = StatementMatcher.Decide([first, second], [payment], 3);

        decisions[first.Id].Confirmed.ShouldBeNull();
        decisions[second.Id].Confirmed.ShouldBeNull();
        decisions[first.Id].Proposals.ShouldHaveSingleItem().Reason.ShouldContain("another line or item");
    }

    [Fact]
    public void BR1009_a_payment_dated_after_the_line_or_before_the_window_is_not_confirmed_and_is_proposed_by_its_reference()
    {
        Only(Line(500m, "UPI001"), Payment(500m, "UPI001", daysBefore: -1)).Confirmed.ShouldBeNull("dated after the line");

        var decision = Only(Line(500m, "UPI001"), Payment(500m, "UPI001", daysBefore: 5));
        decision.Confirmed.ShouldBeNull();
        var proposal = decision.Proposals.ShouldHaveSingleItem();
        (proposal.Rule, proposal.Score).ShouldBe((1, 65));
        proposal.Reason.ShouldContain("5 days before the line, outside the window of 3 days");
    }

    [Fact]
    public void BR1011_rule_one_names_an_amount_that_differs()
    {
        var proposal = Only(Line(500m, "UPI001"), Payment(450m, "UPI001")).Proposals.ShouldHaveSingleItem();

        proposal.Rule.ShouldBe(1);
        proposal.Reason.ShouldContain("its amount is ₹450.00 and the line's ₹500.00");
    }

    [Fact]
    public void BR1011_rule_two_proposes_one_to_three_payments_of_the_amount_in_the_window_and_none_when_more()
    {
        var one = Only(Line(500m), Payment(500m, "X1", daysBefore: 2)).Proposals.ShouldHaveSingleItem();
        (one.Rule, one.Score).ShouldBe((2, 60));

        Only(Line(500m), Payment(500m, "X1"), Payment(500m, "X2"), Payment(500m, "X3")).Proposals.Count.ShouldBe(3);
        Only(Line(500m), Payment(500m, "X1"), Payment(500m, "X2"), Payment(500m, "X3"), Payment(500m, "X4")).Proposals.ShouldBeEmpty();
    }

    [Fact]
    public void BR1011_rule_three_finds_payments_of_one_payer_that_add_up_to_the_line_and_never_mixes_payers()
    {
        var parts = new[] { Payment(1200m, "A", payer: Ravi), Payment(800m, "B", payer: Ravi), Payment(300m, "C", payer: Ravi) };
        var proposal = Only(Line(2000m), [.. parts, Payment(700m, "D", payer: Asha), Payment(1250m, "E", payer: Asha)]).Proposals.ShouldHaveSingleItem();

        (proposal.Rule, proposal.Score).ShouldBe((3, 50));
        proposal.Items.Select(item => item.Id).ShouldBe([parts[0].Id, parts[1].Id], ignoreOrder: true);
        proposal.Reason.ShouldStartWith("2 items of one payer add up to ₹2,000.00");

        // Asha's 700 and Ravi's 1,300 would add up too, but they are two payers' money.
        Only(Line(2000m), Payment(700m, "D", payer: Asha), Payment(1300m, "E", payer: Ravi)).Proposals.ShouldBeEmpty();
    }

    [Fact]
    public void BR1029_a_payment_confirmed_for_one_line_is_offered_to_no_other_line_of_the_run()
    {
        var payment = Payment(500m, "UPI001");
        var sure = Line(500m, "UPI001");
        var other = Line(500m);

        var decisions = StatementMatcher.Decide([sure, other], [payment], 3);

        decisions[sure.Id].Confirmed.ShouldNotBeNull();
        decisions[other.Id].Proposals.ShouldBeEmpty();
    }

    [Fact]
    public void Ten_thousand_lines_against_ten_thousand_payments_are_decided_in_seconds()
    {
        var payments = Enumerable.Range(0, 10_000)
            .Select(index => Payment(100m + index, $"UTR{index:0000000000}", daysBefore: index % 3, payer: Guid.NewGuid(), number: $"PAY/26-27/{index:000000}"))
            .ToList();
        var lines = payments.Select((payment, index) => index % 2 == 0 ? Line(payment.Amount.Amount, payment.Reference) : Line(payment.Amount.Amount)).ToList();

        var watch = Stopwatch.StartNew();
        var decisions = StatementMatcher.Decide(lines, payments, 3);
        watch.Stop();

        decisions.Values.Count(decision => decision.Confirmed is not null).ShouldBe(5_000);
        decisions.Values.Count(decision => decision.Proposals.Count == 1).ShouldBe(5_000);
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(60), $"took {watch.Elapsed.TotalSeconds:0.0} s");
    }
}
