using FluentValidation;

namespace BillingRecon.Application.Billing;

/// <summary>The shape of <see cref="ReplaceCreditNoteLinesCommand"/>: the version the client read, and lines that are each valid.</summary>
public sealed class ReplaceCreditNoteLinesCommandValidator : AbstractValidator<ReplaceCreditNoteLinesCommand>
{
    public ReplaceCreditNoteLinesCommandValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(command => command.ExpectedVersion).NotEmpty().WithMessage("The version you read is required (If-Match).");
        this.CreditNoteLines(command => command.Lines);
    }
}
