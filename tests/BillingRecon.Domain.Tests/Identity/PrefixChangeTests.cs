using System.Reflection;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Domain.Tests.Identity;

/// <summary>
/// The prefix change (BR-802, ADR 0033): asked for from the type's prefix to another one, decided once, approved or
/// rejected with a reason; and the series' own change of prefix, which an approval makes.
/// </summary>
public sealed class PrefixChangeTests
{
    private static readonly DateTimeOffset DecidedAt = new(2026, 11, 21, 9, 15, 0, TimeSpan.Zero);

    [Fact]
    public void BR802_a_change_is_asked_for_from_the_types_prefix_to_another_and_waits()
    {
        var change = PrefixChange.Request(DocumentType.Bill, "BIL", "INV");

        (change.DocumentType, change.FromPrefix, change.ToPrefix, change.Status, change.DecidedAtUtc)
            .ShouldBe((DocumentType.Bill, "BIL", "INV", PrefixChangeStatus.Pending, (DateTimeOffset?)null));
        change.CheckIsPending().IsSuccess.ShouldBeTrue();
        change.DomainEvents.ShouldHaveSingleItem().ShouldBe(new PrefixChangeRequested(DocumentType.Bill, "BIL", "INV"));
    }

    [Fact]
    public void A_change_of_no_type_to_no_prefix_or_to_the_same_one_is_a_bug_in_the_caller()
    {
        Should.Throw<ArgumentException>(() => PrefixChange.Request((DocumentType)42, "BIL", "INV"));
        Should.Throw<ArgumentException>(() => PrefixChange.Request(DocumentType.Bill, "BIL", "inv"));
        Should.Throw<ArgumentException>(() => PrefixChange.Request(DocumentType.Bill, "BIL", "INVO"));
        Should.Throw<ArgumentException>(() => PrefixChange.Request(DocumentType.Bill, "BIL", "BIL"));
    }

    [Fact]
    public void BR606_an_approval_is_recorded_once_and_then_the_change_is_decided()
    {
        var change = PrefixChange.Request(DocumentType.Payment, "PAY", "RCP");
        change.ClearDomainEvents();

        change.RecordApproval(DecidedAt).IsSuccess.ShouldBeTrue();

        (change.Status, change.DecidedAtUtc).ShouldBe((PrefixChangeStatus.Approved, (DateTimeOffset?)DecidedAt));
        change.DomainEvents.ShouldHaveSingleItem().ShouldBe(new PrefixChangeApproved(change.Id, DocumentType.Payment, "PAY", "RCP"));
        change.RecordApproval(DecidedAt).Error.Code.ShouldBe(IdentityErrors.PrefixChangeNotPendingCode);
        change.RecordRejection("Too late", DecidedAt).Error.Code.ShouldBe(IdentityErrors.PrefixChangeNotPendingCode);
        change.CheckIsPending().Error.Code.ShouldBe(IdentityErrors.PrefixChangeNotPendingCode);
    }

    [Fact]
    public void BR605_a_rejection_says_why_and_changes_nothing_else()
    {
        var change = PrefixChange.Request(DocumentType.Refund, "REF", "RFD");
        change.ClearDomainEvents();

        change.RecordRejection(" Printed stationery says REF ", DecidedAt).IsSuccess.ShouldBeTrue();

        change.Status.ShouldBe(PrefixChangeStatus.Rejected);
        var rejected = change.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<PrefixChangeRejected>();
        rejected.Reason.ShouldBe("Printed stationery says REF");
        Should.Throw<ArgumentException>(() => PrefixChange.Request(DocumentType.Refund, "REF", "RFD").RecordRejection("  ", DecidedAt));
    }

    [Fact]
    public void BR802_a_series_takes_the_new_prefix_for_its_next_numbers_and_keeps_its_count()
    {
        var series = Series("BIL", nextNumber: 124);

        series.ChangePrefix("INV", DecidedAt);

        (series.Prefix, series.NextNumber, series.UpdatedAtUtc).ShouldBe(("INV", 124, DecidedAt));
        var changed = series.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<NumberSeriesPrefixChanged>();
        (changed.FromPrefix, changed.ToPrefix).ShouldBe(("BIL", "INV"));
        Should.Throw<ArgumentException>(() => series.ChangePrefix("INV", DecidedAt));
        Should.Throw<ArgumentException>(() => series.ChangePrefix("in", DecidedAt));
    }

    // A series as the database holds one: nothing in the application creates it (ADR 0008).
    private static NumberSeries Series(string prefix, int nextNumber)
    {
        var series = (NumberSeries)Activator.CreateInstance(typeof(NumberSeries), BindingFlags.Instance | BindingFlags.NonPublic, binder: null, [prefix], culture: null)!;
        typeof(NumberSeries).GetProperty(nameof(NumberSeries.NextNumber))!.SetValue(series, nextNumber);

        return series;
    }
}
