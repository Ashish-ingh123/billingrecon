using BillingRecon.Application.Common;
using BillingRecon.Domain.Common;
using FluentValidation;

namespace BillingRecon.Application.Tests.Common;

/// <summary>A validator's outcome becomes the error a use case returns: <c>validation_failed</c>, messages per field, named as the JSON names them.</summary>
public sealed class ValidatorExtensionsTests
{
    private readonly SampleValidator _validator = new();

    [Fact]
    public async Task A_valid_instance_has_no_error()
    {
        var error = await _validator.GetErrorAsync(new Sample("Priya", 1, [new SampleLine(10m)]), CancellationToken.None);

        error.ShouldBeNull();
    }

    [Fact]
    public async Task An_invalid_instance_is_validation_failed_with_the_messages_per_field()
    {
        var error = await _validator.GetErrorAsync(new Sample(string.Empty, 0, []), CancellationToken.None);

        error.ShouldNotBeNull();
        error.Code.ShouldBe(CommonErrors.ValidationFailedCode);
        error.Kind.ShouldBe(ErrorKind.Validation);
        error.FieldErrors.Keys.ShouldBe(["fullName", "pageSize"], ignoreOrder: true);
        error.FieldErrors["fullName"].ShouldBe(["Name required."]);
    }

    [Fact]
    public async Task Every_segment_of_a_nested_field_name_is_written_in_camel_case()
    {
        var error = await _validator.GetErrorAsync(new Sample("Priya", 1, [new SampleLine(10m), new SampleLine(-1m)]), CancellationToken.None);

        error.ShouldNotBeNull().FieldErrors.Keys.ShouldBe(["lines[1].unitPrice"]);
    }

    [Fact]
    public async Task Two_rules_with_the_same_message_on_one_field_report_it_once()
    {
        var validator = new InlineValidator<Sample>();
        validator.RuleFor(sample => sample.FullName).NotEmpty().WithMessage("Required.");
        validator.RuleFor(sample => sample.FullName).NotEmpty().WithMessage("Required.");

        var error = await validator.GetErrorAsync(new Sample(string.Empty, 1, []), CancellationToken.None);

        error.ShouldNotBeNull().FieldErrors["fullName"].ShouldBe(["Required."]);
    }

    public sealed record Sample(string FullName, int PageSize, IReadOnlyList<SampleLine> Lines);

    public sealed record SampleLine(decimal UnitPrice);

    private sealed class SampleValidator : AbstractValidator<Sample>
    {
        public SampleValidator()
        {
            RuleFor(sample => sample.FullName).NotEmpty().WithMessage("Name required.");
            RuleFor(sample => sample.PageSize).GreaterThan(0);
            RuleForEach(sample => sample.Lines).ChildRules(line => line.RuleFor(item => item.UnitPrice).GreaterThan(0m));
        }
    }
}
