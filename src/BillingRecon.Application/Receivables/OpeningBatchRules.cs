using FluentValidation;

namespace BillingRecon.Application.Receivables;

internal static class OpeningBatchRules
{
    private static readonly DateOnly Earliest = new(2000, 4, 1);
    private static readonly DateOnly Latest = new(2099, 4, 1);

    public static IRuleBuilderOptions<T, string> ExpectedVersion<T>(this IRuleBuilder<T, string> rule)
        => rule.NotEmpty().WithMessage("The version you read is required (If-Match).");

    // The date decides the fiscal year of the opening numbers, so a slip of the hand must not pass.
    public static IRuleBuilderOptions<T, DateOnly?> CutOverDate<T>(this IRuleBuilder<T, DateOnly?> rule)
        => rule
            .NotNull().WithMessage("The cut-over date is required: the first day on which documents are raised here.")
            .Must(date => date is null || (date >= Earliest && date <= Latest)).WithMessage("The cut-over date is between 2000-04-01 and 2099-04-01.");
}
