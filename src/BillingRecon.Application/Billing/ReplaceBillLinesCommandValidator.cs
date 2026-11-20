using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>The shape of <see cref="ReplaceBillLinesCommand"/>: the version the client read, and lines that are each valid.</summary>
public sealed class ReplaceBillLinesCommandValidator : AbstractValidator<ReplaceBillLinesCommand>
{
    public ReplaceBillLinesCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.ExpectedVersion).NotEmpty().WithMessage("The version you read is required (If-Match).");

        this.Lines(command => command.Lines);
    }
}
