using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Tests.Common;

/// <summary>The Indian fiscal year (BR-004): 1 April to 31 March, labelled by its two calendar years.</summary>
public sealed class FiscalYearTests
{
    [Theory]
    [InlineData(2026, 4, 1, 2026)] // the first day
    [InlineData(2026, 12, 31, 2026)]
    [InlineData(2027, 1, 1, 2026)] // the calendar year turns, the fiscal year does not
    [InlineData(2027, 3, 31, 2026)] // the last day
    [InlineData(2027, 4, 1, 2027)] // the first day of the next one
    [InlineData(2026, 3, 31, 2025)]
    [InlineData(2028, 2, 29, 2027)] // a leap day
    [InlineData(1900, 4, 1, 1900)]
    public void BR004_a_date_from_april_belongs_to_the_year_it_is_in_and_a_date_before_april_to_the_year_before(int year, int month, int day, int startYear)
    {
        FiscalYear.FromDate(new DateOnly(year, month, day)).StartYear.ShouldBe(startYear);
    }

    [Fact]
    public void BR004_the_fiscal_year_runs_from_the_first_of_april_to_the_thirty_first_of_march()
    {
        var fiscalYear = FiscalYear.Of(2026);

        fiscalYear.Start.ShouldBe(new DateOnly(2026, 4, 1));
        fiscalYear.End.ShouldBe(new DateOnly(2027, 3, 31));
        fiscalYear.Contains(new DateOnly(2026, 4, 1)).ShouldBeTrue();
        fiscalYear.Contains(new DateOnly(2027, 3, 31)).ShouldBeTrue();
        fiscalYear.Contains(new DateOnly(2026, 3, 31)).ShouldBeFalse();
        fiscalYear.Contains(new DateOnly(2027, 4, 1)).ShouldBeFalse();
    }

    [Fact]
    public void Every_day_of_a_fiscal_year_maps_back_to_it_and_the_days_around_it_do_not()
    {
        var fiscalYear = FiscalYear.Of(2027); // contains 29 February 2028

        for (var day = fiscalYear.Start; day <= fiscalYear.End; day = day.AddDays(1))
        {
            FiscalYear.FromDate(day).ShouldBe(fiscalYear);
        }

        fiscalYear.End.DayNumber.ShouldBe(fiscalYear.Start.DayNumber + 365, "a fiscal year with a leap day has 366 days");
        FiscalYear.FromDate(fiscalYear.Start.AddDays(-1)).ShouldBe(FiscalYear.Of(2026));
        FiscalYear.FromDate(fiscalYear.End.AddDays(1)).ShouldBe(FiscalYear.Of(2028));
    }

    [Theory]
    [InlineData(2026, "2026-27", "26-27")]
    [InlineData(2099, "2099-00", "99-00")] // the century turns inside the fiscal year
    [InlineData(2000, "2000-01", "00-01")]
    [InlineData(2009, "2009-10", "09-10")]
    [InlineData(1900, "1900-01", "00-01")]
    public void BR004_the_label_names_both_years_and_the_short_label_is_what_a_document_number_carries(int startYear, string label, string shortLabel)
    {
        var fiscalYear = FiscalYear.Of(startYear);

        fiscalYear.Label.ShouldBe(label);
        fiscalYear.Label.Length.ShouldBe(FiscalYear.LabelLength);
        fiscalYear.ShortLabel.ShouldBe(shortLabel);
        fiscalYear.ToString().ShouldBe(label);
    }

    [Theory]
    [InlineData(2026)]
    [InlineData(2099)]
    [InlineData(1900)]
    [InlineData(9998)]
    public void A_label_parses_back_to_the_fiscal_year_it_came_from(int startYear)
    {
        var fiscalYear = FiscalYear.Of(startYear);

        FiscalYear.Parse(fiscalYear.Label).ShouldBe(fiscalYear);
        FiscalYear.TryParse(fiscalYear.Label, out var parsed).ShouldBeTrue();
        parsed.ShouldBe(fiscalYear);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026")]
    [InlineData("2026-28")] // the second year does not follow the first
    [InlineData("2026-2027")]
    [InlineData("2026/27")]
    [InlineData("26-27")]
    [InlineData("202a-27")]
    [InlineData(" 2026-27")]
    [InlineData("1899-00")]
    [InlineData("9999-00")]
    public void A_text_that_is_not_a_label_does_not_parse(string? label)
    {
        FiscalYear.TryParse(label, out var fiscalYear).ShouldBeFalse();
        fiscalYear.IsDefault.ShouldBeTrue();

        if (label is not null)
        {
            Should.Throw<FormatException>(() => FiscalYear.Parse(label));
        }
    }

    [Fact]
    public void A_year_outside_what_can_be_named_is_a_bug_and_throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => FiscalYear.Of(1899));
        Should.Throw<ArgumentOutOfRangeException>(() => FiscalYear.Of(9999));
        Should.Throw<ArgumentOutOfRangeException>(() => FiscalYear.FromDate(new DateOnly(1900, 3, 31)));

        // The last fiscal year that has an end is 9998-99; a date from April 9999 has none.
        FiscalYear.FromDate(new DateOnly(9999, 3, 31)).ShouldBe(FiscalYear.Of(9998));
        Should.Throw<ArgumentOutOfRangeException>(() => FiscalYear.FromDate(DateOnly.MaxValue));
    }

    [Fact]
    public void Fiscal_years_are_ordered_by_their_start_and_the_default_value_is_none()
    {
        (FiscalYear.Of(2026) < FiscalYear.Of(2027)).ShouldBeTrue();
        (FiscalYear.Of(2027) >= FiscalYear.Of(2027)).ShouldBeTrue();
        FiscalYear.Of(2026).CompareTo(FiscalYear.Of(2025)).ShouldBeGreaterThan(0);
        FiscalYear.Of(2026).ShouldBe(FiscalYear.FromDate(new DateOnly(2026, 10, 17)));
        default(FiscalYear).IsDefault.ShouldBeTrue();
        FiscalYear.Of(1900).IsDefault.ShouldBeFalse();
    }
}
