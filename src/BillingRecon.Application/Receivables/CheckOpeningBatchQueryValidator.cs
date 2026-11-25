using FluentValidation;

namespace BillingRecon.Application.Receivables;

/// <summary>The shape of a dry run: a cut-over date.</summary>
public sealed class CheckOpeningBatchQueryValidator : AbstractValidator<CheckOpeningBatchQuery>
{
    /// <summary>Creates the validator.</summary>
    public CheckOpeningBatchQueryValidator()
        => RuleFor(query => query.CutOverDate).CutOverDate();
}
