using System.Reflection;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Reconciliation;

namespace BillingRecon.Domain.Tests.Reconciliation;

/// <summary>
/// A statement line and an import (BR-1003 to BR-1005; ADR 0065): a line keeps what was read and nothing changes it; its
/// fingerprint is the same for the same values whatever the file; an import is insert-only and accounts for every row.
/// </summary>
public sealed class StatementLineTests
{
    private static readonly SettlementChannelId Channel = new(Guid.NewGuid());

    private static StatementLineValues Values() => new(
        new DateOnly(2026, 11, 27), null, StatementDirection.In, Money.Of(1500m), "UTR8812", "NEFT FROM R KUMAR", Money.Of(152300.50m));

    [Fact]
    public void BR1004_a_line_is_unmatched_and_keeps_what_was_read()
    {
        var line = StatementLine.Read(Channel, new StatementImportId(Guid.NewGuid()), 7, Values(), "27/11/2026,NEFT FROM R KUMAR,UTR8812,,1500.00,152300.50", 1);

        (line.TransactionDate, line.Direction, line.Amount, line.Reference, line.Description, line.Balance, line.RowNumber, line.Occurrence, line.Status).ShouldBe(
            (new DateOnly(2026, 11, 27), StatementDirection.In, Money.Of(1500m), "UTR8812", "NEFT FROM R KUMAR", (Money?)Money.Of(152300.50m), 7, 1, StatementLineStatus.Unmatched));
        line.Fingerprint.ShouldBe(StatementLine.FingerprintOf(Channel, Values()));
        line.Fingerprint.Length.ShouldBe(StatementLine.FingerprintLength);
    }

    [Fact]
    public void BR1004_no_read_field_of_a_line_can_be_set_from_outside_it()
    {
        var readFields = new[]
        {
            nameof(StatementLine.ChannelId), nameof(StatementLine.ImportId), nameof(StatementLine.RowNumber), nameof(StatementLine.TransactionDate),
            nameof(StatementLine.ValueDate), nameof(StatementLine.Direction), nameof(StatementLine.Amount), nameof(StatementLine.Reference),
            nameof(StatementLine.Description), nameof(StatementLine.Balance), nameof(StatementLine.RawText), nameof(StatementLine.Fingerprint),
            nameof(StatementLine.Occurrence),
        };

        foreach (var name in readFields)
        {
            typeof(StatementLine).GetProperty(name)!.SetMethod!.IsPublic.ShouldBeFalse(name);
        }

        // Its only command changes its status (13.2): nothing changes a read field.
        typeof(StatementLine).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .ShouldBe([nameof(StatementLine.MarkMatched)]);
    }

    [Fact]
    public void BR1004_the_same_values_have_the_same_fingerprint_and_any_difference_another()
    {
        var fingerprint = StatementLine.FingerprintOf(Channel, Values());

        StatementLine.FingerprintOf(Channel, Values() with { Description = "  NEFT FROM R KUMAR " }).ShouldBe(fingerprint, "spaces around a value are not part of it");
        StatementLine.FingerprintOf(new SettlementChannelId(Guid.NewGuid()), Values()).ShouldNotBe(fingerprint, "another channel");
        StatementLine.FingerprintOf(Channel, Values() with { Amount = Money.Of(1500.01m) }).ShouldNotBe(fingerprint);
        StatementLine.FingerprintOf(Channel, Values() with { Direction = StatementDirection.Out }).ShouldNotBe(fingerprint);
        StatementLine.FingerprintOf(Channel, Values() with { Balance = null }).ShouldNotBe(fingerprint);
        StatementLine.FingerprintOf(Channel, Values() with { TransactionDate = new DateOnly(2026, 11, 26) }).ShouldNotBe(fingerprint);
        StatementLine.FingerprintOf(Channel, Values() with { ValueDate = new DateOnly(2026, 11, 28) }).ShouldBe(fingerprint, "the value date is not part of it");
    }

    [Fact]
    public void BR1005_a_line_has_an_amount_above_zero()
        => Should.Throw<ArgumentException>(() => StatementLine.Read(Channel, new StatementImportId(Guid.NewGuid()), 2, Values() with { Amount = Money.Zero }, "x", 1));

    [Fact]
    public void BR1003_an_import_is_insert_only_and_accounts_for_every_row_once()
    {
        typeof(IAppendOnly).IsAssignableFrom(typeof(StatementImport)).ShouldBeTrue("an import is a fact and never changes");

        var import = StatementImport.Record(Channel, "hdfc-nov.csv", new string('A', 64), 10, 6, 3, 1, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 27));
        (import.RowsRead, import.LinesAdded, import.LinesAlreadyHeld, import.RowsRefused).ShouldBe((10, 6, 3, 1));
        import.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<StatementImportRecorded>().LinesAdded.ShouldBe(6);

        Should.Throw<ArgumentException>(() => StatementImport.Record(Channel, "x.csv", new string('A', 64), 10, 6, 3, 0, null, null));
        Should.Throw<ArgumentException>(() => StatementImport.Record(Channel, "x.csv", "not a hash", 0, 0, 0, 0, null, null));
    }
}
