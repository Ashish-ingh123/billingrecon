using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Tests.Common;

public sealed class ErrorTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("not_found")]
    [InlineData("bill_already_posted")]
    [InlineData("idempotency_key_reuse")]
    [InlineData("http_418")]
    public void Snake_case_codes_are_accepted(string code)
    {
        var error = new Error(code, "Something happened.", ErrorKind.BusinessRule);

        error.Code.ShouldBe(code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("BillAlreadyPosted")]
    [InlineData("Bill_already_posted")]
    [InlineData("bill-already-posted")]
    [InlineData("bill already posted")]
    [InlineData("bill.posted")]
    [InlineData("_bill")]
    [InlineData("bill_")]
    [InlineData("bill__posted")]
    [InlineData("9_lives")]
    public void Codes_that_are_not_snake_case_are_rejected(string? code)
    {
        var exception = Should.Throw<ArgumentException>(() => new Error(code!, "Something happened.", ErrorKind.BusinessRule));

        exception.ParamName.ShouldBe("code");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_message_is_rejected(string? message)
    {
        Should.Throw<ArgumentException>(() => new Error("bill_already_posted", message!, ErrorKind.BusinessRule));
    }

    [Fact]
    public void An_undefined_kind_is_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new Error("bill_already_posted", "Already posted.", (ErrorKind)99));
    }

    [Fact]
    public void Each_factory_sets_its_kind()
    {
        Error.Validation("amount_invalid", "m").Kind.ShouldBe(ErrorKind.Validation);
        Error.NotFound("bill_not_found", "m").Kind.ShouldBe(ErrorKind.NotFound);
        Error.Forbidden("checker_is_maker", "m").Kind.ShouldBe(ErrorKind.Forbidden);
        Error.Conflict("concurrency_conflict", "m").Kind.ShouldBe(ErrorKind.Conflict);
        Error.BusinessRule("bill_already_posted", "m").Kind.ShouldBe(ErrorKind.BusinessRule);
    }

    [Fact]
    public void Only_a_validation_error_carries_field_errors()
    {
        var fieldErrors = new Dictionary<string, string[]> { ["amount"] = ["Must be greater than zero."] };

        Error.Validation("amount_invalid", "The amount is invalid.", fieldErrors).FieldErrors["amount"].ShouldBe(["Must be greater than zero."]);
        Error.Validation("amount_invalid", "The amount is invalid.").FieldErrors.ShouldBeEmpty();
        Error.BusinessRule("bill_already_posted", "Already posted.").FieldErrors.ShouldBeEmpty();
    }

    [Fact]
    public void Field_errors_are_copied_so_later_changes_by_the_caller_do_not_reach_the_error()
    {
        var messages = new[] { "Must be greater than zero." };
        var fieldErrors = new Dictionary<string, string[]> { ["amount"] = messages };

        var error = Error.Validation("amount_invalid", "The amount is invalid.", fieldErrors);
        messages[0] = "changed";
        fieldErrors["other"] = ["added"];

        error.FieldErrors.Count.ShouldBe(1);
        error.FieldErrors["amount"].ShouldBe(["Must be greater than zero."]);
    }

    [Fact]
    public void Errors_with_the_same_content_are_equal()
    {
        var first = Error.Validation("amount_invalid", "The amount is invalid.", new Dictionary<string, string[]> { ["amount"] = ["Too small."] });
        var second = Error.Validation("amount_invalid", "The amount is invalid.", new Dictionary<string, string[]> { ["amount"] = ["Too small."] });

        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Fact]
    public void Errors_that_differ_in_code_kind_message_or_field_errors_are_not_equal()
    {
        var error = Error.Validation("amount_invalid", "The amount is invalid.", new Dictionary<string, string[]> { ["amount"] = ["Too small."] });

        error.ShouldNotBe(Error.Validation("amount_missing", "The amount is invalid.", new Dictionary<string, string[]> { ["amount"] = ["Too small."] }));
        error.ShouldNotBe(Error.Validation("amount_invalid", "Another message.", new Dictionary<string, string[]> { ["amount"] = ["Too small."] }));
        error.ShouldNotBe(Error.Validation("amount_invalid", "The amount is invalid.", new Dictionary<string, string[]> { ["amount"] = ["Too large."] }));
        error.ShouldNotBe(Error.Validation("amount_invalid", "The amount is invalid."));
        Error.Conflict("stale", "m").ShouldNotBe(Error.BusinessRule("stale", "m"));
    }
}
