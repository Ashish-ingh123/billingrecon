using BillingRecon.Domain.Billing;

namespace BillingRecon.Domain.Tests.Billing;

/// <summary>The code of a service item: kept as entered, ASCII only, and the same code whatever the case of its letters.</summary>
public sealed class ServiceItemCodeTests
{
    [Theory]
    [InlineData("CONS-OPD")]
    [InlineData("LAB/CBC")]
    [InlineData("xray_chest.pa")]
    [InlineData("7")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123")]
    public void A_code_of_letters_digits_and_the_four_marks_is_accepted_as_entered(string value)
    {
        ServiceItemCode.TryOf(value, out var code).ShouldBeTrue();

        code.Value.ShouldBe(value);
        code.ToString().ShouldBe(value);
        code.IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void The_surrounding_white_space_is_not_part_of_the_code()
    {
        ServiceItemCode.Of("  CONS-OPD ").Value.ShouldBe("CONS-OPD");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("CONS OPD")]
    [InlineData("CONS,OPD")]
    [InlineData("परामर्श")]
    [InlineData("CONS\tOPD")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ01234")]
    public void A_blank_code_one_with_other_characters_and_one_longer_than_30_are_refused(string? value)
    {
        ServiceItemCode.TryOf(value, out var code).ShouldBeFalse();

        code.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Of_throws_for_a_value_that_is_not_a_code_and_names_no_value()
    {
        var exception = Should.Throw<ArgumentException>(() => ServiceItemCode.Of("CONS OPD"));

        exception.ParamName.ShouldBe("value");
        exception.Message.ShouldNotContain("CONS OPD");
    }

    [Fact]
    public void Two_codes_that_differ_only_in_case_are_the_same_code()
    {
        var upper = ServiceItemCode.Of("LAB/CBC");
        var lower = ServiceItemCode.Of("lab/cbc");

        upper.ShouldBe(lower);
        (upper == lower).ShouldBeTrue();
        upper.GetHashCode().ShouldBe(lower.GetHashCode());
        upper.ShouldNotBe(ServiceItemCode.Of("LAB/CBC2"));
    }

    [Fact]
    public void The_default_value_is_empty_and_is_not_a_code()
    {
        default(ServiceItemCode).IsEmpty.ShouldBeTrue();
        default(ServiceItemCode).Value.ShouldBe(string.Empty);
    }
}
