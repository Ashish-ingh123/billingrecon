using BillingRecon.Domain.Payers;

namespace BillingRecon.Domain.Tests.Payers;

/// <summary>The medical record number (BR-101): kept as entered, 1 to 30 characters, compared without regard to case.</summary>
public sealed class MrnTests
{
    [Theory]
    [InlineData("MRN-000123", "MRN-000123")]
    [InlineData("  12345  ", "12345")]
    [InlineData("A", "A")]
    [InlineData("HIS/2026/0042", "HIS/2026/0042")]
    [InlineData("ward 7 bed 12", "ward 7 bed 12")]
    public void BR101_an_mrn_is_kept_as_entered_without_the_surrounding_white_space(string input, string expected)
    {
        var mrn = Mrn.Of(input);

        mrn.Value.ShouldBe(expected);
        mrn.ToString().ShouldBe(expected);
        mrn.IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void An_mrn_has_at_most_thirty_characters()
    {
        Mrn.TryOf(new string('7', Mrn.MaxLength), out _).ShouldBeTrue();
        Mrn.TryOf(new string('7', Mrn.MaxLength + 1), out _).ShouldBeFalse();

        // Surrounding white space does not count.
        Mrn.TryOf($"  {new string('7', Mrn.MaxLength)}  ", out _).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("MRN\t1")]
    [InlineData("MRN\n1")]
    [InlineData("\u0000")]
    public void TryOf_refuses_what_is_not_an_mrn(string? input)
    {
        Mrn.TryOf(input, out var mrn).ShouldBeFalse();

        mrn.ShouldBe(default);
    }

    [Fact]
    public void Of_throws_for_what_is_not_an_mrn_and_does_not_repeat_the_value()
    {
        var input = $"MRN-{new string('9', Mrn.MaxLength)}";

        var exception = Should.Throw<ArgumentException>(() => Mrn.Of(input));

        exception.ParamName.ShouldBe("value");
        exception.Message.ShouldNotContain(input);
    }

    [Fact]
    public void BR101_two_mrns_that_differ_only_in_case_are_the_same_mrn()
    {
        var upper = Mrn.Of("MRN-AB12");
        var lower = Mrn.Of("mrn-ab12");

        upper.ShouldBe(lower);
        (upper == lower).ShouldBeTrue();
        upper.GetHashCode().ShouldBe(lower.GetHashCode());

        // Each keeps the text it was entered with.
        lower.Value.ShouldBe("mrn-ab12");
    }

    [Fact]
    public void Different_mrns_are_not_equal()
    {
        Mrn.Of("MRN-1").ShouldNotBe(Mrn.Of("MRN-2"));
        (Mrn.Of("MRN-1") != Mrn.Of("MRN-2")).ShouldBeTrue();
    }

    [Fact]
    public void The_default_value_is_empty_and_is_not_an_mrn()
    {
        default(Mrn).IsEmpty.ShouldBeTrue();
        default(Mrn).Value.ShouldBe(string.Empty);
        default(Mrn).ShouldNotBe(Mrn.Of("MRN-1"));
    }
}
