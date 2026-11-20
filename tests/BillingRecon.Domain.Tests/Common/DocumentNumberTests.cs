using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Tests.Common;

/// <summary>
/// The number of a posted document (BR-801, ADR 0008): <c>PREFIX/YY-YY/NNNNNN</c>, at most sixteen characters as
/// CGST rule 46(b) requires.
/// </summary>
public sealed class DocumentNumberTests
{
    private static readonly FiscalYear Fy2026 = FiscalYear.Of(2026);

    [Theory]
    [InlineData(DocumentType.Bill, "BIL", 123, "BIL/26-27/000123")]
    [InlineData(DocumentType.Bill, "BIL", 1, "BIL/26-27/000001")]
    [InlineData(DocumentType.Bill, "BIL", 999_999, "BIL/26-27/999999")]
    [InlineData(DocumentType.CreditNote, "CN", 42, "CN/26-27/000042")]
    [InlineData(DocumentType.Payment, "PAY", 7, "PAY/26-27/000007")]
    [InlineData(DocumentType.Adjustment, "ADJ", 10, "ADJ/26-27/000010")]
    [InlineData(DocumentType.Refund, "REF", 100_000, "REF/26-27/100000")]
    [InlineData(DocumentType.Bill, "B", 5, "B/26-27/000005")]
    [InlineData(DocumentType.Bill, "9Z", 5, "9Z/26-27/000005")]
    public void BR801_a_number_is_the_prefix_the_short_fiscal_year_and_the_sequence_padded_to_six_digits(DocumentType type, string prefix, int sequence, string expected)
    {
        var number = DocumentNumber.Create(type, Fy2026, prefix, sequence);

        number.Value.ShouldBe(expected);
        number.ToString().ShouldBe(expected);
        number.Type.ShouldBe(type);
        number.FiscalYear.ShouldBe(Fy2026);
        number.Prefix.ShouldBe(prefix);
        number.Sequence.ShouldBe(sequence);
        number.Value.Length.ShouldBeLessThanOrEqualTo(DocumentNumber.MaxLength);
    }

    [Fact]
    public void BR801_the_longest_ordinary_number_is_exactly_sixteen_characters()
    {
        DocumentNumber.Create(DocumentType.Bill, Fy2026, "BIL", 999_999).Value.Length.ShouldBe(16);
    }

    [Fact]
    public void The_fiscal_year_in_the_number_is_the_one_of_the_series_also_across_a_century()
    {
        DocumentNumber.Create(DocumentType.Bill, FiscalYear.Of(2099), "BIL", 1).Value.ShouldBe("BIL/99-00/000001");
    }

    [Fact]
    public void Rule46_a_number_that_would_be_longer_than_sixteen_characters_cannot_be_built()
    {
        // The millionth number widens the sequence to seven digits: with a three-character prefix that is seventeen.
        var exception = Should.Throw<ArgumentException>(() => DocumentNumber.Create(DocumentType.Bill, Fy2026, "BIL", 1_000_000));

        exception.ParamName.ShouldBe("sequence");
        exception.Message.ShouldContain("17 characters");
    }

    [Theory]
    [InlineData("CN", 1_000_000, "CN/26-27/1000000")] // sixteen
    [InlineData("CN", 9_999_999, "CN/26-27/9999999")]
    [InlineData("B", 10_000_000, "B/26-27/10000000")]
    public void A_shorter_prefix_leaves_room_for_a_longer_sequence(string prefix, int sequence, string expected)
    {
        DocumentNumber.Create(DocumentType.CreditNote, Fy2026, prefix, sequence).Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("BILL")] // four characters
    [InlineData("bil")] // lower case
    [InlineData("B-L")]
    [InlineData("B L")]
    [InlineData("Bé")]
    [InlineData("B/")]
    public void BR802_a_prefix_is_one_to_three_capital_letters_or_digits(string prefix)
    {
        DocumentNumber.IsPrefix(prefix).ShouldBeFalse();
        Should.Throw<ArgumentException>(() => DocumentNumber.Create(DocumentType.Bill, Fy2026, prefix, 1)).ParamName.ShouldBe("prefix");
    }

    [Theory]
    [InlineData("B")]
    [InlineData("CN")]
    [InlineData("BIL")]
    [InlineData("007")]
    [InlineData("A1Z")]
    public void BR802_these_are_prefixes(string prefix)
    {
        DocumentNumber.IsPrefix(prefix).ShouldBeTrue();
        DocumentNumber.IsPrefix(null).ShouldBeFalse();
    }

    [Fact]
    public void A_sequence_below_one_a_default_fiscal_year_and_an_undefined_type_are_bugs_and_throw()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => DocumentNumber.Create(DocumentType.Bill, Fy2026, "BIL", 0));
        Should.Throw<ArgumentOutOfRangeException>(() => DocumentNumber.Create(DocumentType.Bill, Fy2026, "BIL", -1));
        Should.Throw<ArgumentException>(() => DocumentNumber.Create(DocumentType.Bill, default, "BIL", 1)).ParamName.ShouldBe("fiscalYear");
        Should.Throw<ArgumentException>(() => DocumentNumber.Create((DocumentType)42, Fy2026, "BIL", 1)).ParamName.ShouldBe("type");
    }

    [Fact]
    public void Two_numbers_are_equal_when_every_part_is()
    {
        var number = DocumentNumber.Create(DocumentType.Bill, Fy2026, "BIL", 123);

        number.ShouldBe(DocumentNumber.Create(DocumentType.Bill, Fy2026, "BIL", 123));
        number.ShouldNotBe(DocumentNumber.Create(DocumentType.Bill, Fy2026, "BIL", 124));
        number.ShouldNotBe(DocumentNumber.Create(DocumentType.Bill, FiscalYear.Of(2027), "BIL", 123));

        // The same text, another kind of document: a bill number is never a payment number.
        number.ShouldNotBe(DocumentNumber.Create(DocumentType.Payment, Fy2026, "BIL", 123));
    }
}
