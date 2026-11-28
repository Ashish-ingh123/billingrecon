using BillingRecon.Domain.Common;
using BillingRecon.Domain.Reconciliation;

namespace BillingRecon.Domain.Tests.Reconciliation;

/// <summary>
/// A match, a proposal and a run (BR-1006, BR-1007, BR-1011, BR-1029; ADR 0066): a match adds up to the paisa and names a
/// line or an item once; a proposal is withdrawn only while open; a run is finished once and goes stale after a while.
/// </summary>
public sealed class MatchTests
{
    private static readonly SettlementChannelId Channel = new(Guid.NewGuid());
    private static readonly StatementLineId Line = new(Guid.NewGuid());

    [Fact]
    public void BR1006_a_match_joins_lines_to_items_whose_amounts_agree_and_is_active()
    {
        var match = Match.Make(Channel, MatchItemType.Payment, MatchSource.Automatic, "The reference agrees.", [(Line, Money.Of(2000m))], [(Guid.NewGuid(), Money.Of(1200m)), (Guid.NewGuid(), Money.Of(800m))]);

        (match.Status, match.Kind, match.Source).ShouldBe((MatchStatus.Active, MatchItemType.Payment, MatchSource.Automatic));
        match.Lines.ShouldHaveSingleItem().IsActive.ShouldBeTrue();
        match.Items.Count.ShouldBe(2);
        match.Items.ShouldAllBe(item => item.IsActive && item.ItemType == MatchItemType.Payment);
        match.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<MatchMade>().Total.ShouldBe(Money.Of(2000m));
    }

    [Fact]
    public void BR1006_a_match_whose_amounts_differ_by_a_paisa_or_that_names_an_item_twice_is_refused()
    {
        var item = Guid.NewGuid();

        Should.Throw<ArgumentException>(() => Match.Make(Channel, MatchItemType.Payment, MatchSource.Manual, "x", [(Line, Money.Of(500m))], [(item, Money.Of(499.99m))]))
            .Message.ShouldContain("to the paisa");
        Should.Throw<ArgumentException>(() => Match.Make(Channel, MatchItemType.Payment, MatchSource.Manual, "x", [(Line, Money.Of(500m))], [(item, Money.Of(250m)), (item, Money.Of(250m))]));
        Should.Throw<ArgumentException>(() => Match.Make(Channel, MatchItemType.Payment, MatchSource.Manual, "x", [], [(item, Money.Of(250m))]));
    }

    [Fact]
    public void BR1011_a_proposal_is_open_and_a_later_run_withdraws_it_once()
    {
        var proposal = MatchProposal.Propose(Channel, Line, MatchItemType.Payment, 2, 70, "The amount agrees.", [(Guid.NewGuid(), Money.Of(500m))]);
        proposal.Status.ShouldBe(MatchProposalStatus.Open);
        proposal.DomainEvents.ShouldBeEmpty("a run's proposals are bookkeeping, not audited one by one");

        proposal.Withdraw();
        proposal.Withdraw();
        proposal.Status.ShouldBe(MatchProposalStatus.Withdrawn);

        Should.Throw<ArgumentException>(() => MatchProposal.Propose(Channel, Line, MatchItemType.Payment, 4, 70, "x", [(Guid.NewGuid(), Money.Of(1m))]));
        Should.Throw<ArgumentException>(() => MatchProposal.Propose(Channel, Line, MatchItemType.Payment, 1, 101, "x", [(Guid.NewGuid(), Money.Of(1m))]));
    }

    [Fact]
    public void BR1029_a_run_is_finished_once_and_one_left_running_goes_stale()
    {
        var run = MatchingRun.Start(Channel);
        run.Status.ShouldBe(MatchingRunStatus.Running);
        run.IsStale(run.CreatedAtUtc + TimeSpan.FromMinutes(14)).ShouldBeFalse();
        run.IsStale(run.CreatedAtUtc + TimeSpan.FromMinutes(16)).ShouldBeTrue();

        run.Finish(10, 5, 3, 0);
        (run.Status, run.LinesRead, run.Matched, run.Proposed).ShouldBe((MatchingRunStatus.Finished, 10, 5, 3));
        Should.Throw<InvalidOperationException>(() => run.Finish(1, 1, 0, 0));
        run.IsStale(run.CreatedAtUtc + TimeSpan.FromHours(1)).ShouldBeFalse("a finished run holds nothing");
    }

    [Fact]
    public void BR1006_a_line_joins_a_match_once()
    {
        var line = StatementLine.Read(Channel, new StatementImportId(Guid.NewGuid()), 2, new StatementLineValues(new DateOnly(2026, 10, 16), null, StatementDirection.In, Money.Of(500m), "UPI001", "UPI CR", null), "x", 1);

        line.MarkMatched();
        line.Status.ShouldBe(StatementLineStatus.Matched);
        Should.Throw<InvalidOperationException>(line.MarkMatched);
    }
}
