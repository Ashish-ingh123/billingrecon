using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Tests.Common;

public sealed class ResultTests
{
    private static readonly Error AlreadyPosted = Error.BusinessRule("bill_already_posted", "The bill is already posted.");

    [Fact]
    public void A_success_has_no_error()
    {
        var result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void A_failure_carries_its_error()
    {
        var result = Result.Failure(AlreadyPosted);

        result.IsSuccess.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(AlreadyPosted);
    }

    [Fact]
    public void A_failure_needs_an_error()
    {
        Should.Throw<ArgumentNullException>(() => Result.Failure(null!));
        Should.Throw<ArgumentNullException>(() => Result.Failure<int>(null!));
    }

    [Fact]
    public void A_success_with_a_value_exposes_the_value()
    {
        var result = Result.Success(42);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void A_success_cannot_carry_null()
    {
        Should.Throw<ArgumentNullException>(() => Result.Success<string>(null!));
    }

    [Fact]
    public void Reading_the_value_of_a_failure_is_a_bug_and_names_the_error()
    {
        var result = Result.Failure<int>(AlreadyPosted);

        var exception = Should.Throw<InvalidOperationException>(() => { _ = result.Value; });

        result.IsFailure.ShouldBeTrue();
        exception.Message.ShouldContain("bill_already_posted");
    }

    [Fact]
    public void A_value_or_an_error_converts_to_a_result_implicitly()
    {
        Result<int> success = 42;
        Result<int> failure = AlreadyPosted;
        Result plainFailure = AlreadyPosted;

        success.Value.ShouldBe(42);
        failure.Error.ShouldBe(AlreadyPosted);
        plainFailure.Error.ShouldBe(AlreadyPosted);
    }

    [Fact]
    public void A_result_with_a_value_can_be_handled_as_a_plain_result()
    {
        Result result = Result.Failure<int>(AlreadyPosted);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("bill_already_posted");
    }
}
